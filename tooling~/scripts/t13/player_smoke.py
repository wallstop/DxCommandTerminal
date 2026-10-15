"""Run release Mono or IL2CPP players from verified, retained clean artifact imports."""
from __future__ import annotations

import argparse
import hashlib
import gzip
import io
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
from typing import Any

import webgl_player
from webgl_player import run_browser

ROOT = Path(__file__).resolve().parents[3]
HARNESS_SOURCE = Path(__file__).read_bytes()
HARNESS_SHA256 = hashlib.sha256(HARNESS_SOURCE).hexdigest()
FIXTURES = ROOT / "tooling~" / "compat" / "player-smoke"
PROFILES = {"legacy": 0, "input-system": 1, "both": 2}
ERRORS = re.compile(r"(?:warning|error) [A-Za-z]+\d+|Aborting batchmode|Scripts have compiler errors|Exception:|falling back to .*discovery|Failed to (?:bind command|bind it|probe for a generated command catalog)")


def sha256(file: Path) -> str:
    return hashlib.sha256(file.read_bytes()).hexdigest()


def parse_editor_version(stdout: str) -> str:
    for line in stdout.split("\n"):
        # JavaScript trim includes BOM, but excludes Python's record separators and NEL.
        candidate = re.sub(r"^(?:[^\S\x1c-\x1f\x85]|\ufeff)+|(?:[^\S\x1c-\x1f\x85]|\ufeff)+$", "", line)
        if re.fullmatch(r"[0-9]{4,}\.[0-9][A-Za-z0-9_.-]*", candidate):
            return candidate
    raise ValueError(f"Editor probe printed no plausible version: {stdout.strip()[:200]}")


def select_profile_legs(matrix: dict[str, Any], editor_id: str) -> list[dict[str, Any]]:
    anchors = [leg for leg in matrix["legs"] if leg["editor"] == editor_id and leg["inputProfile"] == "legacy"]
    if len(anchors) != 1:
        raise ValueError(f"Need one legacy anchor for {editor_id}")
    anchor = anchors[0]
    dimensions = ("requestedFamily", "editorVersion", "domainReloadEnabled")
    if (not isinstance(anchor.get("requestedFamily"), str) or not anchor["requestedFamily"]
            or not isinstance(anchor.get("editorVersion"), str) or not anchor["editorVersion"]
            or type(anchor.get("domainReloadEnabled")) is not bool):
        raise ValueError("Legacy anchor lacks family, version, or domain reload evidence")
    legs: list[dict[str, Any]] = []
    for profile in PROFILES:
        matches = [leg for leg in matrix["legs"] if leg["inputProfile"] == profile
                   and all(type(leg.get(field)) is type(anchor[field]) and leg.get(field) == anchor[field]
                           for field in dimensions)]
        if len(matches) != 1:
            raise ValueError(f"Need one matching import leg for {editor_id}/{profile}")
        legs.append(matches[0])
    return legs


