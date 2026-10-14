"""Pin text parity and process boundaries of the skills-index Python port."""
from __future__ import annotations

import io
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from generate_skills_index import REPO_ROOT, generate_skills_index, index_content


class SkillsIndexTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / "fixture space \u00e9"
        self.skills = self.root / ".llm" / "skills"
        self.skills.mkdir(parents=True)

    def skill(self, name: str, text: str, encoding: str = "utf-8") -> None:
        directory = self.skills / name
        directory.mkdir(exist_ok=True)
        (directory / "SKILL.md").write_bytes(text.encode(encoding))

    def test_committed_index_bytes(self) -> None:
        self.assertEqual(index_content(REPO_ROOT / ".llm" / "skills").encode("utf-8"),
                         (REPO_ROOT / ".llm" / "skills" / "index.md").read_bytes())

    def test_bom_and_line_boundaries(self) -> None:
        for encoding in ("utf-8", "utf-8-sig", "utf-16", "utf-32"):
            for separator in ("\n", "\r", "\r\n"):
                with self.subTest(encoding=encoding, separator=separator):
                    self.skill("alpha", separator.join(("---", "description: 'A\u2028B\x85C'",
                                                       "metadata:", "  category: Core", "---", "body")), encoding)
                    content = index_content(self.skills)
                    self.assertIn("| [alpha](./alpha/SKILL.md) | A\u2028B\x85C |\n", content)
                    self.assertIn("## Core Skills", content)

    def test_case_insensitive_keys_and_last_value(self) -> None:
        self.skill("alpha", "---\nDESCRIPTION: Old\nDescription: New\nMETADATA:\n  CATEGORY: core\n---\nbody")
        content = index_content(self.skills)
        self.assertIn("## Core Skills", content)
        self.assertIn("| New |", content)
        self.assertNotIn("Old", content)

    def test_utf16_ordinal_order(self) -> None:
        names = ("skill-2", "skill-10", "\ue000", "\U00010000")
        for name in names:
            self.skill(name, "---\ndescription: Text\n---\nbody")
        content = index_content(self.skills)
        expected = ("skill-10", "skill-2", "\U00010000", "\ue000")
        positions = [content.index(f"[{name}]") for name in expected]
        self.assertEqual(positions, sorted(positions))

    def test_skipped_sources(self) -> None:
        fixtures = {
            "no-frontmatter": "description: Text\nbody",
            "unclosed": "---\ndescription: Text\nbody",
            "no-description": "---\nname: empty\n---\nbody",
            "empty-description": "---\ndescription:\n---\nbody",
            "unknown-category": "---\ndescription: Text\nmetadata:\n  category: Other\n---\nbody",
            ".hidden": "---\ndescription: Text\n---\nbody",
        }
        for name, source in fixtures.items():
            self.skill(name, source)
        self.assertNotIn("| [", index_content(self.skills))

    def test_dotnet_whitespace(self) -> None:
        self.skill("alpha", "---\ndescription: \x1c\n---\nbody")
        self.assertNotIn("[alpha]", index_content(self.skills))
        self.skill("alpha", "---\ndescription: '\x1c'\n---\nbody")
        self.assertIn("| \x1c |", index_content(self.skills))

    def test_stdout_does_not_write_index(self) -> None:
        self.skill("alpha", "---\ndescription: Text\n---\nbody")
        output = io.StringIO()
        self.assertEqual(0, generate_skills_index(self.root, stdout=True, stream=output))
        self.assertEqual(index_content(self.skills), output.getvalue())
        self.assertFalse((self.skills / "index.md").exists())

    def test_output_bytes_and_missing_root(self) -> None:
        self.skill("alpha", "---\ndescription: Text\n---\nbody")
        output = self.root / "out.md"
        self.assertEqual(0, generate_skills_index(self.root, output_path=output))
        self.assertEqual(index_content(self.skills).encode("utf-8"), output.read_bytes())
        stream = io.StringIO()
        self.assertEqual(1, generate_skills_index(self.root / "missing", stream=stream))
        self.assertIn("Skills directory not found", stream.getvalue())

    def test_cli_stdout_bytes_and_diagnostics(self) -> None:
        self.check_stdout_process([sys.executable, str(REPO_ROOT / "tooling~" / "scripts" / "generate_skills_index.py")],
                                  ["--repo-root", str(self.root), "--stdout", "--verbose-output",
                                   "--output-path", str(self.root / "stdout.md")])

    def test_cli_normal_verbose_keeps_diagnostics_on_stdout(self) -> None:
        self.check_normal_verbose_process([sys.executable, str(REPO_ROOT / "tooling~" / "scripts" / "generate_skills_index.py")],
                                          ["--repo-root", str(self.root), "--verbose-output"])

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell is required for wrapper process coverage")
    def test_powershell_normal_verbose_keeps_diagnostics_on_stdout(self) -> None:
        wrapper = REPO_ROOT / "tooling~" / "scripts" / "generate-skills-index.ps1"
        self.check_normal_verbose_process(["pwsh", "-NoProfile", "-File", str(wrapper)],
                                          ["-RepoRoot", str(self.root), "-VerboseOutput"])

    def check_normal_verbose_process(self, command: list[str], arguments: list[str]) -> None:
        self.skill("alpha", "---\ndescription: Text\n---\nbody")
        self.skill("skipped", "missing frontmatter")
        result = subprocess.run(command + arguments, capture_output=True, check=False, timeout=60)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(b"No frontmatter in skipped", result.stdout)
        self.assertIn(b"[skills-index] Wrote", result.stdout)
        self.assertEqual(b"", result.stderr)
        self.assertEqual(index_content(self.skills).encode("utf-8"), (self.skills / "index.md").read_bytes())
        self.skills.rename(self.root / "saved-skills")
        failed = subprocess.run(command + arguments, capture_output=True, check=False, timeout=60)
        self.assertEqual(1, failed.returncode, failed.stderr)
        self.assertIn(b"Skills directory not found", failed.stdout)
        self.assertEqual(b"", failed.stderr)

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell is required for wrapper process coverage")
    def test_powershell_stdout_bytes_and_diagnostics(self) -> None:
        wrapper = REPO_ROOT / "tooling~" / "scripts" / "generate-skills-index.ps1"
        command = ("[Console]::OutputEncoding = [System.Text.Encoding]::ASCII; "
                   ". $args[0]; $code = New-SkillsIndex -RepoRoot $args[1] -Stdout -VerboseOutput "
                   "-OutputPath (Join-Path $args[1] 'stdout.md'); "
                   "if ($code -isnot [int]) { exit 99 }; exit $code")
        driver = self.root / "driver.ps1"
        driver.write_text(command, encoding="utf-8")
        self.check_stdout_process(["pwsh", "-NoProfile", "-File", str(driver), str(wrapper)], [str(self.root)])

    def check_stdout_process(self, command: list[str], arguments: list[str]) -> None:
        self.skill("unicode", "---\ndescription: Caf\u00e9 \u65e5\u672c \U0001f600\n---\nbody")
        self.skill("skipped", "missing frontmatter")
        expected_path = self.root / "expected.md"
        self.assertEqual(0, generate_skills_index(self.root, output_path=expected_path))
        expected = expected_path.read_bytes()
        result = subprocess.run(command + arguments, capture_output=True, check=False, timeout=60)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(expected, result.stdout)
        self.assertIn(b"No frontmatter in skipped", result.stderr)
        self.assertFalse((self.skills / "index.md").exists())
        self.assertFalse((self.root / "stdout.md").exists())
        self.assertNotIn(b"\r", result.stdout)
        self.assertFalse(result.stdout.startswith(b"\xef\xbb\xbf"))
        self.skills.rename(self.root / "saved-skills")
        failed = subprocess.run(command + arguments, capture_output=True, check=False, timeout=60)
        self.assertEqual(1, failed.returncode, failed.stderr)
        self.assertEqual(b"", failed.stdout)
        self.assertIn(b"Skills directory not found", failed.stderr)


if __name__ == "__main__":
    unittest.main()
