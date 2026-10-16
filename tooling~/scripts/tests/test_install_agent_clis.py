"""Agent CLI refresh failures, registry answers, and lock contention must match the lifecycle contract."""
from __future__ import annotations

import importlib.util
import fcntl
import io
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import contextlib
from contextlib import redirect_stderr, redirect_stdout
from typing import Iterator
from unittest import mock

SPEC = importlib.util.spec_from_file_location(
    "install_agent_clis", Path(__file__).parents[3] / ".devcontainer" / "install_agent_clis.py")
assert SPEC is not None and SPEC.loader is not None
installer = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(installer)

DEFAULT_VERSION = "2.1.0"
# Drives one package per run so install paths stay isolated from the OpenCode
# alias checks, which get their own cases.
FOCUSED_PACKAGE = ("@openai/codex@latest",)
FOCUSED_COMMAND = ("codex",)

NPM_STUB = """#!/usr/bin/env bash
case "${1:-}" in
    list) exit "${DXT_FAKE_NPM_LIST_STATUS:-1}" ;;
    view)
        printf '%s' "${DXT_FAKE_NPM_VIEW:-null}"
        exit 0 ;;
    uninstall)
        printf 'uninstall\\n' >> "${DXT_FAKE_BIN_DIR}/npm-calls.txt"
        exit 0 ;;
    install)
        printf 'install\\n' >> "${DXT_FAKE_BIN_DIR}/npm-calls.txt"
        if [ -n "${DXT_FAKE_NPM_INSTALL_VERSION:-}" ]; then
            printf '%s\\n' "${DXT_FAKE_NPM_INSTALL_VERSION}" > "${DXT_FAKE_BIN_DIR}/version-${DXT_FAKE_NPM_INSTALL_CMD}.txt"
        fi
        exit "${DXT_FAKE_NPM_INSTALL_STATUS:-0}" ;;
esac
exit 1
"""

CLI_STUB = """#!/usr/bin/env bash
version_file="${DXT_FAKE_BIN_DIR}/version-${0##*/}.txt"
if [ -f "${version_file}" ]; then
    cat "${version_file}"
else
    printf '%s\\n' "${DXT_FAKE_DEFAULT_VERSION:-2.1.0}"
fi
"""


class ResolverTests(unittest.TestCase):
    def resolved(self, stdout: str, returncode: int = 0) -> str:
        result = subprocess.CompletedProcess([], returncode, stdout=stdout, stderr="")
        with mock.patch.object(installer, "run", return_value=result) as run:
            answer = installer.resolve_latest_version("@opencode/cli@2")
        self.assertEqual(run.call_args.args[0], ["npm", "view", "@opencode/cli@2", "version", "--json"])
        return answer

    def test_unordered_array_picks_highest_and_passes_strings_through(self) -> None:
        cases = [
            ('["2.0.0","2.0.16","2.0.15"]', "2.0.16"),
            ('"2.0.16"', "2.0.16"),
            ('"  2.0.16\\n"', "2.0.16"),
            ('["2.0.16-beta","2.0.15"]', "2.0.15"),
            ('["2.1","2.1.0"]', "2.1.0"),
        ]
        for payload, expected in cases:
            with self.subTest(payload=payload):
                self.assertEqual(self.resolved(payload), expected)

    def test_no_answers_read_as_empty(self) -> None:
        for payload in ("null", "[]", "not json", ""):
            with self.subTest(payload=payload):
                self.assertEqual(self.resolved(payload), "")

    def test_failed_and_unlaunchable_npm_read_as_empty(self) -> None:
        self.assertEqual(self.resolved('["2.0.16"]', returncode=1), "")
        for failure in (OSError("cannot launch"), subprocess.TimeoutExpired("npm", 20)):
            with self.subTest(failure=failure), mock.patch.object(installer, "run", side_effect=failure):
                self.assertEqual(installer.resolve_latest_version("@opencode/cli@2"), "")

    def test_sort_key_matches_registry_semantics(self) -> None:
        for lower, higher in (("2.0.15", "2.0.16"), ("2.0.16-beta", "2.0.16"), ("2.1", "2.1.0"),
                              ("9.0.0", "10.0.0")):
            with self.subTest(lower=lower, higher=higher):
                self.assertLess(installer.version_sort_key(lower), installer.version_sort_key(higher))
        # A non-numeric component reads as zero, the same on both sides.
        self.assertEqual(installer.version_sort_key("2.0.x"), installer.version_sort_key("2.0.0"))


