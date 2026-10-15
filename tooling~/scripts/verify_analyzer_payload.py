"""Compare shipped analyzer binaries with working-tree and committed-source builds."""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
from pathlib import Path
import subprocess
import shutil
import tarfile
import tempfile


REPO_ROOT = Path(__file__).resolve().parents[2]
PREFIX = "[analyzer-payload]"


@dataclass(frozen=True)
class Payload:
    name: str
    label: str

    @property
    def project(self) -> Path:
        return Path("Generator~") / self.name / (self.name + ".csproj")

    @property
    def dll(self) -> str:
        return self.name + ".dll"


PAYLOADS = (
    Payload("WallstopStudios.DxCommandTerminal.SourceGenerators", "generator"),
    Payload("WallstopStudios.DxCommandTerminal.Analyzers", "internal analyzers"),
)


def payload_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def build_payload(project: Path, output: Path, repo_root: Path) -> Path:
    command = ["dotnet", "build", str(project), "-c", "Release", "--nologo", "--no-incremental", f"/p:AnalyzerPayloadOutputDir={output}"]
    if subprocess.run(command, cwd=repo_root, check=False).returncode != 0:
        raise ValueError(f"rebuilding {project} into {output} failed")
    built = output / project.with_suffix(".dll").name
    if not built.is_file():
        raise ValueError(f"rebuild did not produce a payload at {built}")
    return built


def compare_shipped(payload: Payload, repo_root: Path, built: Path) -> None:
    shipped_hash = payload_hash(repo_root / "Runtime/Analyzers" / payload.dll)
    built_hash = payload_hash(built)
    if shipped_hash != built_hash:
        raise ValueError(
            f"shipped {payload.label} payload has drifted from its sources.\n"
            f"{PREFIX} ERROR: shipped: {shipped_hash}\n"
            f"{PREFIX} ERROR: rebuilt: {built_hash}\n"
            f"{PREFIX} ERROR: Rebuild the payload: dotnet build {payload.project.as_posix()} -c Release"
        )
    print(f"{PREFIX} OK: shipped {payload.label} payload matches its sources", flush=True)


def extract_checkouts(repo_root: Path, directory: Path) -> tuple[Path, Path]:
    archive = directory / "generator.tar"
    command = ["git", "archive", "--format=tar", f"--output={archive}", "HEAD", "Generator~"]
    if subprocess.run(command, cwd=repo_root, check=False).returncode != 0:
        raise ValueError("git archive of Generator~ failed")
    roots = (directory / "checkout-a", directory / "checkout-b")
    with tarfile.open(archive) as source:
        members = source.getmembers()
        for root in roots:
            root.mkdir()
            # Only regular source files and directories belong in the build snapshot.
            for member in members:
                target = (root / member.name).resolve()
                if not target.is_relative_to(root.resolve()) or not (member.isfile() or member.isdir()):
                    raise ValueError(f"unsupported archive entry: {member.name!r}")
            for member in members:
                target = root / member.name
                if member.isdir():
                    target.mkdir(parents=True, exist_ok=True)
                    continue
                target.parent.mkdir(parents=True, exist_ok=True)
                content = source.extractfile(member)
                if content is None:
                    raise ValueError(f"archive entry has no content: {member.name!r}")
                with content, target.open("wb") as output:
                    shutil.copyfileobj(content, output)
                target.chmod(member.mode)
    return roots


def verify(repo_root: Path, two_checkout: bool) -> None:
    for payload in PAYLOADS:
        project = repo_root / payload.project
        if not project.is_file():
            raise ValueError(f"payload project not found: {project}")
        shipped = repo_root / "Runtime/Analyzers" / payload.dll
        if not shipped.is_file():
            raise ValueError(f"shipped {payload.label} payload not found: {shipped}")
    with tempfile.TemporaryDirectory(prefix="dxcommandterminal-analyzer-") as temporary:
        scratch = Path(temporary)
        for payload in PAYLOADS:
            built = build_payload(repo_root / payload.project, scratch, repo_root)
            compare_shipped(payload, repo_root, built)
        if not two_checkout:
            return
        roots = extract_checkouts(repo_root, scratch)
        hashes: dict[tuple[str, str], str] = {}
        for root in roots:
            output = root / "build"
            output.mkdir()
            for payload in PAYLOADS:
                project = root / payload.project
                if not project.is_file():
                    raise ValueError(f"extracted checkout is missing a payload project: {project}")
                hashes[(root.name, payload.dll)] = payload_hash(build_payload(project, output, repo_root))
        for payload in PAYLOADS:
            first, second = (hashes[(root.name, payload.dll)] for root in roots)
            if first != second:
                raise ValueError(
                    f"the {payload.label} payload is not reproducible across checkout directories.\n"
                    f"{PREFIX} ERROR: checkout-a: {first}\n{PREFIX} ERROR: checkout-b: {second}"
                )
            shipped = payload_hash(repo_root / "Runtime/Analyzers" / payload.dll)
            if first != shipped:
                raise ValueError(
                    f"the checkout-directory rebuild does not match the shipped {payload.label} payload.\n"
                    f"{PREFIX} ERROR: shipped: {shipped}\n{PREFIX} ERROR: checkouts: {first}"
                )
            print(f"{PREFIX} OK: {payload.label} payload is byte-identical from two checkout directories", flush=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=REPO_ROOT)
    parser.add_argument("--two-checkout", action="store_true")
    args = parser.parse_args()
    try:
        verify(args.repo_root.resolve(), args.two_checkout)
    except (OSError, ValueError, tarfile.TarError) as error:
        print(f"{PREFIX} ERROR: {error}", flush=True)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
