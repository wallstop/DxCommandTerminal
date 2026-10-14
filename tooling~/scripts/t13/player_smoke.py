"""Run release Mono players from verified, retained clean artifact imports."""
from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
from typing import Any

ROOT = Path(__file__).resolve().parents[3]
HARNESS_SOURCE = Path(__file__).read_bytes()
HARNESS_SHA256 = hashlib.sha256(HARNESS_SOURCE).hexdigest()
FIXTURES = ROOT / "tooling~" / "compat" / "player-smoke"
PROFILES = {"legacy": 0, "input-system": 1, "both": 2}
ERRORS = re.compile(r"(?:warning|error) [A-Za-z]+\d+|Aborting batchmode|Scripts have compiler errors|Exception:|falling back to .*discovery|Failed to (?:bind command|bind it|probe for a generated command catalog)")


def sha256(file: Path) -> str:
    return hashlib.sha256(file.read_bytes()).hexdigest()


def validate_payload(artifact: Path, project: Path, source_revision: str | None = None) -> dict[str, str]:
    hashes: dict[str, str] = {}
    source_files: dict[str, bytes] | None = None
    if source_revision is not None:
        source_archive = subprocess.check_output(["git", "archive", source_revision], cwd=ROOT, timeout=60)
        source_files = {}
        with tarfile.open(fileobj=io.BytesIO(source_archive), mode="r:") as source_tar:
            for entry in source_tar.getmembers():
                file = source_tar.extractfile(entry) if entry.isfile() else None
                if file is not None:
                    source_files[entry.name] = file.read()
    with tarfile.open(artifact, "r:gz") as archive:
        members = {member.name: member for member in archive.getmembers()}
        for name, member in members.items():
            if not name.endswith("/pathname"):
                continue
            pathname_file = archive.extractfile(member)
            if pathname_file is None:
                raise ValueError(f"Unreadable pathname: {name}")
            pathname = pathname_file.read().decode("utf-8").strip()
            prefix = "Packages/com.wallstop-studios.dxcommandterminal/"
            if not pathname.startswith(prefix):
                if pathname == prefix.rstrip("/"):
                    continue
                raise ValueError(f"Unexpected artifact pathname: {pathname}")
            relative = pathname[len(prefix):]
            if ".." in Path(relative).parts or "\\" in relative:
                raise ValueError(f"Unsafe pathname: {pathname}")
            asset = members.get(name.removesuffix("pathname") + "asset")
            if asset is None:
                continue
            payload = archive.extractfile(asset)
            if payload is None:
                raise ValueError(f"Unreadable asset: {pathname}")
            digest = hashlib.sha256(payload.read()).hexdigest()
            source_bytes = source_files.get(relative) if source_files is not None else (ROOT / relative).read_bytes()
            if source_bytes is None or hashlib.sha256(source_bytes).hexdigest() != digest:
                raise ValueError(f"Artifact bytes differ from committed package source: {relative}")
            for file in [project / pathname]:
                if not file.is_file() or sha256(file) != digest:
                    raise ValueError(f"Artifact bytes differ from source/import: {file}")
            hashes[relative] = digest
    if "package.json" not in hashes or not any(name.endswith(".dll") and "/Analyzers/" in name for name in hashes):
        raise ValueError("Artifact lacks package identity or generator payload")
    return hashes


def validate_profile(project: Path, profile: str) -> None:
    if (project / "Temp" / "UnityLockfile").exists():
        raise ValueError(f"Project is locked: {project}")
    settings = (project / "ProjectSettings" / "ProjectSettings.asset").read_text(encoding="utf-8-sig")
    values = re.findall(r"^  activeInputHandler: (\d+)\s*$", settings, re.MULTILINE)
    if values != [str(PROFILES[profile])]:
        raise ValueError(f"Input profile mismatch: {profile}, activeInputHandler={values}")


