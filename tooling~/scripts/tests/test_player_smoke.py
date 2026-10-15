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
    def test_editor_version_uses_first_plausible_line(self) -> None:
        for output, expected in [
            ("2021.3.45f1", "2021.3.45f1"),
            ("Unity banner\n  2022.3.62f3 \r\n6000.4.6f1\n", "2022.3.62f3"),
            ("\t6000.4.0b1-test\t\n", "6000.4.0b1-test"),
            ("Banner\n \ufeff2022.3.62f3\ufeff \r\n", "2022.3.62f3"),
            ("2021.3.45f1\u00e9\n２０２１.3.45f1\n6000.4.6f1", "6000.4.6f1"),
        ]:
            with self.subTest(output=output):
                self.assertEqual(expected, smoke.parse_editor_version(output))
        for output in ("", "Unity banner only", "2021.3.45f1 extra", "21.3.45f1", "2021.x", "２０２１.3.45f1",
                       "\x1c2021.3.45f1", "2021.3.45f1\x85", "Banner\u20282021.3.45f1"):
            with self.subTest(output=output):
                with self.assertRaisesRegex(ValueError, "no plausible version"):
                    smoke.parse_editor_version(output)

    def profile_legs(self, identifiers: list[str]) -> list[dict[str, Any]]:
        return [dict(editor=identifier, inputProfile=profile, requestedFamily="2021.3",
                     editorVersion="2021.3.45f1", domainReloadEnabled=True)
                for identifier, profile in zip(identifiers, ("legacy", "input-system", "both"))]

    def test_profile_selection_accepts_suffix_custom_and_repeated_identifiers(self) -> None:
        for identifiers in (["unity-2021", "unity-2021-input-system", "unity-2021-both"],
                            ["custom-legacy", "custom-input", "custom-combined"], ["shared"] * 3):
            with self.subTest(identifiers=identifiers):
                legs = self.profile_legs(identifiers)
                self.assertEqual(legs, smoke.select_profile_legs({"legs": list(reversed(legs))}, identifiers[0]))

    def test_profile_selection_separates_version_family_and_reload_rows(self) -> None:
        legs = self.profile_legs(["anchor", "input", "both"])
        unrelated = [dict(legs[1], editorVersion="2022.3.62f3"),
                     dict(legs[2], domainReloadEnabled=False), dict(legs[1], requestedFamily="6000.4")]
        self.assertEqual(legs, smoke.select_profile_legs({"legs": unrelated + legs}, "anchor"))
        for field, value in (("editorVersion", "2022.3.62f3"), ("requestedFamily", "6000.4"),
                             ("domainReloadEnabled", False), ("domainReloadEnabled", 1)):
            with self.subTest(field=field, value=value):
                mismatched = [legs[0], dict(legs[1], **{field: value}), legs[2]]
                with self.assertRaises(ValueError):
                    smoke.select_profile_legs({"legs": mismatched}, "anchor")

    def test_profile_selection_rejects_missing_and_ambiguous_legs(self) -> None:
        legs = self.profile_legs(["anchor", "input", "both"])
        for rows in (legs[1:], legs[:2], legs + [dict(legs[0])], legs + [dict(legs[1], editor="other-input")],
                     legs + [dict(legs[2], editor="other-both")],
                     [dict(legs[0], editorVersion=""), legs[1], legs[2]],
                     [dict(legs[0], domainReloadEnabled=None), legs[1], legs[2]]):
            with self.subTest(rows=rows):
                with self.assertRaises(ValueError):
                    smoke.select_profile_legs({"legs": rows}, "anchor")

    def test_stale_or_partial_player_verdict_is_rejected(self) -> None:
        good: dict[str, Any] = {"passed": True, "unityVersion": "2021.3.45f1", "platform": "WindowsPlayer", "developmentBuild": False, "monoBackend": True, "il2cppBackend": False, "inputSystem": False, "legacyInput": True, "catalogPresent": True, "catalogEntries": 2, "catalogBindersExecuted": 2, "generatedCalls": 1, "privateCalls": 1, "manualCalls": 1}
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / "report.json"
            file.write_text(json.dumps(good), encoding="utf-8")
            smoke.checked_player_report(file, "legacy", "2021.3.45f1")
            native = dict(good, monoBackend=False, il2cppBackend=True)
            file.write_text(json.dumps(native), encoding="utf-8")
            smoke.checked_player_report(file, "legacy", "2021.3.45f1", "IL2CPP")
            with self.assertRaises(ValueError):
                smoke.checked_player_report(file, "legacy", "2021.3.45f1", "Mono")
            file.write_text(json.dumps(good), encoding="utf-8")
            with self.assertRaises(ValueError):
                smoke.checked_player_report(file, "legacy", "2021.3.45f1", "IL2CPP")
            for field, value in [("il2cppBackend", True), ("passed", 1), ("catalogPresent", False), ("developmentBuild", True), ("monoBackend", False), ("privateCalls", 0), ("unityVersion", "6000.4.6f1"), ("legacyInput", False), ("failure", "Failed dispatch")]:
                with self.subTest(field=field):
                    file.write_text(json.dumps(dict(good, **{field: value})), encoding="utf-8")
                    with self.assertRaises(ValueError):
                        smoke.checked_player_report(file, "legacy", "2021.3.45f1")

    def test_missing_or_mismatched_build_settings_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            with self.assertRaises(FileNotFoundError):
                smoke.checked_build_settings(output, "IL2CPP", "High")
            for actual in ("Mono/High", "IL2CPP/Low", "IL2CPP/High\n", ""):
                with self.subTest(actual=actual):
                    (output / "build-settings.txt").write_text(actual, encoding="utf-8")
                    with self.assertRaises(ValueError):
                        smoke.checked_build_settings(output, "IL2CPP", "High")
            (output / "build-settings.txt").write_text("IL2CPP/High", encoding="utf-8")
            self.assertEqual("IL2CPP/High", smoke.checked_build_settings(output, "IL2CPP", "High"))

    def payload_fixture(self, root: Path) -> tuple[Path, Path, Path, list[tuple[str, bytes]]]:
        source = root / "source"
        project = root / "project"
        artifact = root / "payload.unitypackage"
        assets = {"package.json": b'{"name":"com.wallstop-studios.dxcommandterminal"}',
                  "Runtime/Analyzers/Generator.dll": b"actual-generator", "Runtime": None}
        entries: list[tuple[str, bytes]] = []
        for index, (name, data) in enumerate(assets.items()):
            meta = f"fileFormatVersion: 2\nguid: {index:032x}\n".encode()
            if data is None:
                meta += b"folderAsset: yes\n"
            entries.extend([(f"{index:032x}/pathname", f"Packages/com.wallstop-studios.dxcommandterminal/{name}".encode()),
                            (f"{index:032x}/asset.meta", meta)])
            if data is not None:
                entries.append((f"{index:032x}/asset", data))
            for base in (source, project / "Packages/com.wallstop-studios.dxcommandterminal"):
                file = base / name
                file.parent.mkdir(parents=True, exist_ok=True)
                if data is None:
                    file.mkdir(exist_ok=True)
                else:
                    file.write_bytes(data)
                file.with_name(file.name + ".meta").write_bytes(meta)
        self.write_artifact(artifact, entries)
        return source, project, artifact, entries

    def write_artifact(self, artifact: Path, entries: list[tuple[str, bytes]]) -> None:
        with tarfile.open(artifact, "w:gz") as archive:
            for name, content in entries:
                member = tarfile.TarInfo(name)
                member.size = len(content)
                archive.addfile(member, io.BytesIO(content))

    def test_imported_assets_and_file_or_folder_metas_must_match(self) -> None:
        for relative in ("Runtime/Analyzers/Generator.dll", "Runtime/Analyzers/Generator.dll.meta", "Runtime.meta"):
            for mutation in ("corrupt", "missing"):
                with self.subTest(relative=relative, mutation=mutation), tempfile.TemporaryDirectory() as directory:
                    source, project, artifact, _ = self.payload_fixture(Path(directory))
                    with patch.object(smoke, "ROOT", source):
                        smoke.validate_payload(artifact, project)
                        imported = project / "Packages/com.wallstop-studios.dxcommandterminal" / relative
                        if mutation == "missing":
                            imported.unlink()
                        else:
                            imported.write_bytes(b"corrupted")
                        with self.assertRaisesRegex(ValueError, "bytes differ"):
                            smoke.validate_payload(artifact, project)

    def test_archive_metadata_must_match_source(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            source, project, artifact, entries = self.payload_fixture(Path(directory))
            entries = [(name, b"changed" if name.endswith("/asset.meta") else content) for name, content in entries]
            self.write_artifact(artifact, entries)
            with patch.object(smoke, "ROOT", source), self.assertRaisesRegex(ValueError, "committed package source"):
                smoke.validate_payload(artifact, project)

    def test_missing_payloads_and_duplicate_members_or_paths_are_rejected(self) -> None:
        for mutation in ("missing-meta", "missing-asset", "duplicate-member", "duplicate-path", "case-collision"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                source, project, artifact, entries = self.payload_fixture(Path(directory))
                if mutation.startswith("missing"):
                    suffix = "/asset.meta" if mutation == "missing-meta" else "/asset"
                    entries = [(name, content) for name, content in entries if not name.endswith(suffix)]
                elif mutation == "duplicate-member":
                    entries.append(entries[0])
                else:
                    original = entries[0][1]
                    pathname = original.upper().replace(b"PACKAGES/COM.WALLSTOP-STUDIOS.DXCOMMANDTERMINAL/", b"Packages/com.wallstop-studios.dxcommandterminal/") if mutation == "case-collision" else original
                    entries.append(("extra/pathname", pathname))
                self.write_artifact(artifact, entries)
                with patch.object(smoke, "ROOT", source), self.assertRaises(ValueError):
                    smoke.validate_payload(artifact, project)

    def test_noncanonical_paths_are_rejected_before_reading_source(self) -> None:
        for relative in ("../outside", "/rooted", "./package.json", "Runtime//file", "C:/rooted", "Runtime\\file"):
            with self.subTest(relative=relative), tempfile.TemporaryDirectory() as directory:
                source, project, artifact, entries = self.payload_fixture(Path(directory))
                entries[0] = (entries[0][0], f"Packages/com.wallstop-studios.dxcommandterminal/{relative}".encode())
                self.write_artifact(artifact, entries)
                with patch.object(smoke, "ROOT", source), self.assertRaisesRegex(ValueError, "Unsafe pathname"):
                    smoke.validate_payload(artifact, project)

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
