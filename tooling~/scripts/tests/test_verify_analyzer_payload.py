"""Exercise payload verification with real Git repositories and .NET builds."""
from __future__ import annotations

import io
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_analyzer_payload as verifier

SCRIPT = Path(verifier.__file__).resolve()


def git(root: Path, *arguments: str) -> None:
    subprocess.run(["git", *arguments], cwd=root, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def commit(root: Path) -> None:
    git(root, "add", ".")
    git(root, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-qm", "Fixture")


class ArchiveTests(unittest.TestCase):
    def test_selected_repository_and_committed_bytes_survive_foreign_cwd(self) -> None:
        with tempfile.TemporaryDirectory(prefix="payload archive ") as temporary:
            base = Path(temporary)
            selected, caller, output = (base / name for name in ("selected", "caller", "output"))
            for root, value in ((selected, b"selected\x00\xff"), (caller, b"caller")):
                (root / "Generator~").mkdir(parents=True)
                (root / "Generator~/source.bin").write_bytes(value)
                git(root, "init", "-q")
                commit(root)
            (selected / "Generator~/source.bin").write_bytes(b"uncommitted")
            (selected / "Generator~/untracked").touch()
            output.mkdir()
            previous = Path.cwd()
            try:
                os.chdir(caller)
                checkouts = verifier.extract_checkouts(selected, output)
            finally:
                os.chdir(previous)
            self.assertNotEqual(checkouts[0], checkouts[1])
            for checkout in checkouts:
                self.assertEqual((checkout / "Generator~/source.bin").read_bytes(), b"selected\x00\xff")
                self.assertFalse((checkout / "Generator~/untracked").exists())

    def test_archive_failure_is_not_reported_as_success(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            git(root, "init", "-q")
            with self.assertRaisesRegex(ValueError, "git archive.*failed"):
                verifier.extract_checkouts(root, root)

    def test_archive_entries_cannot_escape_or_link_outside_checkouts(self) -> None:
        for name, kind in (("../outside", tarfile.REGTYPE), ("Generator~/link", tarfile.SYMTYPE), ("Generator~/hard", tarfile.LNKTYPE)):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                with tarfile.open(root / "generator.tar", "w") as archive:
                    info = tarfile.TarInfo(name)
                    info.type = kind
                    info.linkname = "../../outside" if kind != tarfile.REGTYPE else ""
                    archive.addfile(info, io.BytesIO(b""))
                with patch.object(verifier.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
                    with self.assertRaisesRegex(ValueError, "unsupported archive entry"):
                        verifier.extract_checkouts(root, root)
                self.assertFalse((root.parent / "outside").exists())


@unittest.skipUnless(shutil.which("dotnet"), ".NET SDK required for real compiler controls")
class CompilerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="payload compiler ")
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.repo = self.base / "selected repository"
        self.repo.mkdir()
        self.caller = self.base / "caller repository"
        (self.caller / "Generator~").mkdir(parents=True)
        (self.caller / "Generator~/unrelated.txt").write_text("different repository")
        git(self.caller, "init", "-q")
        commit(self.caller)
        self.scratch = self.base / "scratch"
        self.scratch.mkdir()
        git(self.repo, "init", "-q")
        sdk = subprocess.check_output(["dotnet", "--version"], text=True).strip().split(".")[0]
        self.target = f"net{sdk}.0"
        for payload in verifier.PAYLOADS:
            project = self.repo / payload.project
            project.parent.mkdir(parents=True)
            project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>{self.target}</TargetFramework><DebugType>none</DebugType>
<Deterministic>true</Deterministic><IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion></PropertyGroup>
<Target Name="Payload" AfterTargets="Build"><MakeDir Directories="$(AnalyzerPayloadOutputDir)" />
<Copy SourceFiles="$(TargetPath)" DestinationFolder="$(AnalyzerPayloadOutputDir)" /></Target>
</Project>''')
            (project.parent / "Fixture.cs").write_text('public static class Fixture { public static string Value => "baseline"; }')
        commit(self.repo)
        self.rebuild_shipped()

    def rebuild_shipped(self) -> None:
        shipped = self.repo / "Runtime/Analyzers"
        for payload in verifier.PAYLOADS:
            result = subprocess.run(
                ["dotnet", "build", str(self.repo / payload.project), "-c", "Release", "--nologo", f"/p:AnalyzerPayloadOutputDir={shipped}"],
                cwd=self.repo, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120,
            )
            self.assertEqual(result.returncode, 0, result.stdout)

    def run_cli(self, *arguments: str) -> subprocess.CompletedProcess[str]:
        environment = dict(os.environ, TMPDIR=str(self.scratch), TMP=str(self.scratch), TEMP=str(self.scratch))
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--repo-root", str(self.repo), *arguments],
            cwd=self.caller, env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120,
        )
        self.assertFalse(any(path.name.startswith("dxcommandterminal-analyzer-") for path in self.scratch.iterdir()), result.stdout)
        return result

    def change_source(self, value: str) -> None:
        source = (self.repo / verifier.PAYLOADS[0].project).parent / "Fixture.cs"
        source.write_text(f'public static class Fixture {{ public static string Value => "{value}"; }}')

    def test_foreign_repository_two_checkout_passes_without_touching_payloads(self) -> None:
        before = {payload.dll: (self.repo / "Runtime/Analyzers" / payload.dll).read_bytes() for payload in verifier.PAYLOADS}
        result = self.run_cli("--two-checkout")
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(result.stdout.count("payload matches its sources"), 2)
        self.assertEqual(result.stdout.count("byte-identical from two checkout directories"), 2)
        for name, data in before.items():
            self.assertEqual((self.repo / "Runtime/Analyzers" / name).read_bytes(), data)

    def test_both_modes_reject_uncommitted_source_drift(self) -> None:
        source = (self.repo / verifier.PAYLOADS[0].project).parent / "Fixture.cs"
        stamp = source.stat()
        self.change_source("changed")
        os.utime(source, ns=(stamp.st_atime_ns, stamp.st_mtime_ns))
        for arguments in ((), ("--two-checkout",)):
            with self.subTest(arguments=arguments):
                result = self.run_cli(*arguments)
                self.assertEqual(result.returncode, 1, result.stdout)
                self.assertIn("payload has drifted from its sources", result.stdout)

    def test_two_checkout_rejects_working_tree_and_payload_agreeing_but_head_differing(self) -> None:
        self.change_source("changed")
        self.rebuild_shipped()
        result = self.run_cli("--two-checkout")
        self.assertEqual(result.returncode, 1, result.stdout)
        self.assertIn("payload matches its sources", result.stdout)
        self.assertIn("checkout-directory rebuild does not match", result.stdout)

    def test_compiler_failure_fails_closed(self) -> None:
        source = (self.repo / verifier.PAYLOADS[0].project).parent / "Fixture.cs"
        source.write_text("#error Compiler failure control")
        result = self.run_cli("--two-checkout")
        self.assertEqual(result.returncode, 1, result.stdout)
        self.assertIn("Compiler failure control", result.stdout)
        self.assertIn("ERROR: rebuilding", result.stdout)

    def test_missing_payload_fails_before_building(self) -> None:
        (self.repo / "Runtime/Analyzers" / verifier.PAYLOADS[1].dll).unlink()
        result = self.run_cli()
        self.assertEqual(result.returncode, 1, result.stdout)
        self.assertIn("shipped internal analyzers payload not found", result.stdout)
        self.assertNotIn("Determining projects", result.stdout)


if __name__ == "__main__":
    unittest.main()