def checked_player_report(file: Path, profile: str, version: str) -> dict[str, Any]:
    report = json.loads(file.read_text(encoding="utf-8-sig"))
    expected = {"passed": True, "unityVersion": version, "platform": "WindowsPlayer", "developmentBuild": False, "monoBackend": True, "inputSystem": profile != "legacy", "legacyInput": profile != "input-system", "catalogPresent": True, "catalogEntries": 2, "catalogBindersExecuted": 2, "generatedCalls": 1, "privateCalls": 1, "manualCalls": 1}
    for name, value in expected.items():
        actual = report.get(name)
        if type(actual) is not type(value) or actual != value:
            raise ValueError(f"Player verdict mismatch: {name}={actual!r}, expected {value!r}")
    if report.get("failure") not in (None, ""):
        raise ValueError(f"Player reported failure: {report['failure']}")
    return report


def install_fixtures(project: Path, previous: dict[str, Any] | None = None) -> dict[str, str]:
    legacy_driver = project / "Assets" / "DxTerminalImportDrill.cs"
    if legacy_driver.exists():
        editor_driver = project / "Assets" / "Editor" / legacy_driver.name
        editor_driver.parent.mkdir(exist_ok=True)
        if editor_driver.exists():
            raise ValueError(f"Duplicate import driver: {editor_driver}")
        legacy_driver.rename(editor_driver)
        legacy_meta = legacy_driver.with_suffix(".cs.meta")
        if legacy_meta.exists():
            legacy_meta.rename(editor_driver.with_suffix(".cs.meta"))
    target = project / "Assets" / "T13PlayerSmoke"
    (target / "Editor").mkdir(parents=True, exist_ok=True)
    hashes: dict[str, str] = {}
    for name, relative in [("PlayerProbe.cs.txt", "PlayerProbe.cs"), ("BuildProbe.cs.txt", "Editor/BuildProbe.cs")]:
        source = FIXTURES / name
        destination = target / relative
        if destination.exists() and destination.read_bytes() != source.read_bytes():
            if previous is None or previous.get("project") != str(project) or previous.get("fixtureHashes", {}).get(name) != sha256(destination):
                raise ValueError(f"Existing fixture differs from recorded owned bytes: {destination}")
        shutil.copyfile(source, destination)
        hashes[name] = sha256(source)
    for relative, assembly in [
        ("PlayerSmoke.asmdef", {"name": "DxCommandTerminal.T13.PlayerSmoke", "references": ["WallstopStudios.DxCommandTerminal"]}),
        ("Editor/PlayerSmoke.Editor.asmdef", {"name": "DxCommandTerminal.T13.PlayerSmoke.Editor", "includePlatforms": ["Editor"], "references": ["DxCommandTerminal.T13.PlayerSmoke"]}),
    ]:
        (target / relative).write_text(json.dumps(assembly), encoding="utf-8")
    return hashes


def stop_owned_process(process: subprocess.Popen[Any]) -> None:
    try:
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], check=False, capture_output=True, timeout=30)
    except (subprocess.TimeoutExpired, OSError):
        pass
    if process.poll() is None:
        process.kill()
    process.wait(timeout=30)


