"""Behavior and byte-output checks for the instruction linter."""

from __future__ import annotations

import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from generate_skills_index import generate_skills_index
from lint_llm_instructions import ENTRYPOINTS, frontmatter_failures, hidden_directory, lint_llm_instructions

LINTER = Path(__file__).resolve().parents[1] / "lint_llm_instructions.py"

SKILL = "---\nname: valid-skill\ndescription: A fixture skill.\nmetadata:\n  category: Feature\n---\n\nBody.\n"


class InstructionLintTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.skill = self.root / ".llm/skills/valid-skill/SKILL.md"
        self.skill.parent.mkdir(parents=True)
        self.skill.write_bytes(SKILL.encode())
        (self.root / ".llm/context.md").write_text("# Context\n[index](./skills/index.md)\n")
        for path, link, _ in ENTRYPOINTS:
            destination = self.root / path
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text("[Context" + link + "\n")
        generate_skills_index(self.root)

    def run_lint(self, *, fix: bool = False) -> tuple[int, str]:
        stream = io.StringIO()
        result = lint_llm_instructions(self.root, fix=fix, stream=stream)
        return result, stream.getvalue()

    def test_success_output_bytes(self) -> None:
        code, output = self.run_lint()
        self.assertEqual(code, 0)
        self.assertEqual(output.encode(), (
            "\nValidating SKILL.md frontmatter...\n"
            "[llm-lint] All 1 SKILL.md files have valid frontmatter\n"
            "\nValidating skills index (index.md)...\n"
            "[llm-lint] Skills index is up to date\n"
            "\nValidating context.md...\n\n" + "=" * 60 + "\n"
            "[llm-lint] LLM instructions validation passed!\n" + "=" * 60 + "\n"
        ).encode())

    def test_frontmatter_boundaries(self) -> None:
        rows = (
            ("name: valid-skill", "NAME: 'valid-skill'", None),
            ("name: valid-skill", "name: Valid-Skill", "lowercase"),
            ("name: valid-skill", "name: " + "a" * 65, "65 chars"),
            ("description: A fixture skill.", "description: >", "single inline line"),
            ("description: A fixture skill.", "description: " + "x" * 1024, None),
            ("description: A fixture skill.", "description: " + "x" * 1025, "1025 chars"),
            ("description: A fixture skill.", "description: \x1c", None),
            ("category: Feature", "category: core", "unknown metadata.category"),
        )
        for old, new, expected in rows:
            with self.subTest(new=new[:80]):
                self.skill.write_bytes(SKILL.replace(old, new).encode())
                failures = frontmatter_failures(self.skill)
                if expected is None:
                    self.assertEqual(failures, [])
                else:
                    self.assertIn(expected, "\n".join(failures))

    def test_invalid_skill_fixtures_fail(self) -> None:
        rows = (
            ("# No frontmatter\n", "missing '---' delimited YAML frontmatter"),
            (SKILL.replace("name: valid-skill", "name: other-name"), "must match parent directory"),
            (SKILL.replace("description: A fixture skill.\n", ""), "missing required 'description'"),
            (SKILL.replace("A fixture skill.", "A \u2014 fixture skill."), "non-ASCII character"),
            (SKILL.split("\nBody.")[0], "skill body is empty"),
        )
        for content, expected in rows:
            with self.subTest(expected=expected):
                self.skill.write_bytes(content.encode())
                code, output = self.run_lint()
                self.assertEqual(code, 1, output)
                self.assertIn(expected, output)

    def test_invalid_repository_fixtures_fail(self) -> None:
        rows = (
            ("AGENTS.md", b"# No delegation\n", "entrypoint must delegate"),
            (".llm/context.md", b"# Title\nNo link here.\n", "must link to the generated index"),
            (".llm/context.md", b"# Title\n[index](./skills/index.md)\n# Second\n", "exactly one H1 (found 2)"),
            (".llm/context.md", b"# Title\n[index](./skills/index.md)\n<!-- BEGIN GENERATED SKILLS INDEX -->\n", "stale embedded-index marker"),
        )
        for relative, data, expected in rows:
            path = self.root / relative
            original = path.read_bytes()
            with self.subTest(relative=relative, expected=expected):
                try:
                    path.write_bytes(data)
                    code, output = self.run_lint()
                    self.assertEqual(code, 1, output)
                    self.assertIn(expected, output)
                finally:
                    path.write_bytes(original)

    def test_skill_directory_without_skill_file_fails(self) -> None:
        (self.skill.parent.parent / "empty-skill").mkdir()
        code, output = self.run_lint()
        self.assertEqual(code, 1, output)
        self.assertIn("Skill directory 'empty-skill' has no SKILL.md", output)

    def test_index_crlf_fails_encoding_gate(self) -> None:
        index = self.skill.parent.parent / "index.md"
        index.write_bytes(index.read_bytes().replace(b"\n", b"\r\n"))
        code, output = self.run_lint()
        self.assertEqual(code, 1, output)
        self.assertIn("contains CR (CRLF)", output)

    def run_cli(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(LINTER), "--repo-root", str(self.root), *arguments],
            capture_output=True, text=True, encoding="utf-8", check=False, timeout=60,
        )

    def test_cli_valid_and_missing_pointer(self) -> None:
        result = self.run_cli("--verbose-output")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("LLM instructions validation passed!", result.stdout)
        self.assertIn("Checking required paths", result.stdout)
        (self.root / "CLAUDE.md").unlink()
        result = self.run_cli()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("Claude Code entrypoint not found", result.stdout)

    def test_cli_stale_index_fix(self) -> None:
        self.skill.write_bytes(SKILL.replace("A fixture skill.", "Edited description.").encode())
        result = self.run_cli()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("Skills index index.md is out of date!", result.stdout)
        result = self.run_cli("--fix")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Regenerated index.md", result.stdout)
        self.assertIn(b"Edited description.", (self.skill.parent.parent / "index.md").read_bytes())
        self.assertEqual(self.run_cli().returncode, 0)

    def test_cli_missing_index_fails(self) -> None:
        (self.skill.parent.parent / "index.md").unlink()
        result = self.run_cli()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("index.md does not exist", result.stdout)

    def test_hidden_directory_platform_rules(self) -> None:
        rows = (("linux", ".dot", 0, True), ("linux", "visible", 2, False),
                ("win32", ".dot", 0, False), ("win32", "hidden", 2, True))
        for platform, name, attributes, expected in rows:
            path = self.root / name
            with self.subTest(platform=platform, name=name):
                with patch("lint_llm_instructions.sys.platform", platform), patch.object(
                    Path, "stat", return_value=SimpleNamespace(st_file_attributes=attributes)
                ):
                    self.assertEqual(hidden_directory(path), expected)

    def test_supplementary_description_reports_utf16_units(self) -> None:
        self.skill.write_bytes(SKILL.replace("A fixture skill.", "A 😀 fixture skill.").encode())
        self.assertIn("character(s) [� �]", frontmatter_failures(self.skill)[0])

    def test_bom_detection_preserves_content_and_encoding_gate(self) -> None:
        self.skill.write_bytes(b"\xef\xbb\xbf" + SKILL.encode())
        self.assertEqual(frontmatter_failures(self.skill), [])
        index = self.root / ".llm/skills/index.md"
        index.write_bytes(b"\xef\xbb\xbf" + index.read_bytes())
        code, output = self.run_lint()
        self.assertEqual(code, 1)
        self.assertIn("Skills index is up to date", output)
        self.assertIn("UTF-8 BOM", output)

    def test_missing_index_fix_writes_exact_generated_bytes(self) -> None:
        index = self.root / ".llm/skills/index.md"
        expected = index.read_bytes()
        index.unlink()
        code, output = self.run_lint(fix=True)
        self.assertEqual(code, 0)
        self.assertIn("Generated missing index.md", output)
        self.assertEqual(index.read_bytes(), expected)

    def test_determinism_uses_bytes(self) -> None:
        calls = 0

        def changing_generator(root: Path, *, output_path: Path | None = None,
                               verbose_output: bool = False, stream: io.StringIO) -> int:
            nonlocal calls
            calls += 1
            if output_path is not None:
                output_path.write_bytes(b"first\n" if calls == 1 else b"second\n")
            return 0

        with patch("lint_llm_instructions.generate_skills_index", changing_generator):
            code, output = self.run_lint()
        self.assertEqual(code, 1)
        self.assertIn("NON-deterministic", output)


if __name__ == "__main__":
    unittest.main()
