"""Reject incomplete or stale player timing evidence and pin the p95 gate."""
from __future__ import annotations

import copy
from contextlib import ExitStack, contextmanager
import json
import io
import subprocess
import tarfile
from unittest.mock import Mock, patch
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


class OwnedProcessTests(unittest.TestCase):
    def test_terminal_success_does_not_stop_an_already_reaped_process(self) -> None:
        process = Mock(poll=Mock(return_value=0), wait=Mock(return_value=0))
        with patch.object(smoke, "stop_owned_process") as stop:
            with smoke.owned_process(process) as owned:
                self.assertIs(owned, process)
                self.assertEqual(0, owned.wait(timeout=17))
        process.wait.assert_called_once_with(timeout=17)
        stop.assert_not_called()

    def test_lifetime_cleans_live_children_for_errors_interruptions_and_normal_exit(self) -> None:
        for error in (None, OSError("metadata"), KeyboardInterrupt("interrupt"), SystemExit(3), TimeoutError("deadline")):
            with self.subTest(error=type(error).__name__):
                process = Mock(pid=314, poll=Mock(return_value=None))
                with patch.object(smoke, "stop_owned_process") as stop:
                    if error is None:
                        with smoke.owned_process(process):
                            pass
                    else:
                        with self.assertRaises(type(error)) as caught:
                            with smoke.owned_process(process):
                                raise error
                        self.assertIs(caught.exception, error)
                stop.assert_called_once_with(process)

    def test_cleanup_failure_is_fatal_and_keeps_both_exceptions(self) -> None:
        for primary in (None, OSError("original metadata failure"), KeyboardInterrupt("original interruption")):
            with self.subTest(primary=type(primary).__name__):
                process = Mock(pid=314, poll=Mock(return_value=None))
                cleanup = OSError("cannot reap owned child")
                with patch.object(smoke, "stop_owned_process", side_effect=cleanup) as stop:
                    with self.assertRaises(smoke.OwnedProcessCleanupError) as caught:
                        with smoke.owned_process(process):
                            if primary is not None:
                                raise primary
                self.assertIsInstance(caught.exception, RuntimeError)
                self.assertIs(caught.exception.primary, primary)
                self.assertIs(caught.exception.process, process)
                self.assertIn("314", str(caught.exception))
                self.assertIs(caught.exception.cleanup, cleanup)
                self.assertIs(caught.exception.__cause__, primary)
                self.assertIn("cannot reap owned child", str(caught.exception))
                if primary is not None:
                    self.assertIn(str(primary), str(caught.exception))
                stop.assert_called_once_with(process)

    def test_actual_run_leg_scopes_cleanup_metadata_wait_and_interruption_failures(self) -> None:
        for site in ("readiness-player", "readiness-build", "smoke-build"):
            errors = [OSError("owner publication failed")] if site == "readiness-player" else [OSError("wait failed"), KeyboardInterrupt("wait interrupted"), subprocess.TimeoutExpired("owned", 30)]
            for error in errors:
                for cleanup_fails in (False, True):
                    with self.subTest(site=site, error=type(error).__name__, cleanup_fails=cleanup_fails), tempfile.TemporaryDirectory() as temporary:
                        root = Path(temporary)
                        output = root / "out"
                        revision = "a" * 40
                        digest = "b" * 64
                        leg = dict(inputProfile="legacy", project=str(root / "project"), unity=str(root / "Unity.exe"), artifact=str(root / "artifact"), editorVersion="2021.3.45f1", artifactSha256=digest, failed=False, complete=True, artifactHashMatches=True, git=dict(dirty=False, revision=revision))
                        children: list[Any] = []
                        original_write = Path.write_text

                        def launch(*args: Any, **kwargs: Any) -> Any:
                            player = bool(children)
                            victim = site != "readiness-player" or player
                            child = Mock(pid=4102 if player else 4101, poll=Mock(return_value=None if victim else 0), wait=Mock(side_effect=error if victim else None, return_value=0))
                            children.append(child)
                            if not victim:
                                (output / "build-target.txt").write_text("StandaloneWindows64")
                                (output / "build-result.txt").write_text("Succeeded errors=0 warnings=0 duration=1")
                                (output / "build.log").write_text("")
                                (output / "Player").mkdir()
                                (output / "Player/Smoke.exe").write_bytes(b"fixture")
                            return child

                        def write(path: Path, *args: Any, **kwargs: Any) -> int:
                            if site == "readiness-player" and path.name == "owner.json":
                                raise error
                            return original_write(path, *args, **kwargs)

                        def check_output(command: list[str], **kwargs: Any) -> str:
                            return "2021.3.45f1\n" if "-version" in command else revision + "\n"

                        def run() -> dict[str, Any]:
                            if site.startswith("readiness"):
                                return readiness.run_leg(leg, output, None, 30)
                            return smoke.run_leg(leg, root / "artifact", output, 30)

                        with ExitStack() as stack:
                            stack.enter_context(patch.object(subprocess, "Popen", side_effect=launch))
                            stack.enter_context(patch.object(subprocess, "check_output", side_effect=check_output))
                            stack.enter_context(patch.object(Path, "write_text", write))
                            for name, value in (("sha256", digest), ("validate_profile", None), ("install_fixtures", {}), ("checked_build_settings", {}), ("validate_payload", {})):
                                stack.enter_context(patch.object(smoke, name, return_value=value))
                            stack.enter_context(patch.object(readiness, "verified_payload", return_value=(revision, {})))
                            stop = stack.enter_context(patch.object(smoke, "stop_owned_process", side_effect=OSError("cleanup failed") if cleanup_fails else None))
                            if cleanup_fails:
                                with self.assertRaises(smoke.OwnedProcessCleanupError) as caught:
                                    run()
                                self.assertIn("cleanup failed", str(caught.exception))
                                self.assertFalse((output / "manifest.json").exists())
                            elif isinstance(error, KeyboardInterrupt):
                                with self.assertRaises(KeyboardInterrupt) as caught:
                                    run()
                                self.assertIs(caught.exception, error)
                            else:
                                record = run()
                                self.assertFalse(record["passed"])
                                expected = "exceeded deadline" if isinstance(error, subprocess.TimeoutExpired) else str(error)
                                self.assertIn(expected, record["failure"])
                                self.assertTrue((output / "manifest.json").is_file())
                        stop.assert_called_once_with(children[-1])
                        if site == "readiness-player":
                            children[-1].wait.assert_not_called()
                        else:
                            children[-1].wait.assert_called_once_with(timeout=30)

    def test_actual_webgl_scope_skips_terminal_stop_and_aborts_on_cleanup_failure(self) -> None:
        for mode in ("terminal", "cleanup-failure", "owner-and-cleanup-failure", "cleanup-and-listener-failure", "cleanup-and-log-close-failure"):
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                output = root / "out"
                browser = root / "browser"
                browser.write_bytes(b"fixture")
                revision, digest = "a" * 40, "b" * 64
                leg = dict(inputProfile="legacy", project=str(root / "project"), unity=str(root / "Unity.exe"), editorVersion="2021.3.45f1", artifactSha256=digest, failed=False, complete=True, artifactHashMatches=True, git=dict(dirty=False, revision=revision))
                children: list[Any] = []
                server = Mock(server_port=12345)
                listener_error = OSError("listener shutdown failed")
                if mode == "cleanup-and-listener-failure":
                    server.shutdown.side_effect = listener_error
                original_write = Path.write_text
                original_open = Path.open
                log_error = OSError("browser log close failed")
                primary = OSError("browser owner publication failed")
                cleanup = OSError("browser cleanup failed")

                def launch(*args: Any, **kwargs: Any) -> Any:
                    child = Mock(pid=5101 + len(children), poll=Mock(return_value=0 if not children or mode == "terminal" else None), wait=Mock(return_value=0))
                    children.append(child)
                    if len(children) == 1:
                        (output / "build-target.txt").write_text("WebGL")
                        (output / "build.log").write_text("")
                        (output / "Player").mkdir()
                        (output / "Player/game.wasm").write_bytes(b"fixture")
                    return child

                def open_server(directory: Path, token: str, reports: list[bytes], ready: Any) -> Any:
                    reports.append(b'{"passed":true}')
                    ready.set()
                    return server

                def write(path: Path, *args: Any, **kwargs: Any) -> int:
                    if mode == "owner-and-cleanup-failure" and path.name == "browser-pid.txt":
                        raise primary
                    return original_write(path, *args, **kwargs)

                @contextmanager
                def failing_log(path: Path, *args: Any, **kwargs: Any) -> Any:
                    with original_open(path, *args, **kwargs) as stream:
                        try:
                            yield stream
                        finally:
                            raise log_error

                def open_file(path: Path, *args: Any, **kwargs: Any) -> Any:
                    if mode == "cleanup-and-log-close-failure" and path.name == "browser.log":
                        return failing_log(path, *args, **kwargs)
                    return original_open(path, *args, **kwargs)

                with ExitStack() as stack:
                    stack.enter_context(patch.object(Path, "open", open_file))
                    stack.enter_context(patch.object(subprocess, "Popen", side_effect=launch))
                    stack.enter_context(patch.object(subprocess, "check_output", side_effect=lambda command, **kwargs: "2021.3.45f1\n" if "-version" in command else revision + "\n"))
                    stack.enter_context(patch.object(Path, "write_text", write))
                    stack.enter_context(patch.object(smoke.webgl_player, "report_server", side_effect=open_server))
                    for name, value in (("sha256", digest), ("validate_profile", None), ("install_fixtures", {}), ("checked_build_settings", {}), ("validate_payload", {}), ("checked_player_report", {})):
                        stack.enter_context(patch.object(smoke, name, return_value=value))
                    stop = stack.enter_context(patch.object(smoke, "stop_owned_process", side_effect=cleanup))
                    if mode == "terminal":
                        result = smoke.run_leg(leg, root / "artifact", output, 30, backend="IL2CPP", stripping="High", target="WebGL", browser=browser)
                        self.assertTrue(result["passed"])
                        stop.assert_not_called()
                    else:
                        with self.assertRaises(smoke.OwnedProcessCleanupError) as caught:
                            smoke.run_leg(leg, root / "artifact", output, 30, backend="IL2CPP", stripping="High", target="WebGL", browser=browser)
                        self.assertIs(caught.exception.cleanup, cleanup)
                        self.assertIs(caught.exception.process, children[-1])
                        self.assertIn(str(children[-1].pid), str(caught.exception))
                        if mode == "cleanup-and-listener-failure":
                            self.assertIs(caught.exception.__cause__, listener_error)
                        if mode == "cleanup-and-log-close-failure":
                            self.assertIs(caught.exception.__cause__, log_error)
                        self.assertIs(caught.exception.primary, primary if mode == "owner-and-cleanup-failure" else None)
                        self.assertFalse((output / "manifest.json").exists())
                        stop.assert_called_once_with(children[-1])
                self.assertEqual(len(children), 2)
                server.shutdown.assert_called_once()
                if mode == "cleanup-and-listener-failure":
                    server.server_close.assert_not_called()
                else:
                    server.server_close.assert_called_once()


if __name__ == "__main__":
    unittest.main()
