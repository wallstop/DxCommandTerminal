"""Readiness failures must propagate through the typed OpenCode verifier."""
from __future__ import annotations

import contextlib
import importlib.util
import io
from pathlib import Path
import subprocess
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("verify_opencode", Path(__file__).parents[3] / ".devcontainer" / "verify_opencode.py")
assert SPEC is not None and SPEC.loader is not None
verify = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verify)


class OpenCodeReadinessTests(unittest.TestCase):
    def run_verifier(self, primary: str, secondary: str | None = None) -> tuple[int, str, str]:
        stdout, stderr = io.StringIO(), io.StringIO()
        with patch.object(verify.shutil, "which", side_effect=["opencode", secondary]), patch.object(
            verify, "version", side_effect=[primary, secondary]
        ), contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
            result = verify.main()
        return result, stdout.getvalue(), stderr.getvalue()

    def test_v2_accepts_bare_or_named_output_and_optional_alias(self) -> None:
        for primary in ("2.0.16", "v2.0.16", "opencode v2.0.16", "opencode\t2.0.16"):
            for secondary in (None, primary):
                with self.subTest(primary=primary, secondary=secondary):
                    result, stdout, stderr = self.run_verifier(primary, secondary)
                    self.assertEqual(result, 0)
                    self.assertEqual(stderr, "")
                    self.assertIn(f"OpenCode {primary}", stdout)
                    self.assertEqual("opencode and opencode2 agree" in stdout, secondary is not None)

    def test_invalid_version_and_mismatched_alias_fail(self) -> None:
        for primary, secondary in (("opencode v1.0.0", None), ("", None), ("opencodev2.0.0", None),
                                   ("12.0.0", None), ("opencode v2.0.16", "opencode v2.0.15")):
            with self.subTest(primary=primary, secondary=secondary):
                result, stdout, stderr = self.run_verifier(primary, secondary)
                self.assertEqual(result, 1)
                self.assertEqual(stdout, "")
                self.assertIn("[verify-opencode] ERROR:", stderr)

    def test_missing_cli_and_failed_commands_fail(self) -> None:
        stderr = io.StringIO()
        with patch.object(verify.shutil, "which", return_value=None), contextlib.redirect_stderr(stderr):
            self.assertEqual(verify.main(), 1)
        self.assertIn("opencode is not installed", stderr.getvalue())
        for command in ("opencode", "opencode2"):
            for failure in (OSError("cannot launch"), subprocess.CalledProcessError(23, command)):
                with self.subTest(command=command, failure=failure):
                    with patch.object(verify.subprocess, "run", side_effect=failure):
                        with self.assertRaisesRegex(ValueError, f"{command} --version failed"):
                            verify.version(command)

    def test_command_substitution_preserves_other_whitespace(self) -> None:
        with patch.object(verify.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, b" v2.0.16 \r\n\n")) as run:
            self.assertEqual(verify.version("opencode"), " v2.0.16 \r")
        self.assertEqual(run.call_args.args[0], ["opencode", "--version"])
        self.assertTrue(run.call_args.kwargs["check"])


if __name__ == "__main__":
    unittest.main()