class CommandVersionTests(unittest.TestCase):
    def test_extracts_the_first_semver_token(self) -> None:
        cases = [("opencode v2.0.16", "2.0.16"), ("1.2.3 and 4.5.6", "1.2.3"),
                 ("codex-cli 0.15.0-alpha.1", "0.15.0-alpha.1")]
        for output, expected in cases:
            with self.subTest(output=output):
                result = subprocess.CompletedProcess([], 0, stdout=f"{output}\n", stderr="")
                with mock.patch.object(installer, "run", return_value=result) as run:
                    self.assertEqual(installer.command_version("opencode"), expected)
                self.assertEqual(run.call_args.args[0], ["opencode", "--version"])

    def test_empty_answers(self) -> None:
        empty = subprocess.CompletedProcess([], 0, stdout="opencode rocks\n", stderr="")
        failed = subprocess.CompletedProcess([], 0, stdout="", stderr="")
        for result in (empty, failed):
            with self.subTest(stdout=result.stdout), mock.patch.object(installer, "run", return_value=result):
                self.assertEqual(installer.command_version("opencode"), "")
        for failure in (OSError("missing"), subprocess.TimeoutExpired("opencode", 10)):
            with self.subTest(failure=failure), mock.patch.object(installer, "run", side_effect=failure):
                self.assertEqual(installer.command_version("opencode"), "")
        self.assertEqual(installer.command_version("bogus"), "")


