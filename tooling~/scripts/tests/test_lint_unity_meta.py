"""Red-green tests for tooling~/scripts/lint_unity_meta.py (issue #220 stage 2).

The meta linter's first contract suite (the ps1 had none). Builds fixture
roots with files on disk and synthetic `git ls-files` listings injected, so
every rule runs in-process without spawning git; the git path and the CLI
surface (including the real-repo self-lint CI runs) get their own tests.

Pinned: the three rule classes (orphan metas, missing metas, guid rules),
the folder-asset validity rule, the Unity-ignoration exemptions (dot-prefixed
and ~-suffixed segments), exit codes, the [meta-lint] message surface the
create-unity-meta skill documents, and the summary counters.
"""

from __future__ import annotations

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

import lint_unity_meta  # noqa: E402

LINTER_SCRIPT = SCRIPTS_DIR / "lint_unity_meta.py"

UPPER_GUID = "F1E2D3C4B5A6F7E8D1C2B3A4B5C6D7E8"
UPPER_GUID_LOWER = UPPER_GUID.lower()


def meta_text(guid: str, *, folder_asset: bool = False) -> str:
    lines = ["fileFormatVersion: 2", f"guid: {guid}"]
    if folder_asset:
        lines.append("folderAsset: yes")
    return "\n".join(lines) + "\n"


class MetaFixture(unittest.TestCase):
    """One fixture root per test. The clean baseline: one directory, one
    asset, and every path's meta tracked."""

    def setUp(self) -> None:
        self._temporary = tempfile.TemporaryDirectory()
        self.root = Path(self._temporary.name)
        self.add_file("Assets/Foo.asset")
        self.add_file("Assets.meta", meta_text("d0e1f2a3b4c5d6e7f0a1b2c3d4e5f6a7", folder_asset=True))
        self.add_file("Assets/Foo.asset.meta", meta_text("a1b2c3d4e5f6a7b8c1d2e3f4a5b6c7d8"))
        self.tracked: list[str] = ["Assets.meta", "Assets/Foo.asset", "Assets/Foo.asset.meta"]

    def tearDown(self) -> None:
        self._temporary.cleanup()

    def add_file(self, relative: str, text: str | None = None, encoding: str = "utf-8") -> None:
        target = self.root / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text("content\n" if text is None else text, encoding=encoding)

    def lint(self, tracked: list[str] | None = None, **kwargs: Any) -> tuple[int, str]:
        stream = io.StringIO()
        exit_code = lint_unity_meta.lint_unity_meta(
            self.root,
            tracked_paths=self.tracked if tracked is None else tracked,
            stream=stream,
            **kwargs,
        )
        return exit_code, stream.getvalue()


class CleanTreeTests(MetaFixture):
    def test_clean_tree_passes_with_the_full_summary(self) -> None:
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 0)
        self.assertIn("Unity meta hygiene passed", output)
        self.assertIn(
            "[meta-lint] Summary: 4 tracked files/directories checked "
            "(orphan metas: 0, missing metas: 0, guid problems: 0)",
            output,
        )

    def test_empty_tree_passes(self) -> None:
        exit_code, output = self.lint(tracked=[])
        self.assertEqual(exit_code, 0)
        self.assertIn("Summary: 0 tracked files/directories checked", output)

    def test_deep_directory_target_satisfies_the_orphan_rule(self) -> None:
        self.add_file("Assets/Deep/leaf.txt")
        self.add_file("Assets/Deep/leaf.txt.meta", meta_text("e1f2a3b4c5d6e7f8a1b2c3d4e5f6b7c8"))
        self.add_file("Assets/Deep.meta", meta_text("b2c3d4e5f6a7b8c1d2e3f4a5b6c7d8e1"))
        self.tracked += ["Assets/Deep.meta", "Assets/Deep/leaf.txt", "Assets/Deep/leaf.txt.meta"]
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 0, "a folder meta over a tracked directory must pass")
        self.assertNotIn("[meta-lint] ERROR: orphan meta", output)

    def test_unity_ignored_segments_need_no_meta(self) -> None:
        self.add_file(".llm/context.md")
        self.add_file("tooling~/scripts/tool.ps1")
        self.add_file("Assets/.hidden/leaf.txt")
        ignored = [".llm/context.md", "tooling~/scripts/tool.ps1", "Assets/.hidden/leaf.txt"]
        exit_code, output = self.lint(tracked=self.tracked + ignored)
        self.assertEqual(exit_code, 0, "dot-prefixed and ~-suffixed segments are invisible to Unity")
        self.assertNotIn("[meta-lint] ERROR: missing meta", output)

    def test_verbose_reports_per_file_and_quiet_omits_them(self) -> None:
        verbose_code, verbose_output = self.lint(verbose_output=True)
        self.assertEqual(verbose_code, 0)
        self.assertIn("[meta-lint] OK: Assets.meta: guid unique", verbose_output)
        self.assertIn("[meta-lint] OK: Assets/Foo.asset: meta present", verbose_output)
        self.assertIn("[meta-lint] OK: Assets: meta present", verbose_output)
        _, quiet_output = self.lint()
        self.assertNotIn("[meta-lint] OK:", quiet_output)


