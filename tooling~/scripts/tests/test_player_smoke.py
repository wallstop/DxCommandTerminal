"""Fail-closed checks for real player evidence and retained artifact bytes."""
from __future__ import annotations

import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import subprocess
from unittest.mock import Mock, patch
import unittest
from typing import Any

SPEC = importlib.util.spec_from_file_location("player_smoke", Path(__file__).parents[1] / "t13" / "player_smoke.py")
assert SPEC is not None and SPEC.loader is not None
smoke = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(smoke)


class PlayerSmokeTests(unittest.TestCase):
    def test_stale_or_partial_player_verdict_is_rejected(self) -> None:
        good: dict[str, Any] = {"passed": True, "unityVersion": "2021.3.45f1", "platform": "WindowsPlayer", "developmentBuild": False, "monoBackend": True, "inputSystem": False, "legacyInput": True, "catalogPresent": True, "catalogEntries": 2, "catalogBindersExecuted": 2, "generatedCalls": 1, "privateCalls": 1, "manualCalls": 1}
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / "report.json"
            file.write_text(json.dumps(good), encoding="utf-8")
            smoke.checked_player_report(file, "legacy", "2021.3.45f1")
            for field, value in [("passed", 1), ("catalogPresent", False), ("developmentBuild", True), ("monoBackend", False), ("privateCalls", 0), ("unityVersion", "6000.4.6f1"), ("legacyInput", False), ("failure", "Failed dispatch")]:
                with self.subTest(field=field):
                    file.write_text(json.dumps(dict(good, **{field: value})), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        smoke.checked_player_report(file, "legacy", "2021.3.45f1")

    def test_corrupt_import_with_intact_guid_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "source"
            project = root / "project"
            artifact = root / "payload.unitypackage"
            assets = {"package.json": b'{"name":"com.wallstop-studios.dxcommandterminal"}', "Runtime/Analyzers/Generator.dll": b"actual-generator"}
            with tarfile.open(artifact, "w:gz") as archive:
                for index, (name, data) in enumerate(assets.items()):
                    for entry, content in [("pathname", f"Packages/com.wallstop-studios.dxcommandterminal/{name}".encode()), ("asset", data)]:
                        member = tarfile.TarInfo(f"{index:032x}/{entry}")
                        member.size = len(content)
                        archive.addfile(member, io.BytesIO(content))
                    for file in [source / name, project / "Packages/com.wallstop-studios.dxcommandterminal" / name]:
                        file.parent.mkdir(parents=True, exist_ok=True)
                        file.write_bytes(data)
            previous_root = smoke.ROOT
            setattr(smoke, "ROOT", source)
            try:
                smoke.validate_payload(artifact, project)
                imported = project / "Packages/com.wallstop-studios.dxcommandterminal/Runtime/Analyzers/Generator.dll"
                imported.write_bytes(b"stale-generator")
                with self.assertRaises(ValueError):
                    smoke.validate_payload(artifact, project)
            finally:
                setattr(smoke, "ROOT", previous_root)

    def test_fixture_update_requires_matching_recorded_owned_bytes(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            target = project / "Assets" / "T13PlayerSmoke" / "PlayerProbe.cs"
            target.parent.mkdir(parents=True)
            target.write_bytes(b"old-owned-fixture")
            prior = {"project": str(project), "fixtureHashes": {"PlayerProbe.cs.txt": smoke.sha256(target)}}
            for record in [None, {"project": str(project), "fixtureHashes": {}}, {"project": "unrelated", "fixtureHashes": prior["fixtureHashes"]}]:
                with self.subTest(record=record):
                    with self.assertRaises(ValueError):
                        smoke.install_fixtures(project, record)
                    self.assertEqual(target.read_bytes(), b"old-owned-fixture")
            smoke.install_fixtures(project, prior)
            self.assertEqual(target.read_bytes(), (smoke.FIXTURES / "PlayerProbe.cs.txt").read_bytes())

    def test_failed_tree_kill_uses_bounded_direct_fallback(self) -> None:
        for failure in [subprocess.TimeoutExpired("taskkill", 30), FileNotFoundError("taskkill missing")]:
            with self.subTest(failure=failure):
                process = Mock(pid=314, poll=Mock(return_value=None))
                with patch.object(smoke.subprocess, "run", side_effect=failure) as taskkill:
                    smoke.stop_owned_process(process)
                self.assertEqual(taskkill.call_args.kwargs["timeout"], 30)
                process.kill.assert_called_once()
                process.wait.assert_called_once_with(timeout=30)

    def test_duplicate_or_wrong_input_profile_is_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            settings = project / "ProjectSettings" / "ProjectSettings.asset"
            settings.parent.mkdir()
            for text in ["  activeInputHandler: 1\n", "  activeInputHandler: 0\n  activeInputHandler: 0\n", ""]:
                with self.subTest(text=text):
                    settings.write_text(text, encoding="utf-8")
                    with self.assertRaises(ValueError):
                        smoke.validate_profile(project, "legacy")


if __name__ == "__main__":
    unittest.main()