class RefreshFlowTests(unittest.TestCase):
    def setUp(self) -> None:
        self.work = tempfile.TemporaryDirectory()
        self.addCleanup(self.work.cleanup)
        self.bin_dir = Path(self.work.name) / "bin"
        self.bin_dir.mkdir()
        for name in ("npm", "codex", "opencode", "opencode2", "nanocoder", "claude"):
            stub = self.bin_dir / name
            stub.write_text(NPM_STUB if name == "npm" else CLI_STUB)
            stub.chmod(0o755)

    @contextlib.contextmanager
    def refresh(self, env: dict[str, str], packages: tuple[str, ...] = FOCUSED_PACKAGE,
                commands: tuple[str, ...] = FOCUSED_COMMAND) -> Iterator[tuple[int, str, str, mock.Mock]]:
        stdout, stderr = io.StringIO(), io.StringIO()
        # /usr/bin:/bin keeps the stub shebangs working; neither directory
        # ships the real CLIs, so a deleted stub stays deleted.
        overrides = {
            "HOME": self.work.name,
            "TMPDIR": self.work.name,
            "DXT_FAKE_BIN_DIR": str(self.bin_dir),
            "NPM_CONFIG_PREFIX": "",
            "PATH": f"{self.bin_dir}:/usr/bin:/bin",
            **env,
        }
        with mock.patch.dict(os.environ, overrides), \
                mock.patch.object(installer, "PACKAGES", packages), \
                mock.patch.object(installer, "COMMANDS", commands), \
                mock.patch.object(installer, "time") as clock, \
                redirect_stdout(stdout), redirect_stderr(stderr):
            status = installer.main()
        yield status, stdout.getvalue(), stderr.getvalue(), clock.sleep

    def calls(self) -> list[str]:
        log = self.bin_dir / "npm-calls.txt"
        return log.read_text().splitlines() if log.exists() else []

    def test_missing_npm_keeps_the_image_clis(self) -> None:
        # A nonexistent entry cannot see a host npm, whatever the runner ships.
        with self.refresh({"PATH": str(Path(self.work.name) / "no-bin")}) as (status, out, err, _):
            self.assertEqual(status, 0)
            self.assertIn("npm is unavailable; keeping the image-provided agent CLIs.", err)

    def test_registry_unavailable_keeps_an_installed_cli(self) -> None:
        with self.refresh({}) as (status, out, err, _):
            self.assertEqual(status, 0)
            self.assertIn("registry unavailable; keeping @openai/codex@2.1.0.", out)
            self.assertEqual(err, "")

    def test_registry_unavailable_without_an_install_fails(self) -> None:
        (self.bin_dir / "codex").unlink()
        with self.refresh({}) as (status, out, err, _):
            self.assertEqual(status, 1)
            self.assertIn("registry unavailable and @openai/codex is not installed.", err)

    def test_current_version_skips_the_install(self) -> None:
        with self.refresh({"DXT_FAKE_NPM_VIEW": '"2.1.0"'}) as (status, out, err, _):
            self.assertEqual(status, 0)
            self.assertIn("@openai/codex@2.1.0 is current.", out)
            self.assertEqual(self.calls(), [])

    def test_newer_registry_version_installs_and_verifies(self) -> None:
        env = {"DXT_FAKE_NPM_VIEW": '"2.2.0"', "DXT_FAKE_NPM_INSTALL_CMD": "codex",
               "DXT_FAKE_NPM_INSTALL_VERSION": "2.2.0"}
        with self.refresh(env) as (status, out, err, _):
            self.assertEqual(status, 0)
            self.assertIn("installing @openai/codex@latest (current: 2.1.0)...", out)
            self.assertIn("@openai/codex@2.2.0 is ready.", out)
            self.assertEqual(self.calls(), ["install"])

    def test_failed_install_retries_three_times(self) -> None:
        with self.refresh({"DXT_FAKE_NPM_VIEW": '"2.2.0"', "DXT_FAKE_NPM_INSTALL_STATUS": "1"}) as (
                status, out, err, sleep):
            self.assertEqual(status, 1)
            for attempt in (1, 2, 3):
                self.assertIn(f"install attempt {attempt}/3 failed.", err)
            self.assertEqual([call.args[0] for call in sleep.call_args_list], [2, 4, 6])
            self.assertEqual(len(self.calls()), 3)

    def test_install_that_reports_the_wrong_version_fails(self) -> None:
        env = {"DXT_FAKE_NPM_VIEW": '"2.2.0"', "DXT_FAKE_NPM_INSTALL_CMD": "codex",
               "DXT_FAKE_NPM_INSTALL_VERSION": "2.0.9"}
        with self.refresh(env) as (status, out, err, _):
            self.assertEqual(status, 1)
            self.assertNotIn("is ready.", out)
            self.assertEqual(len(self.calls()), 3)

    def test_invalid_registry_version_fails_without_installing(self) -> None:
        with self.refresh({"DXT_FAKE_NPM_VIEW": '"two.point.zero"'}) as (status, out, err, _):
            self.assertEqual(status, 1)
            self.assertIn("Registry returned an invalid version for @openai/codex.", err)
            self.assertEqual(self.calls(), [])

    def test_legacy_package_removal_tracks_the_listing_answer(self) -> None:
        with self.refresh({"DXT_FAKE_NPM_LIST_STATUS": "0"}) as (status, out, err, _):
            self.assertEqual(status, 0)
            self.assertIn("removing legacy opencode-ai package.", out)
            self.assertEqual(self.calls(), ["uninstall"])
        (self.bin_dir / "npm-calls.txt").unlink()
        with self.refresh({}) as (status, out, err, _):
            self.assertEqual(status, 0)
            self.assertNotIn("removing legacy opencode-ai package.", out)
            self.assertEqual(self.calls(), [])

    def test_opencode_v2_is_required(self) -> None:
        (self.bin_dir / "version-opencode.txt").write_text("opencode v1.18.32\n")
        with self.refresh({"DXT_FAKE_NPM_VIEW": '"2.1.0"'}) as (status, out, err, _):
            self.assertEqual(status, 1)
            self.assertIn("OpenCode v2 is required; found 1.18.32.", err)

    def test_opencode_alias_must_match(self) -> None:
        (self.bin_dir / "version-opencode2.txt").write_text("opencode v2.0.15\n")
        with self.refresh({"DXT_FAKE_NPM_VIEW": '"2.1.0"'}) as (status, out, err, _):
            self.assertEqual(status, 1)
            self.assertIn("opencode2 does not match opencode 2.1.0.", err)

    def test_alias_absent_and_agreeing_both_pass(self) -> None:
        (self.bin_dir / "opencode2").unlink()
        with self.refresh({"DXT_FAKE_NPM_VIEW": '"2.1.0"'}) as (status, _, _, _):
            self.assertEqual(status, 0)
        (self.bin_dir / "opencode2").write_text(CLI_STUB)
        (self.bin_dir / "opencode2").chmod(0o755)
        (self.bin_dir / "version-opencode2.txt").write_text("opencode v2.1.0\n")
        with self.refresh({"DXT_FAKE_NPM_VIEW": '"2.1.0"'}) as (status, _, _, _):
            self.assertEqual(status, 0)

    def test_lock_contention_exits_without_touching_the_registry(self) -> None:
        lock_path = Path(self.work.name) / installer.LOCK_NAME
        with lock_path.open("ab") as held:
            fcntl.flock(held.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            with self.refresh({}) as (status, out, err, _):
                self.assertEqual(status, 0)
                self.assertIn("another agent CLI refresh is already running.", out)
            fcntl.flock(held.fileno(), fcntl.LOCK_UN)

    def test_full_refresh_reports_every_current_cli(self) -> None:
        with self.refresh({"DXT_FAKE_NPM_VIEW": '"2.1.0"'}, packages=installer.PACKAGES,
                          commands=installer.COMMANDS) as (status, out, err, _):
            self.assertEqual(status, 0)
            for package in ("@openai/codex", "@opencode/cli", "@nanocollective/nanocoder",
                            "@anthropic-ai/claude-code"):
                self.assertIn(f"{package}@2.1.0 is current.", out)
            self.assertEqual(err, "")


if __name__ == "__main__":
    unittest.main()