class OrphanMetaTests(MetaFixture):
    def test_meta_without_a_tracked_target_is_an_orphan(self) -> None:
        self.add_file("Assets/Bar.meta", meta_text("b1c2d3e4f5a6b7c8d1e2f3a4b5c6d7e8"))
        exit_code, output = self.lint(tracked=self.tracked + ["Assets/Bar.meta"])
        self.assertEqual(exit_code, 1, "an orphan meta must fail")
        self.assertIn(
            "[meta-lint] ERROR: orphan meta: 'Assets/Bar.meta' has no tracked target 'Assets/Bar'",
            output,
        )

    def test_folder_meta_targeting_a_tracked_file_is_an_orphan(self) -> None:
        self.add_file("Bogus.asset")
        self.add_file("Bogus.asset.meta", meta_text("c1d2e3f4a5b6c7d8e1f2a3b4c5d6e7f0", folder_asset=True))
        exit_code, output = self.lint(tracked=self.tracked + ["Bogus.asset", "Bogus.asset.meta"])
        self.assertEqual(exit_code, 1)
        self.assertIn(
            "[meta-lint] ERROR: folder meta 'Bogus.asset.meta' targets a tracked file, not a directory",
            output,
        )

    def test_folder_asset_lines_gate_the_file_target_rule(self) -> None:
        # The line value is case-insensitive (the ps1's -match family) and the
        # match is anchored: a value other than "yes" or an indented line is
        # not a folder-asset line, so a file target stays legal.
        for line, expect_ok in (
            ("folderAsset: yes", False),
            ("folderAsset: no", True),
            ("folderAsset: YES", False),
            (" folderAsset: yes", True),
        ):
            with self.subTest(line=line):
                guid = "e5f6a7b8c1d2e3f4a5b6c7d8e1f2a3b4"
                label = line.strip().replace(" ", "-")
                self.add_file(f"Bogus{label}.asset")
                self.add_file(f"Bogus{label}.asset.meta", f"fileFormatVersion: 2\nguid: {guid}\n{line}\n")
                exit_code, output = self.lint(
                    tracked=self.tracked + [f"Bogus{label}.asset", f"Bogus{label}.asset.meta"]
                )
                if expect_ok:
                    self.assertEqual(exit_code, 0, f"folderAsset line {line!r} must not be read as folder asset")
                    self.assertIn("Unity meta hygiene passed", output)
                else:
                    self.assertEqual(exit_code, 1)
                    self.assertIn("targets a tracked file, not a directory", output)

    def test_bom_and_crlf_metas_parse(self) -> None:
        # .NET's ReadAllLines stripped a BOM and split CRLF; the port keeps
        # both, so a meta written by Unity on Windows reads the same.
        self.add_file("Assets/Bom.asset")
        self.add_file("Assets/Bom.asset.meta", meta_text("c3d4e5f6a7b8c1d2e3f4a5b6c7d8e1f2"), encoding="utf-8-sig")
        bom_meta = self.root / "Assets/Bom.asset.meta"
        bom_meta.write_text(bom_meta.read_text(encoding="utf-8-sig").replace("\n", "\r\n"), encoding="utf-8")
        exit_code, output = self.lint(tracked=self.tracked + ["Assets/Bom.asset", "Assets/Bom.asset.meta"])
        self.assertEqual(exit_code, 0, "a BOM + CRLF meta must read like any other")
        self.assertIn("Unity meta hygiene passed", output)

    def test_non_utf8_meta_bytes_decode_as_replacement_not_as_a_read_error(self) -> None:
        # .NET's default decoder replaced invalid bytes with U+FFFD instead of
        # throwing; the port keeps that, so a stray latin-1 byte in a meta
        # cannot move a file from the orphan bucket into the guid bucket.
        self.add_file("Assets/Fuzzy.asset")
        (self.root / "Assets/Fuzzy.asset.meta").write_bytes(
            f"fileFormatVersion: 2\nguid: {UPPER_GUID_LOWER}\ncomment: caf\xe9\n".encode("latin-1")
        )
        exit_code, output = self.lint(tracked=self.tracked + ["Assets/Fuzzy.asset", "Assets/Fuzzy.asset.meta"])
        self.assertEqual(exit_code, 0, "invalid bytes decode as replacement; the guid still parses")
        self.assertIn("Unity meta hygiene passed", output)


