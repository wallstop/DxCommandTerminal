"""Measure first readiness in fresh regular Mono players and warmed shell readiness."""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
import re
import statistics
import subprocess
from typing import Any
from uuid import uuid4

import player_smoke as smoke

SOURCE = Path(__file__).read_bytes()
SOURCE_HASH = smoke.sha256(Path(__file__))
SAMPLES = 30
WARM_SAMPLES = 1000
BUDGET_MS = 5.0


def distribution(values: list[float]) -> dict[str, float | int]:
    if not values or any(not math.isfinite(value) or value < 0 for value in values):
        raise ValueError("Expected finite nonnegative timings")
    ordered = sorted(values)
    return {"n": len(values), "median": statistics.median(values), "p95": ordered[math.ceil(0.95 * len(values)) - 1], "max": ordered[-1]}


def checked_report(file: Path, profile: str, version: str, token: str, pid: int, warm: int, command_count: int = 1000) -> dict[str, Any]:
    report: dict[str, Any] = json.loads(file.read_text(encoding="utf-8-sig"))
    expected = {"passed": True, "token": token, "pid": pid, "unityVersion": version, "platform": "WindowsPlayer",
                "batchMode": False, "developmentBuild": False, "monoBackend": True, "il2cppBackend": False,
                "inputSystem": profile != "legacy", "legacyInput": profile != "input-system",
                "catalogEntries": command_count, "workloadCommands": command_count, "handlerCalls": command_count + 2 if command_count else 0}
    for name, value in expected.items():
        if type(report.get(name)) is not type(value) or report[name] != value:
            raise ValueError(f"Readiness report differs: {name}={report.get(name)!r}, expected {value!r}")
    for name in ("frequency", "coldTicks", "processorCount"):
        if type(report.get(name)) is not int or not 0 < report[name] < 2**63:
            raise ValueError(f"Invalid positive integer: {name}")
    if type(report.get("totalCommands")) is not int or report["totalCommands"] < command_count:
        raise ValueError("Workload is not registered")
    for name, count in (("warmTicks", warm), ("clockTicks", 1000), ("phaseTicks", 3)):
        values = report.get(name)
        minimum = 1 if name == "warmTicks" else 0
        if not isinstance(values, list) or len(values) != count or any(type(value) is not int or not minimum <= value < 2**63 for value in values):
            raise ValueError(f"Invalid sample window: {name}")
    if sum(report["phaseTicks"]) != report["coldTicks"]:
        raise ValueError("Readiness phases do not sum to the measured window")
    for name in ("processor", "os", "graphics"):
        if not isinstance(report.get(name), str) or not report[name] or report[name] == "Null":
            raise ValueError(f"Missing player environment: {name}")
    logs = report.get("logs")
    if not isinstance(logs, list) or any(not isinstance(line, str) or smoke.ERRORS.search(line) or re.match(r"^\[(?:Warning|Error|Assert|Exception)\]", line) for line in logs):
        raise ValueError("Runtime logs lack evidence or contain warnings/errors/fallback")
    if report.get("failure") not in (None, ""):
        raise ValueError(f"Player reported failure: {report['failure']}")
    return report


def summarize(reports: list[dict[str, Any]], command_count: int = 1000) -> dict[str, Any]:
    if len(reports) != SAMPLES or len({report["token"] for report in reports}) != SAMPLES:
        raise ValueError("Need 30 separately owned fresh-player reports")
    environment = ("unityVersion", "platform", "processor", "processorCount", "os", "graphics", "frequency", "totalCommands")
    for report in reports:
        if any(report[name] != reports[0][name] for name in environment):
            raise ValueError("Player environments differ within the timing series")
    cold = [report["coldTicks"] * 1000.0 / report["frequency"] for report in reports]
    warm = [value * 1000.0 / report["frequency"] for report in reports for value in report["warmTicks"]]
    clock = [value * 1000.0 / report["frequency"] for report in reports for value in report["clockTicks"]]
    if len(warm) != WARM_SAMPLES:
        raise ValueError("Need exactly 1000 warmed readiness samples")
    result: dict[str, Any] = {"coldMilliseconds": cold, "warmMilliseconds": warm, "clockMilliseconds": clock,
                              "cold": distribution(cold), "warm": distribution(warm), "clock": distribution(clock), "budgetMilliseconds": BUDGET_MS}
    result["phases"] = {name: distribution([report["phaseTicks"][index] * 1000.0 / report["frequency"] for report in reports]) for index, name in enumerate(("construct", "configure", "register"))}
    result["passed"] = command_count == 1000 and result["cold"]["p95"] < BUDGET_MS and result["warm"]["p95"] < BUDGET_MS
    return result