def metadata_matches(relative: str, expected: bytes, actual: bytes) -> bool:
    if actual == expected:
        return True
    # Older editors expand GUID-only script metas with these measured defaults.
    header = expected.rstrip(b"\r\n")
    if not relative.endswith(".cs.meta") or re.fullmatch(rb"fileFormatVersion: 2\r?\nguid: [0-9a-fA-F]{32}", header) is None:
        return False
    defaults = (b"\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n"
                b"  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n"
                b"  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    return actual.replace(b"\r\n", b"\n") == header.replace(b"\r\n", b"\n") + defaults


def validate_payload(artifact: Path, project: Path, source_revision: str | None = None, normalized_metadata: dict[str, str] | None = None) -> dict[str, str]:
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
    def verify_bytes(relative: str, payload: bytes) -> None:
        digest = hashlib.sha256(payload).hexdigest()
        source_bytes = source_files.get(relative) if source_files is not None else (ROOT / relative).read_bytes()
        if source_bytes is None or hashlib.sha256(source_bytes).hexdigest() != digest:
            raise ValueError(f"Artifact bytes differ from committed package source: {relative}")
        imported = project / "Packages" / "com.wallstop-studios.dxcommandterminal" / relative
        if not imported.is_file():
            raise ValueError(f"Artifact bytes differ from source/import: {imported}")
        actual = imported.read_bytes()
        matches = metadata_matches(relative, payload, actual) if relative.endswith(".meta") else actual == payload
        if not matches:
            raise ValueError(f"Artifact bytes differ from source/import: {imported}")
        if normalized_metadata is not None and actual != payload:
            normalized_metadata[relative] = hashlib.sha256(actual).hexdigest()
        hashes[relative] = digest

    # Random member reads otherwise decompress the gzip stream for every backward seek.
    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(artifact.read_bytes())), mode="r:") as archive:
        entries = archive.getmembers()
        members = {member.name: member for member in entries}
        if len(members) != len(entries):
            raise ValueError("Duplicate archive member")
        pathnames: set[str] = set()
        for name, member in members.items():
            if not name.endswith("/pathname"):
                continue
            if not member.isfile():
                raise ValueError(f"Non-file pathname: {name}")
            pathname_file = archive.extractfile(member)
            if pathname_file is None:
                raise ValueError(f"Unreadable pathname: {name}")
            pathname = pathname_file.read().decode("utf-8")
            prefix = "Packages/com.wallstop-studios.dxcommandterminal/"
            if not pathname.startswith(prefix):
                raise ValueError(f"Unexpected artifact pathname: {pathname}")
            relative = pathname[len(prefix):]
            parts = relative.split("/")
            if any(part in ("", ".", "..") for part in parts) or "\\" in relative or ":" in relative:
                raise ValueError(f"Unsafe pathname: {pathname}")
            # Windows imports cannot distinguish paths that differ only by case.
            identity = relative.casefold()
            if identity in pathnames:
                raise ValueError(f"Duplicate artifact pathname: {pathname}")
            pathnames.add(identity)
            base = name.removesuffix("pathname")
            meta = members.get(base + "asset.meta")
            if meta is None or not meta.isfile():
                raise ValueError(f"Missing or non-file metadata: {pathname}")
            metadata = archive.extractfile(meta)
            if metadata is None:
                raise ValueError(f"Unreadable metadata: {pathname}")
            meta_bytes = metadata.read()
            verify_bytes(relative + ".meta", meta_bytes)
            asset = members.get(base + "asset")
            folder = re.search(rb"^folderAsset:[ \t]*yes[ \t]*\r?$", meta_bytes, re.MULTILINE) is not None
            if folder:
                if asset is not None or not (project / pathname).is_dir():
                    raise ValueError(f"Invalid imported folder: {pathname}")
                continue
            if asset is None or not asset.isfile():
                raise ValueError(f"Missing or non-file asset: {pathname}")
            payload = archive.extractfile(asset)
            if payload is None:
                raise ValueError(f"Unreadable asset: {pathname}")
            verify_bytes(relative, payload.read())
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


def checked_player_report(file: Path, profile: str, version: str, backend: str = "Mono", target: str = "StandaloneWindows64") -> dict[str, Any]:
    report = json.loads(file.read_text(encoding="utf-8-sig"))
    expected = {"passed": True, "unityVersion": version, "platform": "WebGLPlayer" if target == "WebGL" else "WindowsPlayer", "developmentBuild": False, "monoBackend": backend == "Mono", "il2cppBackend": backend == "IL2CPP", "inputSystem": profile != "legacy", "legacyInput": profile != "input-system", "catalogPresent": True, "catalogEntries": 2, "catalogBindersExecuted": 2, "generatedCalls": 1, "privateCalls": 1, "manualCalls": 1}
    for name, value in expected.items():
        actual = report.get(name)
        if type(actual) is not type(value) or actual != value:
            raise ValueError(f"Player verdict mismatch: {name}={actual!r}, expected {value!r}")
    if target == "WebGL":
        logs = report.get("logs")
        if not isinstance(logs, list) or not all(isinstance(line, str) for line in logs):
            raise ValueError("WebGL report has no runtime log evidence")
        if any(ERRORS.search(line) or re.match(r"^\[(?:Warning|Error|Assert|Exception)\]", line) for line in logs):
            raise ValueError("WebGL runtime logs contain command errors or discovery fallback")
        if not isinstance(report.get("browserUserAgent"), str) or not report["browserUserAgent"]:
            raise ValueError("WebGL report has no browser identity")
    if report.get("failure") not in (None, ""):
        raise ValueError(f"Player reported failure: {report['failure']}")
    return report


def checked_build_settings(output: Path, backend: str, stripping: str) -> str:
    settings = (output / "build-settings.txt").read_text(encoding="utf-8-sig")
    if settings != f"{backend}/{stripping}":
        raise ValueError(f"Actual build settings differ: {settings}")
    return settings


