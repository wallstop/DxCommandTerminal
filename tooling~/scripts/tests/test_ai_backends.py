"""The Z.AI / OpenRouter launcher regression suite (issue #220).

Ports `test-ai-backends.sh` to typed stdlib Python: credential resolution
(environment, aliases, `.env.local` parsed as data), the four provider
launchers' exported environment and codex profiles, the BASH_ENV scrub
guarantees, launcher installation, and the `.env.local` autoload wiring.
Every launch drives the real `.devcontainer/ai-backends.sh` through a
subprocess against fake codex/claude CLIs that record their arguments and
environment, so nothing is ever billed.

Unlike the bash suite, which discarded launcher exit codes and asserted only
on the recording, every launch expected to succeed must also exit zero - the
launcher's contract is all-or-nothing, so a nonzero exit with a plausible
recording is still a regression.

An ambient loader would inject credentials into every case's environment
and mask the file-sourced values these assertions check, so every
subprocess starts from a scrubbed environment: all eleven managed
credential names (the canonical names and every alias `env` can export),
plus the launcher's own control variables.
"""
from __future__ import annotations

import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import tomllib
import unittest
from pathlib import Path
from typing import Dict, Iterable, List, Optional, Tuple

REPO_ROOT = Path(__file__).parents[3]
DEVCONTAINER = REPO_ROOT / ".devcontainer"
BACKENDS = DEVCONTAINER / "ai-backends.sh"
ENV_AUTOLOAD = DEVCONTAINER / "env-autoload.sh"
POST_CREATE = DEVCONTAINER / "post-create.sh"

# Every managed credential name `ai-backends.sh env` can export. An
# interactive devcontainer shell carries the whole set (the rc autoload), so
# a case that means "this credential is absent" must clear every name, not
# just the canonical one: find_credential resolves aliases (Z_AI_API_KEY,
# GITHUB_MCP_PAT, ...), and an ambient alias silently wins over the
# .env.local value under test - the suite then fails in exactly the
# container it ships in while passing on a bare CI runner.
MANAGED_CREDENTIALS = (
    "ZAI_API_KEY",
    "Z_AI_API_KEY",
    "ZHIPU_API_KEY",
    "OPENROUTER_API_KEY",
    "GITHUB_TOKEN",
    "GH_TOKEN",
    "GITHUB_PERSONAL_ACCESS_TOKEN",
    "GITHUB_PAT",
    "GITHUB_MCP_PAT",
    "UNITY_MCP_BEARER_TOKEN",
    "UNITY_PROJECT_PATH",
)

# The launcher's own plumbing: an ambient value would redirect the profile
# writes (CODEX_HOME), mask the file-sourced values under test, or override
# the launcher defaults the assertions pin (model maps, timeouts, container
# mode, scrub modes).
SANDBOX_UNSET = (
    *MANAGED_CREDENTIALS,
    "AI_BACKENDS_REPO_ROOT",
    "DXT_WORKSPACE_ROOT",
    "DXT_ENV_AUTOLOAD_DISABLED",
    "BASH_ENV",
    "CODEX_HOME",
    "CODEX_ZAI_MODEL",
    "CODEX_ZAI_REASONING_EFFORT",
    "ZAI_API_TIMEOUT_MS",
    "CLAUDE_ZAI_CONFIG_DIR",
    "CLAUDE_ZAI_HAIKU_MODEL",
    "CLAUDE_ZAI_SONNET_MODEL",
    "CLAUDE_ZAI_OPUS_MODEL",
    "AI_BACKENDS_CONTAINER_MODE",
    "CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB",
    "CODEX_OPENROUTER_MODEL",
    "OPENROUTER_API_TIMEOUT_MS",
    "CLAUDE_OPENROUTER_CONFIG_DIR",
    "CLAUDE_OPENROUTER_FABLE_MODEL",
    "CLAUDE_OPENROUTER_OPUS_MODEL",
    "CLAUDE_OPENROUTER_SONNET_MODEL",
    "CLAUDE_OPENROUTER_HAIKU_MODEL",
    "CLAUDE_OPENROUTER_SUBAGENT_MODEL",
    "CLAUDE_OPENROUTER_GATEWAY_DISCOVERY",
    "CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB",
)

# The fake CLI dir must lead PATH, but system paths must follow so the
# ai-backends shebang (#!/usr/bin/env bash) still resolves.
SYSTEM_PATH = "/usr/bin:/bin:/usr/sbin:/sbin"

SKIP_ON_WINDOWS = unittest.skipIf(sys.platform == "win32", "the launchers are bash scripts")

# Verbatim from the retired bash suite: the fake CLIs record their arguments
# and environment to $FAKE_CLI_OUT, and optionally re-record the environment
# a spawned child would see (with or without the Z.AI scrub applied).
FAKE_CLI = """#!/usr/bin/env bash
{
    echo "--- args ---"
    printf '%s\\n' "$@"
    echo "--- env ---"
    env | LC_ALL=C sort
    if [ "${FAKE_CLI_RUN_CHILD:-0}" = "1" ]; then
        echo "--- child env ---"
        if [ "${FAKE_CLI_SCRUB_ZAI:-0}" = "1" ]; then
            env -u ZAI_API_KEY -u Z_AI_API_KEY bash -c 'env | LC_ALL=C sort'
        else
            bash -c 'env | LC_ALL=C sort'
        fi
    fi
} >> "${FAKE_CLI_OUT:-/dev/stdout}"
exit 0
"""