def verified_payload(leg: dict[str, Any], artifact: Path, project: Path) -> tuple[str, dict[str, str]]:
    metadata = leg.get("git")
    if not isinstance(metadata, dict) or metadata.get("dirty") is not False:
        raise ValueError("Verified import has no clean package source revision")
    revision = metadata.get("revision")
    if not isinstance(revision, str) or re.fullmatch(r"[0-9a-f]{40}", revision) is None:
        raise ValueError("Verified import has no exact package source revision")
    return revision, smoke.validate_payload(artifact, project, revision)


def run_leg(leg: dict[str, Any], output: Path, previous: dict[str, Any] | None, timeout: int, command_count: int = 1000) -> dict[str, Any]:
    output.mkdir(parents=True, exist_ok=False)
    project, unity, artifact = (Path(leg[name]) for name in ("project", "unity", "artifact"))
    profile, version = leg["inputProfile"], leg["editorVersion"]
    revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=smoke.ROOT, text=True).strip()
    record: dict[str, Any] = {"passed": False, "project": str(project), "inputProfile": profile, "editorVersion": version,
                              "harnessRevision": revision, "harnessHashes": {"player_readiness.py": SOURCE_HASH, "player_smoke.py": smoke.HARNESS_SHA256},
                              "backend": "Mono", "stripping": "Low", "target": "StandaloneWindows64", "commandCount": command_count, "reports": []}
    try:
        if leg.get("failed") is not False or leg.get("complete") is not True or leg.get("artifactHashMatches") is not True or leg["git"].get("dirty") is not False:
            raise ValueError("Prior clean import did not pass")
        record["artifactSha256"] = smoke.sha256(artifact)
        if record["artifactSha256"] != leg["artifactSha256"]:
            raise ValueError("Artifact differs from the verified import")
        record["editorVersionProbe"] = subprocess.check_output([str(unity), "-version"], timeout=30, text=True)
        if smoke.parse_editor_version(record["editorVersionProbe"]) != version:
            raise ValueError("Editor version differs")
        smoke.validate_profile(project, profile)
        record["packageSourceRevision"], record["payloadHashes"] = verified_payload(leg, artifact, project)
        record["fixtureHashes"] = smoke.install_fixtures(project, previous, profile, readiness=True, command_count=command_count)
        record["fixtureSources"] = {name: smoke.sha256(smoke.FIXTURES / name) for name in ("ReadinessProbe.cs.txt", "BuildProbe.cs.txt")}
        environment = dict(os.environ, DX_T13_PLAYER_OUT=str(output), DX_T13_INPUT_PROFILE=profile, DX_T13_BACKEND="Mono", DX_T13_STRIPPING="Low", DX_T13_TARGET="StandaloneWindows64", DX_T13_BUILD_ONLY="1")
        command = [str(unity), "-batchmode", "-projectPath", str(project), "-buildTarget", "Win64", "-executeMethod", "DxCommandTerminal.T13.PlayerSmoke.Editor.BuildProbe.Run", "-logFile", str(output / "build.log")]
        record["buildArguments"] = command
        with smoke.owned_process(subprocess.Popen(command, env=environment)) as build:
            record["buildPid"] = build.pid
            try:
                record["buildExit"] = build.wait(timeout=timeout)
            except subprocess.TimeoutExpired:
                raise TimeoutError("Owned readiness build exceeded deadline")
        if record["buildExit"] != 0:
            raise ValueError("Readiness build failed")
        smoke.checked_build_settings(output, "Mono", "Low")
        if (output / "build-target.txt").read_text(encoding="utf-8-sig") != "StandaloneWindows64":
            raise ValueError("Build target differs")
        result = (output / "build-result.txt").read_text(encoding="utf-8-sig")
        if not result.startswith("Succeeded errors=0 warnings=0 duration=") or smoke.ERRORS.search((output / "build.log").read_text(encoding="utf-8-sig", errors="replace")):
            raise ValueError("Build reports errors or warnings")
        record["playerBinaryHashes"] = {str(file.relative_to(output)): smoke.sha256(file) for file in (output / "Player").rglob("*") if file.is_file()}
        for index in range(SAMPLES):
            folder = output / f"run-{index:02d}"
            folder.mkdir()
            token = uuid4().hex
            warm = WARM_SAMPLES if index == 0 else 0
            env = dict(os.environ, DX_T13_PLAYER_REPORT=str(folder / "report.json"), DX_T13_INPUT_PROFILE=profile, DX_T13_RUN_TOKEN=token, DX_T13_WARM_SAMPLES=str(warm))
            arguments = [str(output / "Player/Smoke.exe"), "-screen-fullscreen", "0", "-screen-width", "96", "-screen-height", "96", "-logFile", str(folder / "player.log")]
            with smoke.owned_process(subprocess.Popen(arguments, env=env)) as player:
                owner = {"pid": player.pid, "token": token, "arguments": arguments, "warmSamples": warm}
                (folder / "owner.json").write_text(json.dumps(owner, indent=2))
                try:
                    owner["exit"] = player.wait(timeout=120)
                except subprocess.TimeoutExpired:
                    raise TimeoutError("Owned readiness player exceeded deadline")
            (folder / "owner.json").write_text(json.dumps(owner, indent=2))
            if owner["exit"] != 0:
                raise ValueError(f"Readiness player {index} exited {owner['exit']}")
            report = checked_report(folder / "report.json", profile, version, token, player.pid, warm, command_count)
            if smoke.ERRORS.search((folder / "player.log").read_text(encoding="utf-8-sig", errors="replace")):
                raise ValueError(f"Player log {index} has errors or fallback")
            record["reports"].append(report)
            (output / "progress.json").write_text(json.dumps({"completed": index + 1, "owner": owner}))
        for relative, digest in record["playerBinaryHashes"].items():
            if smoke.sha256(output / relative) != digest:
                raise ValueError("Player bytes changed during the measurement series")
        smoke.validate_profile(project, profile)
        record["measurements"] = summarize(record["reports"], command_count)
        record["executionPassed"] = True
        record["passed"] = record["measurements"]["passed"]
    except smoke.OwnedProcessCleanupError:
        raise
    except Exception as error:
        record["failure"] = str(error)
    (output / "manifest.json").write_text(json.dumps(record, indent=2))
    return record


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--matrix-report", required=True, type=Path)
    parser.add_argument("--previous-player-report", required=True, type=Path)
    parser.add_argument("--editor-id", default="unity-2021")
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--commands", type=int, choices=(0, 1000), default=1000)
    parser.add_argument("--timeout", default=1200, type=int)
    args = parser.parse_args()
    if os.name != "nt" or args.out.exists() or args.timeout < 60:
        parser.error("Run on Windows with a fresh short output root and timeout >= 60")
    matrix = json.loads(args.matrix_report.read_text(encoding="utf-8-sig"))
    legs = smoke.select_profile_legs(matrix, args.editor_id)
    previous = json.loads(args.previous_player_report.read_text(encoding="utf-8-sig"))
    prior = {leg["inputProfile"]: leg for leg in previous["legs"]}
    if len(prior) != len(previous["legs"]):
        parser.error("Duplicate prior fixture ownership profiles")
    args.out.mkdir(parents=True)
    (args.out / "readiness-source.py").write_bytes(SOURCE)
    (args.out / "smoke-source.py").write_bytes(smoke.HARNESS_SOURCE)
    results = [run_leg(leg, args.out / leg["inputProfile"], prior.get(leg["inputProfile"]), args.timeout, args.commands) for leg in legs]
    passed = all(result["passed"] for result in results)
    (args.out / "matrix-manifest.json").write_text(json.dumps({"passed": passed, "scope": "Regular Mono players; first shell readiness after Unity startup; no UI or allocation acceptance", "legs": results}, indent=2))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
