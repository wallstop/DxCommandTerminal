"""Reject incomplete or stale player timing evidence and pin the p95 gate."""
from __future__ import annotations

import copy
import json
import io
import subprocess
import tarfile
from unittest.mock import patch
from pathlib import Path
import sys
import tempfile
import unittest
from typing import Any

sys.path.insert(0, str(Path(__file__).parents[1] / "t13"))
import player_readiness as readiness
import player_smoke as smoke


class ReadinessTests(unittest.TestCase):
    def report(self, warm: int = 1000) -> dict[str, Any]:
        return dict(passed=True, token="owner", pid=42, unityVersion="2021.3.45f1", platform="WindowsPlayer",
                    batchMode=False, developmentBuild=False, monoBackend=True, il2cppBackend=False,
                    inputSystem=False, legacyInput=True, catalogEntries=1000, workloadCommands=1000, totalCommands=1000,
                    handlerCalls=1002, frequency=10000000, coldTicks=10000, phaseTicks=[1000, 1000, 8000], warmTicks=[10000] * warm,
                    clockTicks=[1] * 1000, processor="processor", processorCount=8, os="Windows", graphics="Direct3D11", logs=[])

    def test_exact_player_report_and_sample_windows_are_required(self) -> None:
        good = self.report()
        with tempfile.TemporaryDirectory() as temporary:
            file = Path(temporary) / "report.json"
            file.write_text(json.dumps(good))
            self.assertEqual(good, readiness.checked_report(file, "legacy", "2021.3.45f1", "owner", 42, 1000))
            for name, value in [("passed", 1), ("token", "stale"), ("pid", 43), ("unityVersion", "2022.3.62f1"),
                                ("batchMode", True), ("graphics", "Null"), ("monoBackend", False), ("inputSystem", True),
                                ("catalogEntries", 999), ("workloadCommands", 999), ("handlerCalls", 1001),
                                ("frequency", 0), ("coldTicks", True), ("phaseTicks", [1, 2, 3]), ("warmTicks", [10000] * 999),
                                ("warmTicks", [False] * 1000), ("warmTicks", [-1] * 1000), ("clockTicks", []),
                                ("logs", ["[Warning] fallback"]), ("logs", None), ("failure", "dispatch failed")]:
                with self.subTest(name=name, value=str(value)[:60]):
                    file.write_text(json.dumps(dict(good, **{name: value})))
                    with self.assertRaises(ValueError):
                        readiness.checked_report(file, "legacy", "2021.3.45f1", "owner", 42, 1000)

    def series(self) -> list[dict[str, Any]]:
        reports = [self.report(1000 if index == 0 else 0) for index in range(30)]
        for index, report in enumerate(reports):
            report["token"] = str(index)
        return reports

    def test_empty_control_never_approves_the_readiness_gate(self) -> None:
        reports = self.series()
        for report in reports:
            report.update(catalogEntries=0, workloadCommands=0, totalCommands=0, handlerCalls=0)
        with tempfile.TemporaryDirectory() as temporary:
            file = Path(temporary) / "report.json"
            file.write_text(json.dumps(reports[0]))
            readiness.checked_report(file, "legacy", "2021.3.45f1", "0", 42, 1000, 0)
        self.assertFalse(readiness.summarize(reports, 0)["passed"])

    def test_raw_windows_and_statistics_are_separate(self) -> None:
        reports = self.series()
        reports[-1]["coldTicks"] = 40000
        result = readiness.summarize(reports)
        self.assertTrue(result["passed"])
        self.assertEqual(result["cold"], {"n": 30, "median": 1.0, "p95": 1.0, "max": 4.0})
        self.assertEqual(result["warm"]["n"], 1000)
        self.assertEqual(result["clock"]["n"], 30000)
        self.assertEqual(result["coldMilliseconds"][-1], 4.0)
        self.assertEqual(result["budgetMilliseconds"], 5.0)

    def test_both_p95_gates_are_strict_and_keep_outliers(self) -> None:
        for window in ("coldTicks", "warmTicks"):
            for ticks, passed in ((49999, True), (50000, False), (60000, False)):
                with self.subTest(window=window, ticks=ticks):
                    reports = self.series()
                    if window == "coldTicks":
                        for report in reports[-2:]:
                            report[window] = ticks
                    else:
                        reports[0][window][-51:] = [ticks] * 51
                    self.assertEqual(readiness.summarize(reports)["passed"], passed)

    def test_incomplete_replayed_or_mixed_series_cannot_pass(self) -> None:
        good = self.series()
        changed = copy.deepcopy(good)
        changed[-1]["graphics"] = "Vulkan"
        repeated = copy.deepcopy(good)
        repeated[-1]["token"] = repeated[0]["token"]
        no_warm = copy.deepcopy(good)
        no_warm[0]["warmTicks"] = []
        for reports in (good[:29], good + [good[0]], changed, repeated, no_warm):
            with self.subTest(reports=len(reports)):
                with self.assertRaises(ValueError):
                    readiness.summarize(reports)
        for values in ([], [float("nan")], [float("inf")], [-1.0]):
            with self.assertRaises(ValueError):
                readiness.distribution(values)

    def test_payload_uses_the_verified_package_commit_after_harness_head_moves(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source, project = root / "source", root / "project"
            source.mkdir()
            artifact = root / "artifact.unitypackage"
            assets = {"package.json": b'{"version":"1.0.0"}', "Runtime/Analyzers/Generator.dll": b"generator"}
            with tarfile.open(artifact, "w:gz") as archive:
                for index, (relative, data) in enumerate(assets.items()):
                    meta = f"fileFormatVersion: 2\nguid: {index:032x}\n".encode()
                    for base in (source, project / "Packages/com.wallstop-studios.dxcommandterminal"):
                        file = base / relative
                        file.parent.mkdir(parents=True, exist_ok=True)
                        file.write_bytes(data)
                        file.with_name(file.name + ".meta").write_bytes(meta)
                    for suffix, value in [("pathname", ("Packages/com.wallstop-studios.dxcommandterminal/" + relative).encode()), ("asset", data), ("asset.meta", meta)]:
                        member = tarfile.TarInfo(f"{index:032x}/{suffix}")
                        member.size = len(value)
                        archive.addfile(member, io.BytesIO(value))
            def git(*arguments: str) -> str:
                return subprocess.check_output(["git", *arguments], cwd=source, text=True, stderr=subprocess.PIPE).strip()
            git("init", "-q")
            git("config", "core.autocrlf", "true")
            (source / ".gitattributes").write_bytes(b"* -text\n")
            git("add", ".")
            git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-qm", "Package")
            original = git("rev-parse", "HEAD")
            (source / "package.json").write_bytes(b'{"version":"2.0.0"}')
            git("add", ".")
            git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "commit", "-qm", "Later tree")
            self.assertNotEqual(original, git("rev-parse", "HEAD"))
            with patch.object(readiness.smoke, "ROOT", source):
                revision, hashes = readiness.verified_payload({"git": {"dirty": False, "revision": original}}, artifact, project)
                self.assertEqual(revision, original)
                self.assertTrue(hashes)
                for metadata in ({}, {"dirty": False}, {"dirty": False, "revision": "HEAD"}, {"dirty": True, "revision": original}):
                    with self.subTest(metadata=metadata), self.assertRaises(ValueError):
                        readiness.verified_payload({"git": metadata}, artifact, project)

    def test_fixture_mode_changes_require_full_ownership_and_can_restore_smoke(self) -> None:
        with tempfile.TemporaryDirectory() as temporary:
            project = Path(temporary)
            old = smoke.install_fixtures(project)
            prior = {"project": str(project), "fixtureHashes": old}
            measured = smoke.install_fixtures(project, prior, readiness=True)
            workload = project / "Assets/T13PlayerSmoke/ReadinessCommands.cs"
            self.assertEqual(workload.read_bytes(), smoke.readiness_commands(1000))
            with self.assertRaises(ValueError):
                smoke.install_fixtures(project, prior)
            restored = smoke.install_fixtures(project, {"project": str(project), "fixtureHashes": measured})
            self.assertEqual(old, restored)
            self.assertEqual(workload.read_bytes(), smoke.readiness_commands(0))
            before = workload.read_bytes()
            with self.assertRaises(ValueError):
                smoke.install_fixtures(project, readiness=True, target_name="WebGL")
            self.assertEqual(before, workload.read_bytes())


if __name__ == "__main__":
    unittest.main()