def run_leg(leg: dict[str, Any], artifact: Path, output: Path, timeout: int, previous: dict[str, Any] | None = None) -> dict[str, Any]:
    profile = leg["inputProfile"]
    project = Path(leg["project"])
    unity = Path(leg["unity"])
    version = leg["editorVersion"]
    output.mkdir(parents=True, exist_ok=False)
    evidence: dict[str, Any] = {"passed": False, "inputProfile": profile, "project": str(project), "editor": str(unity), "editorVersion": version, "artifactSha256": sha256(artifact), "harnessRevision": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(), "harnessSha256": HARNESS_SHA256, "backend": "Mono", "stripping": "Low", "development": False}
    try:
        if leg.get("failed") is not False or leg.get("complete") is not True or leg.get("artifactHashMatches") is not True:
            raise ValueError("Prior clean import leg did not pass")
        if evidence["artifactSha256"] != leg["artifactSha256"]:
            raise ValueError("Artifact hash differs from verified import")
        actual_version = subprocess.check_output([str(unity), "-version"], timeout=30, text=True).strip()
        if actual_version != version:
            raise ValueError(f"Editor version differs: {actual_version} != {version}")
        validate_profile(project, profile)
        source_revision = leg["git"]["revision"]
        if leg["git"].get("dirty") is not False or not re.fullmatch(r"[0-9a-f]{40}", source_revision):
            raise ValueError("Verified import has no clean exact source revision")
        evidence["packageSourceRevision"] = source_revision
        evidence["payloadHashes"] = validate_payload(artifact, project, source_revision)
        evidence["fixtureHashes"] = install_fixtures(project, previous)
        env = dict(os.environ, DX_T13_PLAYER_OUT=str(output), DX_T13_INPUT_PROFILE=profile)
        command = [str(unity), "-batchmode", "-projectPath", str(project), "-executeMethod", "DxCommandTerminal.T13.PlayerSmoke.Editor.BuildProbe.Run", "-logFile", str(output / "build.log")]
        process = subprocess.Popen(command, env=env)
        evidence["buildPid"] = process.pid
        try:
            evidence["buildExit"] = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            stop_owned_process(process)
            raise TimeoutError("Owned player drill exceeded deadline")
        if evidence["buildExit"] != 0:
            raise ValueError(f"Build/run process exited {evidence['buildExit']}")
        for name in ["build.log", "player.log"]:
            log = (output / name).read_text(encoding="utf-8-sig", errors="replace")
            matches = ERRORS.findall(log)
            if matches:
                raise ValueError(f"{name} contains errors: {matches[:10]}")
        if (output / "player-exit.txt").read_text().strip() != "0":
            raise ValueError("Actual player exit is not zero")
        evidence["playerReport"] = checked_player_report(output / "player-report.json", profile, version)
        validate_profile(project, profile)
        evidence["passed"] = True
    except Exception as exception:
        evidence["failure"] = str(exception)
    (output / "manifest.json").write_text(json.dumps(evidence, indent=2) + "\n", encoding="utf-8")
    return evidence


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--matrix-report", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True, help="Fresh short Windows output root")
    parser.add_argument("--editor-id", default="unity-2021")
    parser.add_argument("--previous-player-report", type=Path, help="Prior owned player matrix for verified fixture updates")
    parser.add_argument("--timeout", type=int, default=1200)
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("This Windows standalone drill must run on its Windows host")
    if args.out.exists() or args.timeout < 60:
        parser.error("Output must be fresh and timeout must be at least 60 seconds")
    matrix = json.loads(args.matrix_report.read_text(encoding="utf-8-sig"))
    legs: list[dict[str, Any]] = []
    for profile in PROFILES:
        editor_id = args.editor_id if profile == "legacy" else f"{args.editor_id}-{profile}"
        matches = [leg for leg in matrix["legs"] if leg["editor"] == editor_id and leg["inputProfile"] == profile]
        if len(matches) != 1:
            parser.error(f"Need one verified import leg for {editor_id}/{profile}")
        legs.append(matches[0])
    args.out.mkdir(parents=True)
    (args.out / "harness-source.py").write_bytes(HARNESS_SOURCE)
    prior: dict[str, dict[str, Any]] = {}
    if args.previous_player_report is not None:
        previous_matrix = json.loads(args.previous_player_report.read_text(encoding="utf-8-sig"))
        shutil.copyfile(args.previous_player_report, args.out / "previous-matrix-manifest.json")
        for previous in previous_matrix["legs"]:
            profile = previous["inputProfile"]
            if profile in prior:
                parser.error(f"Duplicate prior owned profile: {profile}")
            prior[profile] = previous
    reports = [run_leg(leg, Path(leg["artifact"]), args.out / leg["inputProfile"], args.timeout, prior.get(leg["inputProfile"])) for leg in legs]
    passed = all(report["passed"] for report in reports)
    (args.out / "matrix-manifest.json").write_text(json.dumps({"passed": passed, "coverage": ["release-Mono-generated-private-manual-dispatch", "consumer-generator-loading", "compiled-input-profiles", "argument-parsing-and-arity"], "legs": reports}, indent=2) + "\n", encoding="utf-8")
    print(f"Mono player profiles: {sum(report['passed'] for report in reports)}/3 passed")
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