def install_fixtures(project: Path, previous: dict[str, Any] | None = None, profile: str = "legacy", target_name: str = "StandaloneWindows64") -> dict[str, str]:
    target = project / "Assets" / "T13PlayerSmoke"
    files: dict[str, tuple[str, bytes]] = {}
    for name, relative in [("PlayerProbe.cs.txt", "PlayerProbe.cs"), ("BuildProbe.cs.txt", "Editor/BuildProbe.cs")]:
        files[name] = (relative, (FIXTURES / name).read_bytes())
    for relative, assembly in [
        ("PlayerSmoke.asmdef", {"name": "DxCommandTerminal.T13.PlayerSmoke", "references": ["WallstopStudios.DxCommandTerminal"]}),
        ("Editor/PlayerSmoke.Editor.asmdef", {"name": "DxCommandTerminal.T13.PlayerSmoke.Editor", "includePlatforms": ["Editor"], "references": ["DxCommandTerminal.T13.PlayerSmoke"]}),
    ]:
        files[relative] = (relative, json.dumps(assembly).encode("utf-8"))
    if target_name == "WebGL":
        if profile not in PROFILES:
            raise ValueError("Invalid expected WebGL input profile")
        data = ('namespace DxCommandTerminal.T13.PlayerSmoke { internal static class ExpectedProfile { '
                'internal const string InputProfile = "' + profile + '"; } }').encode("utf-8")
        files["ExpectedProfile.cs"] = ("ExpectedProfile.cs", data)
        files["Report.jslib.txt"] = ("Report.jslib", (FIXTURES / "Report.jslib.txt").read_bytes())
    # Refuse every unowned edit before replacing any fixture.
    for key, (relative, contents) in files.items():
        destination = target / relative
        if destination.exists() and destination.read_bytes() != contents:
            if previous is None or previous.get("project") != str(project) or previous.get("fixtureHashes", {}).get(key) != sha256(destination):
                raise ValueError(f"Existing fixture differs from recorded owned bytes: {destination}")
    legacy_driver = project / "Assets" / "DxTerminalImportDrill.cs"
    if legacy_driver.exists():
        editor_driver = project / "Assets" / "Editor" / legacy_driver.name
        if editor_driver.exists():
            raise ValueError(f"Duplicate import driver: {editor_driver}")
        editor_driver.parent.mkdir(exist_ok=True)
        legacy_driver.rename(editor_driver)
        legacy_meta = legacy_driver.with_suffix(".cs.meta")
        if legacy_meta.exists():
            legacy_meta.rename(editor_driver.with_suffix(".cs.meta"))
    hashes: dict[str, str] = {}
    for key, (relative, contents) in files.items():
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        if not destination.exists() or destination.read_bytes() != contents:
            destination.write_bytes(contents)
        hashes[key] = sha256(destination)
    return hashes


def stop_owned_process(process: subprocess.Popen[Any]) -> None:
    try:
        subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"], check=False, capture_output=True, timeout=30)
    except (subprocess.TimeoutExpired, OSError):
        pass
    if process.poll() is None:
        process.kill()
    process.wait(timeout=30)