def sanitize_env(unset: Iterable[str] = SANDBOX_UNSET) -> Dict[str, str]:
    return {key: value for key, value in os.environ.items() if key not in unset}


def section_after(text: str, marker: str) -> str:
    return text.split(marker, 1)[1] if marker in text else ""


class BackendsTestCase(unittest.TestCase):
    """Shared sandbox: fake CLIs, a private codex home, and a scrubbed env."""

    def setUp(self) -> None:
        self.work = Path(tempfile.mkdtemp(prefix="dxt-ai-backends-"))
        self.addCleanup(shutil.rmtree, self.work, True)
        self.fake_bin = self.work / "bin"
        self.fake_bin.mkdir()
        for name in ("codex", "claude"):
            cli = self.fake_bin / name
            cli.write_text(FAKE_CLI, encoding="utf-8")
            cli.chmod(0o755)
        self.sandbox_path = f"{self.fake_bin}:{SYSTEM_PATH}:{os.environ.get('PATH', '')}"
        self.codex_home = self.work / "codex-home"
        self.scratch_count = 0

    def env(self, **overrides: str) -> Dict[str, str]:
        result = sanitize_env()
        result["PATH"] = self.sandbox_path
        result["CODEX_HOME"] = str(self.codex_home)
        result.update(overrides)
        return result

    def scratch(self, name: str) -> Path:
        self.scratch_count += 1
        return self.work / f"{self.scratch_count:03d}-{name}"

    def env_root(self, *lines: str, name: str = "envroot") -> Path:
        """A checkout stand-in whose .env.local holds exactly `lines`."""
        root = self.scratch(name)
        root.mkdir()
        (root / ".env.local").write_text("".join(f"{line}\n" for line in lines), encoding="utf-8")
        return root

    def run_launcher(self, action: str, *args: str, **env_overrides: str) -> Tuple[subprocess.CompletedProcess[str], str]:
        """Runs one launcher action; returns (result, what the fake CLI saw)."""
        out = self.scratch(f"recorded-{action.replace('/', '-')}.txt")
        env = self.env(FAKE_CLI_OUT=str(out), **env_overrides)
        result = subprocess.run(
            ["/bin/bash", str(BACKENDS), action, *args],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )
        recorded = out.read_text(encoding="utf-8") if out.exists() else ""
        return result, recorded

    def record(self, action: str, *args: str, codex_home: Optional[Path] = None, **env_overrides: str) -> str:
        """Runs one launcher action expected to succeed, returning the recording."""
        if codex_home is not None:
            env_overrides["CODEX_HOME"] = str(codex_home)
        result, recorded = self.run_launcher(action, *args, **env_overrides)
        self.assertEqual(result.returncode, 0, f"{action} failed: {result.stderr}")
        return recorded

    def assert_contains(self, text: str, pattern: str, label: str) -> None:
        self.assertIsNotNone(re.search(pattern, text, re.MULTILINE), f"{label}: {pattern!r} not found")

    def assert_not_contains(self, text: str, pattern: str, label: str) -> None:
        self.assertIsNone(re.search(pattern, text, re.MULTILINE), f"{label}: {pattern!r} found")


@SKIP_ON_WINDOWS
class ScrubListParity(BackendsTestCase):
    """Every emit_export name is scrubbed, and only those."""

    def emit_export_names(self) -> List[str]:
        names: List[str] = []
        for line in BACKENDS.read_text(encoding="utf-8").splitlines():
            match = re.match(r"^    emit_export (.+)$", line)
            if match:
                names.extend(match.group(1).split())
        return sorted(set(names))

    def test_scrub_list_matches_the_emit_export_set(self) -> None:
        # A credential or alias added to print_env_exports without landing in
        # MANAGED_CREDENTIALS (or the reverse) would re-open the ambient-leak
        # hole this suite's sanitized environment guards against.
        names = self.emit_export_names()
        self.assertGreater(len(names), 0, "no emit_export call sites found in the launcher")
        for name in names:
            with self.subTest(name=name):
                self.assertIn(name, MANAGED_CREDENTIALS, "emit_export name missing from MANAGED_CREDENTIALS")
        for name in MANAGED_CREDENTIALS:
            with self.subTest(name=name):
                self.assertIn(name, names, "scrub-list name is not an emit_export name")


