"""Exercise the real shell entry point with hostile rc files and workspace paths."""
from __future__ import annotations

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[3]
INSTALLER = ROOT / ".devcontainer" / "install-env-autoload.sh"
START = b"# >>> dxcommandterminal .env.local autoload >>>"
END = b"# <<< dxcommandterminal .env.local autoload <<<"


@unittest.skipUnless(shutil.which("bash") and os.name != "nt", "Bash lifecycle runs on Linux")
class AutoloadInstallerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.scratch = tempfile.TemporaryDirectory()
        self.addCleanup(self.scratch.cleanup)
        self.root = Path(self.scratch.name)
        self.rc = self.root / ".bashrc"

    def install(self, workspace: str = "/workspace") -> subprocess.CompletedProcess[bytes]:
        environment = {**os.environ, "HOME": str(self.root), "TMPDIR": str(self.root), "BASH_ENV": "/dev/null"}
        return subprocess.run(["bash", "-c", 'source "$1"; install_env_local_autoload "$2"', "_",
                               str(INSTALLER), workspace], env=environment, capture_output=True, timeout=40)

    def test_malformed_blocks_refuse_without_changing_either_rc(self) -> None:
        for markers in ((START,), (END, START), (START, START, END, END), (START, END, END)):
            with self.subTest(markers=markers):
                source = b"export KEEP=1\n" + b"\n".join(markers) + b"\nalias keep=true\n"
                self.rc.write_bytes(source)
                profile = self.root / ".profile"
                profile.write_bytes(b"keep profile without newline")
                result = self.install()
                self.assertEqual(result.returncode, 1, result.stderr)
                self.assertIn(b"Malformed autoload block", result.stderr)
                self.assertEqual(self.rc.read_bytes(), source)
                self.assertEqual(profile.read_bytes(), b"keep profile without newline")

    def test_all_files_validate_before_any_write(self) -> None:
        self.rc.write_bytes(b"keep bash\n")
        (self.root / ".profile").write_bytes(START + b"\n")
        self.assertEqual(self.install().returncode, 1)
        self.assertEqual(self.rc.read_bytes(), b"keep bash\n")

    def test_existing_block_moves_after_noninteractive_guard(self) -> None:
        self.rc.write_bytes(b"case $- in *i*) ;; *) return;; esac\n")
        self.assertEqual(self.install().returncode, 0)
        installed = self.rc.read_bytes()
        block = installed[installed.index(START):]
        guard = b"case $- in *i*) ;; *) return;; esac\n"
        self.rc.write_bytes(block + guard)
        self.assertEqual(self.install().returncode, 0)
        actual = self.rc.read_bytes()
        self.assertLess(actual.index(guard), actual.index(START))
        self.assertEqual(actual.count(START), 1)

    def test_replaces_duplicates_and_preserves_other_bytes(self) -> None:
        source = b"\xffkeep\r\n" + START + b"\nold\n" + END + b"\n" + START + b"\nold2\n" + END + b"\ntail"
        self.rc.write_bytes(source)
        self.assertEqual(self.install().returncode, 0)
        actual = self.rc.read_bytes()
        self.assertTrue(actual.startswith(b"\xffkeep\r\ntail\n"), actual)
        self.assertEqual(actual.count(START), 1)
        self.assertNotIn(b"old", actual)

    def test_quoted_path_is_literal_and_installation_is_idempotent(self) -> None:
        workspace = str(self.root / "project ' $(touch leaked) `touch leaked2` \\ folder")
        self.rc.write_bytes(b"keep " + START + b"\n")
        self.assertEqual(self.install(workspace).returncode, 0)
        first = self.rc.read_bytes()
        timestamp = self.rc.stat().st_mtime_ns
        self.assertEqual(self.install(workspace).returncode, 0)
        self.assertEqual(self.rc.read_bytes(), first)
        self.assertEqual(self.rc.stat().st_mtime_ns, timestamp)
        result = subprocess.run(["bash", "-c", '. "$1"; printf "%s" "$DXT_WORKSPACE_ROOT"', "_", str(self.rc)],
                                cwd=self.root, env={**os.environ, "BASH_ENV": "/dev/null"}, capture_output=True)
        self.assertEqual(result.stdout, os.fsencode(workspace))
        self.assertFalse((self.root / "leaked").exists())
        self.assertFalse((self.root / "leaked2").exists())

    def test_symlink_and_file_permissions_survive(self) -> None:
        target = self.root / "actual-rc"
        target.write_bytes(b"keep\n")
        target.chmod(0o640)
        self.rc.symlink_to(target)
        self.assertEqual(self.install().returncode, 0)
        self.assertTrue(self.rc.is_symlink())
        self.assertEqual(target.stat().st_mode & 0o777, 0o640)
        self.assertIn(START, target.read_bytes())

    def test_only_lf_delimits_marker_lines(self) -> None:
        source = b"keep\r" + START + b"\nkeep\r" + END + b"\n"
        self.rc.write_bytes(source)
        self.assertEqual(self.install().returncode, 0)
        self.assertTrue(self.rc.read_bytes().startswith(source))

    def test_missing_rc_files_are_not_created(self) -> None:
        self.assertEqual(self.install().returncode, 0)
        self.assertFalse(self.rc.exists())
        self.assertFalse((self.root / ".profile").exists())


if __name__ == "__main__":
    unittest.main()