def run_leg(leg: dict[str, Any], artifact: Path, output: Path, timeout: int, previous: dict[str, Any] | None = None, backend: str = "Mono", stripping: str = "Low", target: str = "StandaloneWindows64", browser: Path | None = None) -> dict[str, Any]:
    profile = leg["inputProfile"]
    project = Path(leg["project"])
    unity = Path(leg["unity"])
    version = leg["editorVersion"]
    output.mkdir(parents=True, exist_ok=False)
    evidence: dict[str, Any] = {"passed": False, "inputProfile": profile, "project": str(project), "editor": str(unity), "editorVersion": version, "artifactSha256": sha256(artifact), "harnessRevision": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(), "harnessSha256": HARNESS_SHA256, "backend": backend, "stripping": stripping, "development": False, "target": target, "browserHarnessSha256": webgl_player.HARNESS_SHA256}
    try:
        if leg.get("failed") is not False or leg.get("complete") is not True or leg.get("artifactHashMatches") is not True:
            raise ValueError("Prior clean import leg did not pass")
        if evidence["artifactSha256"] != leg["artifactSha256"]:
            raise ValueError("Artifact hash differs from verified import")
        evidence["editorVersionProbeOutput"] = subprocess.check_output([str(unity), "-version"], timeout=30, encoding="utf-8", errors="replace")
        actual_version = parse_editor_version(evidence["editorVersionProbeOutput"])
        if actual_version != version:
            raise ValueError(f"Editor version differs: {actual_version} != {version}")
        validate_profile(project, profile)
        source_revision = leg["git"]["revision"]
        if leg["git"].get("dirty") is not False or not re.fullmatch(r"[0-9a-f]{40}", source_revision):
            raise ValueError("Verified import has no clean exact source revision")
        evidence["packageSourceRevision"] = source_revision
        normalized_metadata: dict[str, str] = {}
        evidence["normalizedMetadataHashes"] = normalized_metadata
        evidence["payloadHashes"] = validate_payload(artifact, project, source_revision, normalized_metadata)
        evidence["fixtureHashes"] = install_fixtures(project, previous, profile, target)
        env = dict(os.environ, DX_T13_PLAYER_OUT=str(output), DX_T13_INPUT_PROFILE=profile, DX_T13_BACKEND=backend, DX_T13_STRIPPING=stripping, DX_T13_TARGET=target)
        command = [str(unity), "-batchmode", "-projectPath", str(project), "-buildTarget", "WebGL" if target == "WebGL" else "Win64", "-executeMethod", "DxCommandTerminal.T13.PlayerSmoke.Editor.BuildProbe.Run", "-logFile", str(output / "build.log")]
        process = subprocess.Popen(command, env=env)
        evidence["buildPid"] = process.pid
        try:
            evidence["buildExit"] = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            stop_owned_process(process)
            raise TimeoutError("Owned player drill exceeded deadline")
        if evidence["buildExit"] != 0:
            raise ValueError(f"Build/run process exited {evidence['buildExit']}")
        evidence["buildSettings"] = checked_build_settings(output, backend, stripping)
        actual_target = (output / "build-target.txt").read_text(encoding="utf-8-sig")
        if actual_target != target:
            raise ValueError(f"Build target differs: {actual_target}")
        if target == "WebGL":
            if browser is None or not browser.is_file():
                raise ValueError("WebGL requires an existing browser executable")
            evidence["browserSha256"] = sha256(browser)
            evidence["browserRun"] = run_browser(browser, output, min(timeout, 180), stop_owned_process)
            binaries = [str(file.relative_to(output)).replace("\\", "/") for file in (output / "Player").rglob("*") if file.is_file()]
            if not any(name.endswith(".wasm") for name in binaries):
                raise ValueError("WebGL build has no WebAssembly payload")
        else:
            binaries = ["Player/Smoke.exe"]
            if backend == "IL2CPP":
                binaries.append("Player/GameAssembly.dll")
        evidence["playerBinaryHashes"] = {name: sha256(output / name) for name in binaries}
        for name in (["build.log"] if target == "WebGL" else ["build.log", "player.log"]):
            log = (output / name).read_text(encoding="utf-8-sig", errors="replace")
            matches = ERRORS.findall(log)
            if matches:
                raise ValueError(f"{name} contains errors: {matches[:10]}")
        if target != "WebGL" and (output / "player-exit.txt").read_text().strip() != "0":
            raise ValueError("Actual player exit is not zero")
        evidence["playerReport"] = checked_player_report(output / "player-report.json", profile, version, backend, target)
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
    parser.add_argument("--backend", choices=("Mono", "IL2CPP"), default="Mono")
    parser.add_argument("--target", choices=("StandaloneWindows64", "WebGL"), default="StandaloneWindows64")
    parser.add_argument("--browser", type=Path, help="Chromium browser executable for WebGL")
    parser.add_argument("--stripping", choices=("Low", "Medium", "High"), default="Low")
    parser.add_argument("--timeout", type=int, default=1200)
    args = parser.parse_args()
    if os.name != "nt":
        parser.error("This player drill must run on its Windows host")
    if args.target == "WebGL" and (args.backend != "IL2CPP" or args.browser is None or not args.browser.is_file()):
        parser.error("WebGL requires IL2CPP and an existing --browser executable")
    if args.out.exists() or args.timeout < 60:
        parser.error("Output must be fresh and timeout must be at least 60 seconds")
    matrix = json.loads(args.matrix_report.read_text(encoding="utf-8-sig"))
    try:
        legs = select_profile_legs(matrix, args.editor_id)
    except ValueError as exception:
        parser.error(str(exception))
    args.out.mkdir(parents=True)
    (args.out / "harness-source.py").write_bytes(HARNESS_SOURCE)
    (args.out / "browser-harness-source.py").write_bytes(webgl_player.HARNESS_SOURCE)
    prior: dict[str, dict[str, Any]] = {}
    if args.previous_player_report is not None:
        previous_matrix = json.loads(args.previous_player_report.read_text(encoding="utf-8-sig"))
        shutil.copyfile(args.previous_player_report, args.out / "previous-matrix-manifest.json")
        for previous in previous_matrix["legs"]:
            profile = previous["inputProfile"]
            if profile in prior:
                parser.error(f"Duplicate prior owned profile: {profile}")
            prior[profile] = previous
    reports = [run_leg(leg, Path(leg["artifact"]), args.out / leg["inputProfile"], args.timeout, prior.get(leg["inputProfile"]), args.backend, args.stripping, args.target, args.browser) for leg in legs]
    passed = all(report["passed"] for report in reports)
    (args.out / "matrix-manifest.json").write_text(json.dumps({"passed": passed, "coverage": [f"release-{args.target}-{args.backend}-{args.stripping}-generated-private-manual-dispatch", "consumer-generator-loading", "compiled-input-profiles", "argument-parsing-and-arity"], "legs": reports}, indent=2) + "\n", encoding="utf-8")
    print(f"{args.backend}/{args.stripping} player profiles: {sum(report['passed'] for report in reports)}/3 passed")
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