class MissingMetaTests(MetaFixture):
    def test_tracked_file_without_its_meta_fails(self) -> None:
        # The meta file still exists on disk: the tracked listing (the remote)
        # is what the rule reads.
        self.tracked.remove("Assets/Foo.asset.meta")
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 1, "a missing meta must fail")
        self.assertIn(
            "[meta-lint] ERROR: missing meta: 'Assets/Foo.asset' is visible to Unity "
            "but 'Assets/Foo.asset.meta' is not tracked",
            output,
        )

    def test_tracked_directory_without_its_meta_fails(self) -> None:
        self.tracked.remove("Assets.meta")
        exit_code, output = self.lint()
        self.assertEqual(exit_code, 1)
        self.assertIn(
            "[meta-lint] ERROR: missing meta: directory 'Assets' is visible to Unity "
            "but 'Assets.meta' is not tracked",
            output,
        )


class GuidTests(MetaFixture):
    def test_unparsable_guids_fail(self) -> None:
        cases = [
            ("no guid line", "fileFormatVersion: 2\n"),
            ("31 hex", f"fileFormatVersion: 2\nguid: {'a' * 31}\n"),
            ("33 hex", f"fileFormatVersion: 2\nguid: {'a' * 33}\n"),
            ("non-hex", f"fileFormatVersion: 2\nguid: {'a' * 31}g\n"),
            ("indented line", f"fileFormatVersion: 2\n guid: {'a' * 32}\n"),
        ]
        for label, body in cases:
            with self.subTest(label=label):
                self.add_file("Assets/Bad.meta", body)
                exit_code, output = self.lint(tracked=self.tracked + ["Assets/Bad.meta"])
                self.assertEqual(exit_code, 1, f"{label} must fail")
                self.assertIn(
                    "[meta-lint] ERROR: 'Assets/Bad.meta' has no parsable 32-hex 'guid:' line",
                    output,
                )

    def test_multiple_guid_lines_fail(self) -> None:
        body = f"fileFormatVersion: 2\nguid: {'a' * 32}\nguid: {'b' * 32}\n"
        self.add_file("Assets/Bad.meta", body)
        exit_code, output = self.lint(tracked=self.tracked + ["Assets/Bad.meta"])
        self.assertEqual(exit_code, 1)
        self.assertIn("[meta-lint] ERROR: 'Assets/Bad.meta' declares multiple guid: lines", output)

    def test_guid_is_parsed_with_surrounding_spaces_and_normalized_to_lowercase(self) -> None:
        self.add_file("Assets/Spaced")
        self.add_file("Assets/Spaced.meta", f"fileFormatVersion: 2\nguid:   {UPPER_GUID}  \n")
        exit_code, output = self.lint(tracked=self.tracked + ["Assets/Spaced", "Assets/Spaced.meta"])
        self.assertEqual(exit_code, 0, "surrounding whitespace is allowed; case is normalized")
        self.add_file("Assets/Lower")
        self.add_file("Assets/Lower.meta", meta_text(UPPER_GUID_LOWER))
        duplicate_code, output = self.lint(tracked=self.tracked + ["Assets/Spaced", "Assets/Spaced.meta", "Assets/Lower", "Assets/Lower.meta"])
        self.assertEqual(duplicate_code, 1, "an upper/lowercase guid pair is still a duplicate")
        self.assertIn(
            f"[meta-lint] ERROR: duplicate guid '{UPPER_GUID_LOWER}' in 'Assets/Spaced.meta' "
            "and 'Assets/Lower.meta'",
            output,
        )

    def test_duplicate_guid_meta_still_runs_the_orphan_rule(self) -> None:
        self.add_file("Assets/Dup.meta", meta_text("a1b2c3d4e5f6a7b8c1d2e3f4a5b6c7d8"))
        exit_code, output = self.lint(tracked=self.tracked + ["Assets/Dup.meta"])
        self.assertEqual(exit_code, 1)
        self.assertIn("[meta-lint] ERROR: duplicate guid 'a1b2c3d4e5f6a7b8c1d2e3f4a5b6c7d8'", output)
        self.assertIn(
            "orphan meta: 'Assets/Dup.meta' has no tracked target 'Assets/Dup'",
            output,
        )