@SKIP_ON_WINDOWS
class CodexZaiContract(BackendsTestCase):
    """codex-zai: profile, model, key forwarding, and .env.local resolution."""

    def test_profile_model_and_key_forwarding(self) -> None:
        recorded = self.record("codex-zai", "--model", "glm-5.3", ZAI_API_KEY="test-key-codex")
        self.assert_contains(recorded, r"^--profile$", "codex-zai passes --profile")
        self.assert_contains(recorded, r"^devcontainer-zai$", "codex-zai uses the zai profile")
        self.assert_contains(recorded, 'model_provider="ZAI"', "codex-zai pins the ZAI provider")
        self.assert_contains(recorded, 'model_reasoning_effort="max"', "codex-zai defaults reasoning to max")
        self.assert_contains(recorded, r"^ZAI_API_KEY=test-key-codex$", "codex-zai exports ZAI_API_KEY")
        self.assert_not_contains(recorded, r"^Z_AI_API_KEY=", "codex-zai removes the Z.AI alias")
        self.assert_not_contains(recorded, r"^ZHIPU_API_KEY=", "codex-zai removes the native OpenCode mirror")
        # The hostile environment must not reach the generated profile files.
        profile = (self.codex_home / "devcontainer-zai.config.toml").read_text(encoding="utf-8")
        self.assertNotIn("test-key-codex", profile, "profile contains the raw key")

    def test_z_ai_alias_and_reasoning_override(self) -> None:
        recorded = self.record("codex-zai", Z_AI_API_KEY="alias-key", CODEX_ZAI_REASONING_EFFORT="low")
        self.assert_contains(recorded, r"^ZAI_API_KEY=alias-key$", "Z_AI_API_KEY alias is honored")
        self.assert_contains(recorded, 'model_reasoning_effort="low"', "reasoning effort override applied")

    def test_env_local_fallback(self) -> None:
        # Data-driven: quoting, whitespace, plain, and BOM-prefixed files all
        # resolve to the same exported key (the BOM is skipped so a
        # Windows-authored .env.local keeps working).
        cases: List[Tuple[str, Tuple[str, ...], str]] = [
            ("quoted alias strips quotes", ("# comment", 'Z_AI_API_KEY="zai-key-from-envfile"'), "zai-key-from-envfile"),
            ("whitespace around the assignment", ("  Z_AI_API_KEY = \"zai-spaced-key\"  ",), "zai-spaced-key"),
            ("plain key", ("ZAI_API_KEY=zai-file-key",), "zai-file-key"),
            # The BOM literal below is U+FEFF, spelled out so it cannot be
            # silently stripped by an editor.
            ("UTF-8 BOM is ignored", ("\ufeffZAI_API_KEY=zai-bom-key",), "zai-bom-key"),
        ]
        for label, lines, expected in cases:
            with self.subTest(case=label):
                root = self.env_root(*lines)
                recorded = self.record("codex-zai", AI_BACKENDS_REPO_ROOT=str(root))
                self.assert_contains(recorded, rf"^ZAI_API_KEY={re.escape(expected)}$", label)

    def test_environment_beats_env_local(self) -> None:
        root = self.env_root("ZAI_API_KEY=zai-file-key")
        recorded = self.record("codex-zai", ZAI_API_KEY="env-wins", AI_BACKENDS_REPO_ROOT=str(root))
        self.assert_contains(recorded, r"^ZAI_API_KEY=env-wins$", "environment beats .env.local")

    def test_empty_environment_variable_is_ignored(self) -> None:
        root = self.env_root("ZAI_API_KEY=zai-file-key")
        recorded = self.record("codex-zai", ZAI_API_KEY="", AI_BACKENDS_REPO_ROOT=str(root))
        self.assert_contains(recorded, r"^ZAI_API_KEY=zai-file-key$", "empty environment variable is ignored")

    def test_default_env_local_location_is_derived_from_the_script_path(self) -> None:
        # A copy of the launcher under another checkout must read that
        # checkout's .env.local, not the real one's - AI_BACKENDS_REPO_ROOT
        # stays unset for exactly this, so the ambient checkout (which may
        # carry its own .env.local) must not satisfy the assertion.
        defroot = self.scratch("defroot")
        (defroot / ".devcontainer").mkdir(parents=True)
        shutil.copy2(BACKENDS, defroot / ".devcontainer" / "ai-backends.sh")
        (defroot / ".env.local").write_text("ZAI_API_KEY=default-root-key\n", encoding="utf-8")
        out = self.scratch("recorded-defroot.txt")
        env = self.env(FAKE_CLI_OUT=str(out))
        result = subprocess.run(
            ["/bin/bash", str(defroot / ".devcontainer" / "ai-backends.sh"), "codex-zai"],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assert_contains(
            out.read_text(encoding="utf-8"),
            r"^ZAI_API_KEY=default-root-key$",
            "default .env.local resolves next to the checkout root",
        )

    def test_missing_key_fails_loudly(self) -> None:
        empty_root = self.scratch("empty-root")
        empty_root.mkdir()
        result, _ = self.run_launcher("codex-zai", AI_BACKENDS_REPO_ROOT=str(empty_root))
        self.assertNotEqual(result.returncode, 0, "missing key should fail")
        self.assert_contains(result.stderr, "ZAI_API_KEY", "missing key error mentions ZAI_API_KEY")

    def test_invalid_reasoning_effort_fails(self) -> None:
        result, _ = self.run_launcher("codex-zai", ZAI_API_KEY="test-key", CODEX_ZAI_REASONING_EFFORT="extreme")
        self.assertNotEqual(result.returncode, 0, "invalid reasoning effort should fail")
        self.assert_contains(result.stderr, "low, high, or max", "invalid effort error is precise")


@SKIP_ON_WINDOWS
class ClaudeZaiContract(BackendsTestCase):
    """claude-zai: endpoint, token mapping, and the scrubbed environment."""

    def launch_claude_zai(self, *args: str, **env_overrides: str) -> str:
        env_overrides.setdefault("CLAUDE_ZAI_CONFIG_DIR", str(self.work / "claude-zai-home"))
        env_overrides.setdefault("AI_BACKENDS_CONTAINER_MODE", "no")
        env_overrides.setdefault("CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB", "0")
        result, recorded = self.run_launcher("claude-zai", *args, **env_overrides)
        self.assertEqual(result.returncode, 0, f"claude-zai failed: {result.stderr}")
        return recorded

    def test_endpoint_token_and_scrubbed_environment(self) -> None:
        recorded = self.launch_claude_zai(
            "--dangerously-skip-permissions",
            ZAI_API_KEY="test-key-claude",
            ANTHROPIC_API_KEY="shadowed",
            ANTHROPIC_BASE_URL="https://evil.example",
            CLAUDE_CODE_USE_BEDROCK="1",
            CLAUDE_CODE_USE_VERTEX="1",
            CLAUDE_CODE_USE_GATEWAY="1",
        )
        self.assert_contains(recorded, r"^ANTHROPIC_AUTH_TOKEN=test-key-claude$", "claude-zai sets ANTHROPIC_AUTH_TOKEN")
        self.assert_contains(recorded, r"^ANTHROPIC_BASE_URL=https://api\.z\.ai/api/anthropic$", "claude-zai points at the Z.AI anthropic endpoint")
        self.assert_contains(recorded, r"^API_TIMEOUT_MS=3000000$", "claude-zai defaults API_TIMEOUT_MS to the Z.AI-recommended 3000000")
        self.assert_contains(recorded, r"^ANTHROPIC_DEFAULT_SONNET_MODEL=glm-5\.3\[1m\]$", "claude-zai maps sonnet to glm-5.3[1m]")
        self.assert_contains(recorded, r"^ANTHROPIC_DEFAULT_HAIKU_MODEL=glm-5\.3-flash\[1m\]$", "claude-zai maps haiku to glm-5.3-flash[1m]")
        self.assert_contains(recorded, r"^CLAUDE_CONFIG_DIR=", "claude-zai isolates CLAUDE_CONFIG_DIR")
        self.assert_not_contains(recorded, r"^ANTHROPIC_API_KEY=", "competing ANTHROPIC_API_KEY is removed")
        self.assert_not_contains(recorded, r"^CLAUDE_CODE_USE_BEDROCK=", "Bedrock selector is removed")
        self.assert_not_contains(recorded, r"^CLAUDE_CODE_USE_VERTEX=", "Vertex selector is removed")
        self.assert_not_contains(recorded, r"^CLAUDE_CODE_USE_GATEWAY=", "Gateway selector is removed")
        self.assert_not_contains(recorded, r"^ZAI_API_KEY=", "ZAI key is scrubbed from the claude process")
        self.assert_not_contains(recorded, r"^Z_AI_API_KEY=", "ZAI alias is scrubbed from the claude process")
        self.assert_not_contains(recorded, r"^ZHIPU_API_KEY=", "Z.AI mirror is scrubbed from the claude process")
        self.assert_contains(recorded, r"^--dangerously-skip-permissions$", "extra arguments are forwarded")

    def test_env_local_fallback(self) -> None:
        root = self.env_root("ZAI_API_KEY=zai-file-key")
        recorded = self.launch_claude_zai(AI_BACKENDS_REPO_ROOT=str(root))
        self.assert_contains(recorded, r"^ANTHROPIC_AUTH_TOKEN=zai-file-key$", "claude-zai resolves the Z.AI key from .env.local")

    def test_container_mode_disables_the_inner_sandbox(self) -> None:
        recorded = self.launch_claude_zai(AI_BACKENDS_CONTAINER_MODE="yes", ZAI_API_KEY="k2")
        self.assert_contains(
            recorded,
            re.escape('"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}'),
            "container mode passes the weaker-sandbox settings",
        )


@SKIP_ON_WINDOWS
class ScrubbedAutoloadContract(BackendsTestCase):
    """BASH_ENV cannot restore provider keys behind the scrub policy."""

    def test_codex_child_cannot_restore_the_file_key(self) -> None:
        scrub_root = self.env_root("ZAI_API_KEY=zai-scrub-key")
        out = self.scratch("recorded-scrub.txt")
        env = self.env(
            FAKE_CLI_OUT=str(out),
            AI_BACKENDS_REPO_ROOT=str(scrub_root),
            DXT_WORKSPACE_ROOT=str(scrub_root),
            BASH_ENV=str(ENV_AUTOLOAD),
            FAKE_CLI_RUN_CHILD="1",
            FAKE_CLI_SCRUB_ZAI="1",
        )
        result = subprocess.run(
            ["/bin/bash", str(BACKENDS), "codex-zai"],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        recorded = out.read_text(encoding="utf-8")
        self.assert_contains(recorded, r"^ZAI_API_KEY=zai-scrub-key$", "codex parent receives the file key")
        self.assert_contains(recorded, r"^DXT_ENV_AUTOLOAD_DISABLED=1$", "codex disables shell autoload")
        child = section_after(recorded, "--- child env ---\n")
        self.assert_not_contains(child, r"^ZAI_API_KEY=", "codex child did not restore the Z.AI key")
        self.assert_contains(child, r"^DXT_ENV_AUTOLOAD_DISABLED=1$", "codex child keeps autoload disabled")
        self.assert_contains(child, r"^BASH_ENV=/dev/null$", "codex child uses a no-op BASH_ENV")


@SKIP_ON_WINDOWS
class ScrubCallSiteContract(BackendsTestCase):
    """Anchored call sites only: a comment or the definition must not satisfy this."""

    def test_every_provider_launcher_calls_the_scrub_helper(self) -> None:
        call_sites = sum(
            1
            for line in BACKENDS.read_text(encoding="utf-8").splitlines()
            if line == "    prepare_scrubbed_agent_environment"
        )
        self.assertEqual(
            call_sites, 4,
            "found the wrong number of scrub call sites; expected one per provider launcher",
        )


@SKIP_ON_WINDOWS
class InstallContract(BackendsTestCase):
    """install: launchers, profile files, permissions, and refusals."""

    def test_install_writes_all_four_launchers_and_the_profile_files(self) -> None:
        bin_dir = self.scratch("launchers")
        codex_home = self.scratch("codex-home2")
        for _ in (0, 1):
            # The second install runs over the first one's symlinks and
            # profile files: reinstalling an existing installation is the
            # normal container-rebuild path and must stay idempotent.
            result, _ = self.run_launcher("install", AI_BACKENDS_BIN_DIR=str(bin_dir), CODEX_HOME=str(codex_home))
            self.assertEqual(result.returncode, 0, result.stderr)
        for launcher in ("codex-zai", "claude-zai", "codex-openrouter", "claude-openrouter"):
            with self.subTest(launcher=launcher):
                self.assertTrue((bin_dir / launcher).is_symlink(), f"{launcher} launcher is not a symlink")
        profile = codex_home / "devcontainer-zai.config.toml"
        catalog = codex_home / "devcontainer-zai-models.json"
        self.assertTrue(profile.is_file(), "profile toml exists")
        self.assertTrue(catalog.is_file(), "model catalog exists")
        self.assertIn("glm-5.3", catalog.read_text(encoding="utf-8"), "catalog lists glm-5.3")
        profile_text = profile.read_text(encoding="utf-8")
        self.assertIn("https://api.z.ai/api/v1", profile_text, "profile uses responses endpoint")
        self.assertNotIn("API_KEY=", profile_text, "profile keeps the key out of the toml")
        self.assertEqual(codex_home.stat().st_mode & 0o777, 0o700, "codex home is 0700")
        self.assertEqual(profile.stat().st_mode & 0o777, 0o600, "profile toml is 0600")

    def test_install_refuses_to_replace_non_symlink_launchers(self) -> None:
        bin_dir = self.scratch("launchers2")
        bin_dir.mkdir()
        (bin_dir / "codex-zai").write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
        result, _ = self.run_launcher("install", AI_BACKENDS_BIN_DIR=str(bin_dir))
        self.assertNotEqual(result.returncode, 0, "install should refuse to replace a non-symlink launcher")
        self.assert_contains(result.stderr, "non-symlink", "refusal message explains the problem")


@SKIP_ON_WINDOWS
class CodexOpenRouterContract(BackendsTestCase):
    """codex-openrouter: profile, auth command, and .env.local resolution."""

    def openrouter_home(self) -> Path:
        return self.scratch("codex-home-or")

    def test_profile_model_and_key_forwarding(self) -> None:
        recorded = self.record("codex-openrouter", OPENROUTER_API_KEY="or-key-codex")
        self.assert_contains(recorded, r"^--profile$", "codex-openrouter passes --profile")
        self.assert_contains(recorded, r"^devcontainer-openrouter$", "codex-openrouter uses the openrouter profile")
        self.assert_contains(recorded, 'model_provider="OPENROUTER"', "codex-openrouter pins the OPENROUTER provider")
        self.assert_contains(recorded, r"^--model$", "codex-openrouter passes --model")
        self.assert_contains(recorded, r"^openai/gpt-5\.6-sol$", "codex-openrouter defaults to a current flagship model")
        self.assert_contains(recorded, r"^OPENROUTER_API_KEY=or-key-codex$", "codex-openrouter exports OPENROUTER_API_KEY")

    def test_model_override_and_profile_file_contents(self) -> None:
        home = self.openrouter_home()
        recorded = self.record(
            "codex-openrouter",
            codex_home=home,
            OPENROUTER_API_KEY="or-key",
            CODEX_OPENROUTER_MODEL="anthropic/claude-sonnet-5",
        )
        self.assert_contains(recorded, r"^anthropic/claude-sonnet-5$", "CODEX_OPENROUTER_MODEL override applied")
        profile_text = (home / "devcontainer-openrouter.config.toml").read_text(encoding="utf-8")
        self.assertIn('base_url = "https://openrouter.ai/api/v1"', profile_text, "profile uses chat completions endpoint")
        self.assertIn('wire_api = "responses"', profile_text, "profile uses the responses wire api")
        self.assertIn("[model_providers.OPENROUTER.auth]", profile_text, "profile uses command-backed auth")
        self.assertNotIn("env_key", profile_text, "env_key mode (skips the live model catalog) is not used")
        self.assertNotIn("or-key", profile_text, "openrouter profile keeps the raw key out of the toml")
        self.assertIn('OPENROUTER_API_KEY = "exclude"', profile_text, "profile filters the key from subprocesses")

    def test_auth_command_prints_the_key_from_the_environment(self) -> None:
        # Extract the auth command exactly as Codex would parse it from the
        # TOML basic string, then run it and compare stdout with the exported
        # key; without the key it must print nothing.
        home = self.openrouter_home()
        self.record("codex-openrouter", codex_home=home, OPENROUTER_API_KEY="or-key")
        profile_path = home / "devcontainer-openrouter.config.toml"
        self.assertTrue(profile_path.is_file(), "openrouter profile toml exists")
        profile = profile_path.read_text(encoding="utf-8")
        match = re.search(r'^args = \["-c", "(.*)"\]$', profile, re.MULTILINE)
        assert match is not None, "auth args line exists"
        auth_cmd = match.group(1).replace('\\"', '"')
        probe = subprocess.run(
            ["sh", "-c", auth_cmd],
            capture_output=True,
            text=True,
            env=self.env(OPENROUTER_API_KEY="probe-token"),
            timeout=120,
        )
        # Command substitution strips trailing newlines; compare the same way.
        self.assertEqual(probe.stdout.strip(), "probe-token", "auth command prints the exported key")
        anonymous = subprocess.run(
            ["sh", "-c", auth_cmd],
            capture_output=True,
            text=True,
            env=self.env(),
            timeout=120,
        )
        self.assertEqual(anonymous.stdout.strip(), "", "auth command prints nothing without the key")

    def test_generated_profiles_parse_strictly(self) -> None:
        # Both profiles must parse as strict TOML/JSON exactly as Codex reads
        # them; each sandbox starts fresh, so install both first.
        self.record("codex-zai", ZAI_API_KEY="probe-zai")
        with open(self.codex_home / "devcontainer-zai.config.toml", "rb") as handle:
            zai = tomllib.load(handle)
        zai_provider = zai["model_providers"]["ZAI"]
        self.assertEqual(zai_provider["base_url"], "https://api.z.ai/api/v1")
        self.assertEqual(zai_provider["wire_api"], "responses")
        self.assertEqual(zai_provider["env_key"], "ZAI_API_KEY", "key resolution stays environment-based")
        self.assertNotIn("auth", zai_provider)
        self.assertNotIn("experimental_bearer_token", zai_provider)
        self.assertEqual(
            zai["shell_environment_policy"]["filters"],
            {"ZAI_API_KEY": "exclude", "Z_AI_API_KEY": "exclude"},
        )
        with open(self.codex_home / "devcontainer-zai-models.json", "rb") as handle:
            catalog = json.load(handle)
        self.assertEqual(catalog["models"][0]["slug"], "glm-5.3", "zai model catalog parses as JSON")
        home = self.scratch("codex-home-strict")
        self.record("codex-openrouter", codex_home=home, OPENROUTER_API_KEY="probe-or")
        with open(home / "devcontainer-openrouter.config.toml", "rb") as handle:
            openrouter = tomllib.load(handle)
        provider = openrouter["model_providers"]["OPENROUTER"]
        self.assertEqual(provider["base_url"], "https://openrouter.ai/api/v1")
        self.assertEqual(provider["wire_api"], "responses")
        self.assertEqual(provider["auth"]["command"], "sh")
        self.assertEqual(
            provider["auth"]["args"],
            ["-c", "printf '%s' \"${OPENROUTER_API_KEY}\""],
            "auth args survive TOML escaping",
        )
        self.assertNotIn("env_key", provider)
        self.assertEqual(openrouter["shell_environment_policy"]["filters"], {"OPENROUTER_API_KEY": "exclude"})

    def test_missing_key_fails_loudly(self) -> None:
        empty_root = self.scratch("empty-root")
        empty_root.mkdir()
        result, _ = self.run_launcher("codex-openrouter", AI_BACKENDS_REPO_ROOT=str(empty_root))
        self.assertNotEqual(result.returncode, 0, "missing OPENROUTER_API_KEY should fail")
        self.assert_contains(result.stderr, "OPENROUTER_API_KEY", "missing key error mentions OPENROUTER_API_KEY")

    def test_env_local_fallback(self) -> None:
        root = self.env_root("# comment", 'OPENROUTER_API_KEY="or-key-from-envfile"')
        recorded = self.record("codex-openrouter", AI_BACKENDS_REPO_ROOT=str(root))
        self.assert_contains(recorded, r"^OPENROUTER_API_KEY=or-key-from-envfile$", ".env.local key is resolved (quotes stripped)")


@SKIP_ON_WINDOWS
class ClaudeOpenRouterContract(BackendsTestCase):
    """claude-openrouter: endpoint, token, and the explicit empty API key."""

    def launch_claude_openrouter(self, *args: str, **env_overrides: str) -> str:
        env_overrides.setdefault("CLAUDE_OPENROUTER_CONFIG_DIR", str(self.work / "claude-or-home"))
        env_overrides.setdefault("AI_BACKENDS_CONTAINER_MODE", "no")
        env_overrides.setdefault("CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB", "0")
        result, recorded = self.run_launcher("claude-openrouter", *args, **env_overrides)
        self.assertEqual(result.returncode, 0, f"claude-openrouter failed: {result.stderr}")
        return recorded

    def test_endpoint_token_and_explicit_empty_anthropic_api_key(self) -> None:
        recorded = self.launch_claude_openrouter(
            "--model", "anthropic/claude-sonnet-5",
            OPENROUTER_API_KEY="or-key-claude",
            ANTHROPIC_API_KEY="shadowed",
            ANTHROPIC_BASE_URL="https://evil.example",
            CLAUDE_CODE_USE_BEDROCK="1",
            CLAUDE_CODE_USE_VERTEX="1",
        )
        self.assert_contains(recorded, r"^ANTHROPIC_AUTH_TOKEN=or-key-claude$", "claude-openrouter sets ANTHROPIC_AUTH_TOKEN")
        self.assert_contains(recorded, r"^ANTHROPIC_BASE_URL=https://openrouter\.ai/api$", "claude-openrouter points at the OpenRouter anthropic endpoint")
        self.assert_contains(recorded, r"^ANTHROPIC_API_KEY=$", "ANTHROPIC_API_KEY is explicitly empty (OpenRouter requirement)")
        self.assert_contains(recorded, r"^ANTHROPIC_DEFAULT_SONNET_MODEL=anthropic/claude-sonnet-5$", "claude-openrouter maps sonnet to claude-sonnet-5")
        self.assert_contains(recorded, r"^ANTHROPIC_DEFAULT_OPUS_MODEL=anthropic/claude-opus-5$", "claude-openrouter maps opus to claude-opus-5")
        self.assert_contains(recorded, r"^ANTHROPIC_DEFAULT_HAIKU_MODEL=anthropic/claude-haiku-4\.5$", "claude-openrouter maps haiku to claude-haiku-4.5")
        self.assert_contains(recorded, r"^ANTHROPIC_DEFAULT_FABLE_MODEL=anthropic/claude-fable-5\.1$", "claude-openrouter maps the fable class")
        self.assert_contains(recorded, r"^CLAUDE_CODE_SUBAGENT_MODEL=", "claude-openrouter sets the subagent model")
        self.assert_contains(recorded, r"^CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY=1$", "gateway model discovery is enabled by default")
        self.assert_contains(recorded, r"^CLAUDE_CONFIG_DIR=", "claude-openrouter isolates CLAUDE_CONFIG_DIR")
        self.assert_not_contains(recorded, r"^CLAUDE_CODE_USE_BEDROCK=", "Bedrock selector is removed")
        self.assert_not_contains(recorded, r"^CLAUDE_CODE_USE_VERTEX=", "Vertex selector is removed")
        self.assert_not_contains(recorded, r"^OPENROUTER_API_KEY=", "OPENROUTER key is scrubbed from the claude process")
        self.assert_contains(recorded, r"^--model$", "extra arguments are forwarded")

    def test_model_overrides_and_discovery_opt_out(self) -> None:
        recorded = self.launch_claude_openrouter(
            OPENROUTER_API_KEY="or-key",
            CLAUDE_OPENROUTER_SONNET_MODEL="openai/gpt-5.6-sol",
            CLAUDE_OPENROUTER_GATEWAY_DISCOVERY="0",
        )
        self.assert_contains(recorded, r"^ANTHROPIC_DEFAULT_SONNET_MODEL=openai/gpt-5\.6-sol$", "CLAUDE_OPENROUTER_SONNET_MODEL override applied")
        self.assert_contains(recorded, r"^CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY=0$", "gateway discovery can be disabled")

    def test_container_mode_disables_the_inner_sandbox(self) -> None:
        recorded = self.launch_claude_openrouter(AI_BACKENDS_CONTAINER_MODE="yes", OPENROUTER_API_KEY="or-key")
        self.assert_contains(
            recorded,
            re.escape('"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}'),
            "container mode passes the weaker-sandbox settings",
        )


@SKIP_ON_WINDOWS
class EnvExportsContract(BackendsTestCase):
    """env: exports managed credentials from environment and .env.local."""

    def run_env(self, root: Optional[Path] = None, **env_overrides: str) -> str:
        if root is None:
            root = self.scratch("empty-envroot")
            root.mkdir()
        result, _ = self.run_launcher("env", AI_BACKENDS_REPO_ROOT=str(root), **env_overrides)
        self.assertEqual(result.returncode, 0, result.stderr)
        return result.stdout

    def test_exports_managed_credentials(self) -> None:
        # The quoted OpenRouter value carries an embedded single quote; the
        # export must escape it so the output stays safe to eval.
        root = self.env_root(
            "ZAI_API_KEY=zai-file-key",
            'GITHUB_PERSONAL_ACCESS_TOKEN="gh-token-file"',
            "UNITY_PROJECT_PATH=/unity/path",
            "OPENROUTER_API_KEY=\"it's quoted\"",
        )
        output = self.run_env(root)
        self.assert_contains(output, r"^export ZAI_API_KEY='zai-file-key'$", "env exports the Z.AI key")
        self.assert_contains(output, r"^export ZHIPU_API_KEY='zai-file-key'$", "env mirrors the Z.AI key into ZHIPU_API_KEY for native opencode")
        self.assert_contains(output, r"^export OPENROUTER_API_KEY='it'\\''s quoted'$", "env escapes single quotes for safe eval")
        self.assert_contains(output, r"^export GITHUB_TOKEN='gh-token-file'$", "env maps GitHub aliases onto GITHUB_TOKEN")
        self.assert_contains(output, r"^export UNITY_PROJECT_PATH='/unity/path'$", "env exports UNITY_PROJECT_PATH")

    def test_unset_keys_are_omitted_and_output_evaluates_cleanly(self) -> None:
        output = self.run_env(ZAI_API_KEY="env-only-zai")
        eval_script = self.scratch("eval-env.sh")
        eval_script.write_text(output, encoding="utf-8")
        evaluated = subprocess.run(
            ["/bin/bash", "-c", f"set -e; . '{eval_script}'; test \"${{ZAI_API_KEY}}\" = 'env-only-zai'"],
            capture_output=True,
            text=True,
            timeout=120,
        )
        self.assertEqual(evaluated.returncode, 0, "env output evaluates and sets the variable")
        self.assertNotIn("OPENROUTER", output, "env omits keys absent from environment and .env.local")
        self.assertNotIn("UNITY", output, "env omits unset Unity values")

    def test_environment_precedence_over_env_local(self) -> None:
        root = self.env_root("ZAI_API_KEY=zai-file-key")
        output = self.run_env(root, ZAI_API_KEY="env-zai-wins")
        self.assert_contains(output, r"^export ZAI_API_KEY='env-zai-wins'$", "env honors environment precedence")


@SKIP_ON_WINDOWS
class EnvAutoloadContract(BackendsTestCase):
    """The rc snippet exports .env.local credentials for native agents."""

    def setUp(self) -> None:
        super().setUp()
        self.assertTrue(ENV_AUTOLOAD.is_file(), "env-autoload snippet exists")
        self.autoload_root = self.env_root("ZAI_API_KEY=zai-file-key", "OPENROUTER_API_KEY=or-file-key", name="autoload-root")
        (self.autoload_root / ".devcontainer").mkdir()
        shutil.copy2(BACKENDS, self.autoload_root / ".devcontainer" / "ai-backends.sh")

    def source_autoload(self, command: str, workspace_root: Optional[Path] = None, merge_stderr: bool = False, **env_overrides: str) -> str:
        env = self.env(
            DXT_WORKSPACE_ROOT=str(workspace_root or self.autoload_root),
            **env_overrides,
        )
        result = subprocess.run(
            ["/bin/bash", "-c", f'. "$1"; {command}', "_", str(ENV_AUTOLOAD)],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )
        return result.stdout + result.stderr if merge_stderr else result.stdout

    def test_exports_credentials_and_unsets_its_own_plumbing(self) -> None:
        output = self.source_autoload("env | LC_ALL=C sort")
        self.assert_contains(output, r"^ZAI_API_KEY=zai-file-key$", "autoload exports the Z.AI key")
        self.assert_contains(output, r"^OPENROUTER_API_KEY=or-file-key$", "autoload exports the OpenRouter key")
        self.assert_contains(output, r"^ZHIPU_API_KEY=zai-file-key$", "autoload mirrors the Z.AI key for native opencode")
        self.assert_not_contains(output, r"^DXT_WORKSPACE_ROOT=", "autoload unsets its own plumbing variable")

    def test_disabled_autoload_does_not_import_credentials(self) -> None:
        output = self.source_autoload(
            'printf "%s\\n" "${ZAI_API_KEY-UNSET}"',
            merge_stderr=True,
            DXT_ENV_AUTOLOAD_DISABLED="1",
        )
        self.assert_contains(output, r"^UNSET$", "disabled autoload does not import credentials")

    def test_keeps_environment_precedence(self) -> None:
        output = self.source_autoload('printf "%s\\n" "${ZAI_API_KEY}"', ZAI_API_KEY="env-wins")
        self.assert_contains(output, r"^env-wins$", "autoload keeps environment precedence")

    def test_is_a_silent_no_op_without_the_checkout(self) -> None:
        output = self.source_autoload(
            'printf "exit=%s zai=%s\\n" "$?" "${ZAI_API_KEY-UNSET}"',
            workspace_root=self.work / "missing-root",
            merge_stderr=True,
        )
        self.assert_contains(output, r"^exit=0 zai=UNSET$", "autoload is a silent no-op without the checkout")


@SKIP_ON_WINDOWS
class PostCreateAutoloadContract(BackendsTestCase):
    """post-create installs the autoload rc block idempotently."""

    def test_installs_the_autoload_block_once_and_migrates_the_path(self) -> None:
        home = self.scratch("pc-home")
        (home / ".config").mkdir(parents=True)
        autoload_root = self.env_root("ZAI_API_KEY=zai-file-key", "OPENROUTER_API_KEY=or-file-key", name="pc-autoload-root")
        (autoload_root / ".devcontainer").mkdir()
        shutil.copy2(BACKENDS, autoload_root / ".devcontainer" / "ai-backends.sh")
        old_root = self.work / "old-workspace"
        for rc_name in (".bashrc", ".profile"):
            (home / rc_name).write_text(
                "# >>> dxcommandterminal .env.local autoload >>>\n"
                f"DXT_WORKSPACE_ROOT='{old_root}'\n"
                f"if [ -f '{old_root}/.devcontainer/env-autoload.sh' ]; then\n"
                f"    . '{old_root}/.devcontainer/env-autoload.sh'\n"
                "fi\n"
                "# <<< dxcommandterminal .env.local autoload <<<\n",
                encoding="utf-8",
            )
        # Sourcing post-create.sh only defines functions and enables
        # -euo pipefail (main is guarded); relax the flags for the calls.
        result = subprocess.run(
            [
                "/bin/bash", "-c",
                '. "$1"; set +e +u; ensure_env_local_autoload "$2"; ensure_env_local_autoload "$2"',
                "_", str(POST_CREATE), str(autoload_root),
            ],
            capture_output=True,
            text=True,
            env=self.env(HOME=str(home)),
            timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        for rc_name in (".bashrc", ".profile"):
            rc_path = home / rc_name
            self.assertTrue(rc_path.is_file(), f"{rc_name} exists")
            rc = rc_path.read_text(encoding="utf-8")
            with self.subTest(rc=rc_name):
                self.assertEqual(
                    rc.count("dxcommandterminal .env.local autoload"), 2,
                    f"{rc_name} has exactly one autoload block",
                )
        bashrc = (home / ".bashrc").read_text(encoding="utf-8")
        self.assertIn(f"DXT_WORKSPACE_ROOT='{autoload_root}'", bashrc, "bashrc block pins the workspace root")
        self.assertIn(
            f"[ -f '{autoload_root}/.devcontainer/env-autoload.sh' ]",
            bashrc,
            "bashrc block guards the snippet path",
        )
        self.assertNotIn(str(old_root), bashrc, "bashrc drops the old workspace path")
        self.assertNotIn(str(old_root), (home / ".profile").read_text(encoding="utf-8"), "profile drops the old workspace path")


if __name__ == "__main__":
    unittest.main()
