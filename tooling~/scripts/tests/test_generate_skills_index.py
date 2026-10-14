"""Pin text parity and process boundaries of the skills-index Python port."""
from __future__ import annotations

import io
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
        self.root = Path(self.temporary.name)
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


if __name__ == "__main__":
    unittest.main()