class DiscoveryTests(MetaFixture):
    def test_unreadable_meta_reports_and_skips_the_orphan_rule(self) -> None:
        # 'Ghost.meta' is tracked but a directory on disk, so the read fails;
        # the guid error is what reports and the orphan rule is not reached.
        (self.root / "Ghost.meta").mkdir()
        exit_code, output = self.lint(tracked=self.tracked + ["Ghost.meta"])
        self.assertEqual(exit_code, 1)
        self.assertIn("[meta-lint] ERROR: 'Ghost.meta' could not be read:", output)
        self.assertNotIn("'Ghost.meta' has no tracked target", output)

    def test_git_discovery_lists_the_staged_tree(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(["git", "init", "-q"], cwd=root, check=True)
            staged = [
                ("Assets/Foo.asset", None),
                ("Assets.meta", meta_text("d0e1f2a3b4c5d6e7f0a1b2c3d4e5f6a7")),
                ("Assets/Foo.asset.meta", meta_text("a1b2c3d4e5f6a7b8c1d2e3f4a5b6c7d8")),
                # A non-ASCII path exercises the NUL-delimited decode (-z keeps
                # raw bytes; core.quotepath cannot quote them).
                ("Assets/Föld.asset", None),
                ("Assets/Föld.asset.meta", meta_text("f0a1b2c3d4e5f6a7b8c1d2e3f4a5b6c7")),
            ]
            for relative, text in staged:
                target = root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text("content\n" if text is None else text, encoding="utf-8")
            subprocess.run(["git", "add", "-A"], cwd=root, check=True)
            stream = io.StringIO()
            exit_code = lint_unity_meta.lint_unity_meta(root, stream=stream)
            self.assertEqual(exit_code, 0, "the staged tree is the remote")
            self.assertIn("Unity meta hygiene passed", stream.getvalue())

    def test_git_failure_fails_with_the_ps1_message(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            stream = io.StringIO()
            exit_code = lint_unity_meta.lint_unity_meta(Path(temporary), stream=stream)
            self.assertEqual(exit_code, 1)
            self.assertIn("git ls-files failed; is", stream.getvalue())
            self.assertIn("a git repository?", stream.getvalue())


class CliTests(unittest.TestCase):
    def test_cli_flags_match_the_ps1_surface(self) -> None:
        """The pre-commit hook and CI call the CLI, not the module; pin the
        argument names the delegator forwards. The CLI discovery path is the
        git one, so the fixture is a staged repo like a real checkout."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            subprocess.run(["git", "init", "-q"], cwd=root, check=True)
            (root / "Assets").mkdir()
            (root / "Assets" / "Foo.asset").write_text("content\n", encoding="utf-8")
            (root / "Assets.meta").write_text(meta_text("d0e1f2a3b4c5d6e7f0a1b2c3d4e5f6a7"), encoding="utf-8")
            (root / "Assets" / "Foo.asset.meta").write_text(
                meta_text("a1b2c3d4e5f6a7b8c1d2e3f4a5b6c7d8"), encoding="utf-8"
            )
            subprocess.run(["git", "add", "-A"], cwd=root, check=True)
            result = subprocess.run(
                [sys.executable, str(LINTER_SCRIPT), "--verbose-output", "--repo-root", str(root)],
                capture_output=True,
                text=True,
                check=True,
            )
            self.assertIn("Unity meta hygiene passed", result.stdout)
            self.assertIn("[meta-lint] OK: Assets/Foo.asset: meta present", result.stdout)

    def test_cli_defaults_to_this_repo_and_passes(self) -> None:
        """No --repo-root: the linter runs on the checkout it lives in - the
        same real tree the Unity Meta Lint workflow and the pre-commit hook
        lint."""
        result = subprocess.run(
            [sys.executable, str(LINTER_SCRIPT)],
            capture_output=True,
            text=True,
            check=True,
        )
        self.assertIn("[meta-lint] Summary:", result.stdout)
        self.assertIn("Unity meta hygiene passed", result.stdout)


if __name__ == "__main__":
    unittest.main()
