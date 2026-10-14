"""Red-green tests for tooling~/scripts/lint_skill_sizes.py (issue #220 stage 1).

The typed-python port of test-lint-skill-sizes.ps1. Builds isolated fixture
roots and asserts the boundary behavior of the line limits (269 OK / 270
critical / 300 at-limit / 301 error), the generated index exemption, recursive
coverage, the fail-on-critical flag, and the CLI surface.

The linter runs in-process (one import, no interpreter spawn per case); one
CLI case pins the argument surface the pre-commit hook and CI invoke.
"""

from __future__ import annotations

import contextlib
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from typing import Any

TESTS_DIR = Path(__file__).resolve().parent
SCRIPTS_DIR = TESTS_DIR.parent
sys.path.insert(0, str(SCRIPTS_DIR))

import lint_skill_sizes  # noqa: E402

FIXTURE_SKILL = TESTS_DIR / "fixtures" / "valid-skill.md"
LINTER_SCRIPT = SCRIPTS_DIR / "lint_skill_sizes.py"

VALID_SKILL_TEXT = FIXTURE_SKILL.read_text(encoding="utf-8")
CONTEXT_TEXT = "# Title\n\nSee [index](./skills/index.md).\n"


class SkillSizesFixture(unittest.TestCase):
    """Builds one fixture root per test and removes it on teardown."""

    def setUp(self) -> None:
        self._temporary = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary.name)
        skill_dir = self.root / ".llm" / "skills" / "valid-skill"
        skill_dir.mkdir(parents=True)
        (skill_dir / "SKILL.md").write_text(VALID_SKILL_TEXT, encoding="utf-8")
        (self.root / ".llm" / "context.md").write_text(CONTEXT_TEXT, encoding="utf-8")

    def tearDown(self) -> None:
        self._temporary.cleanup()

    def write_sized(self, relative: str, lines: int) -> None:
        target = self.root / ".llm" / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(
            "".join(f"line {index}\n" for index in range(1, lines + 1)),
            encoding="utf-8",
        )

    def lint(self, **kwargs: Any) -> tuple[int, str]:
        stream = io.StringIO()
        exit_code = lint_skill_sizes.lint_skill_sizes(self.root, stream=stream, **kwargs)
        return exit_code, stream.getvalue()


class SkillSizesBoundaryTests(SkillSizesFixture):
    def test_files_at_or_under_269_lines_pass(self) -> None:
        self.write_sized("skills/valid-skill/references/deep.md", 269)
        exit_code, output = self.lint(verbose_output=True)
        self.assertEqual(exit_code, 0, "269-line file must pass")
        self.assertIn("All files within size limits", output)
        self.assertIn("[skill-sizes] OK: skills/valid-skill/references/deep.md: 269 lines", output)

    def test_file_at_exactly_270_lines_is_critical_but_exits_zero(self) -> None:
        self.write_sized("skills/valid-skill/references/big.md", 270)
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 0, "270-line file must warn, not fail")
        self.assertIn("[skill-sizes] CRITICAL: skills/valid-skill/references/big.md: 270 lines", output)

    def test_file_at_exactly_300_lines_is_critical_at_limit(self) -> None:
        self.write_sized("skills/valid-skill/references/big.md", 300)
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 0, "300-line file must warn, not fail")
        self.assertIn("AT the 300 line limit", output)

    def test_file_over_300_lines_fails(self) -> None:
        self.write_sized("skills/valid-skill/references/big.md", 301)
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 1, "301-line file must fail")
        self.assertIn(
            "[skill-sizes] ERROR: skills/valid-skill/references/big.md: 301 lines (max: 300) - MUST split/reduce",
            output,
        )

    def test_generated_index_is_exempt_from_limits(self) -> None:
        self.write_sized("skills/index.md", 500)
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 0, "index.md must be exempt")
        self.assertNotIn("index.md", output)

    def test_over_limit_context_fails(self) -> None:
        (self.root / ".llm" / "context.md").write_text(
            "# Title\n\n" + "".join(f"ctx line {index}\n" for index in range(1, 306)),
            encoding="utf-8",
        )
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 1, "over-limit context.md must fail")
        self.assertIn("context.md: 307 lines (max: 300)", output)

    def test_fail_on_critical_promotes_the_critical_band_to_a_failure(self) -> None:
        self.write_sized("skills/valid-skill/references/big.md", 285)
        exit_code, _ = self.lint(fail_on_critical=True)
        self.assertEqual(exit_code, 1, "-FailOnCritical must fail the run")

    def test_missing_llm_directory_fails_fast(self) -> None:
        with tempfile.TemporaryDirectory() as empty:
            stream = io.StringIO()
            exit_code = lint_skill_sizes.lint_skill_sizes(Path(empty), stream=stream)
            self.assertEqual(exit_code, 1, "missing .llm dir must fail")
            self.assertIn("LLM directory not found", stream.getvalue())


class SkillSizesCliTests(unittest.TestCase):
    def test_cli_flags_match_the_ps1_surface(self) -> None:
        """The pre-commit hook and CI call the CLI, not the module; pin the
        argument names the delegator forwards."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / ".llm").mkdir()
            (root / ".llm" / "context.md").write_text("# Title\n", encoding="utf-8")
            result = subprocess.run(
                [
                    sys.executable,
                    str(LINTER_SCRIPT),
                    "--verbose-output",
                    "--repo-root",
                    str(root),
                ],
                capture_output=True,
                text=True,
                check=True,
            )
            self.assertIn("All files within size limits", result.stdout)
            self.assertIn("[skill-sizes] OK: context.md: 1 lines", result.stdout)


if __name__ == "__main__":
    unittest.main()
