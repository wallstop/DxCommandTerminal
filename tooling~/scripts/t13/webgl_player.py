"""Run an isolated browser against a loopback-only WebGL build and retain its report."""
from __future__ import annotations

from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
import json
import hashlib
from pathlib import Path
import re
import subprocess
import threading
import time
from typing import Any, Callable, cast
from uuid import uuid4


HARNESS_SOURCE = Path(__file__).read_bytes()
HARNESS_SHA256 = hashlib.sha256(HARNESS_SOURCE).hexdigest()


class ReportServer(ThreadingHTTPServer):
    def __init__(self, directory: Path, token: str, report: list[bytes], ready: threading.Event) -> None:
        self.report_lock = threading.Lock()
        handler = partial(ReportHandler, directory=str(directory), token=token, report=report, ready=ready)
        super().__init__(("127.0.0.1", 0), handler)


class ReportHandler(SimpleHTTPRequestHandler):
    timeout = 5

    def __init__(self, *args: Any, directory: str, token: str, report: list[bytes], ready: threading.Event, **kwargs: Any) -> None:
        self.token, self.report, self.ready = token, report, ready
        super().__init__(*args, directory=directory, **kwargs)

    def log_message(self, format: str, *args: Any) -> None:
        pass

    def end_headers(self) -> None:
        self.send_header("Cross-Origin-Opener-Policy", "same-origin")
        self.send_header("Cross-Origin-Embedder-Policy", "require-corp")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def do_POST(self) -> None:
        if self.path != "/report?token=" + self.token:
            self.send_error(404)
            return
        length = self.headers.get("Content-Length", "")
        if re.fullmatch(r"[0-9]{1,7}", length) is None or not 0 < int(length) <= 1048576:
            self.send_error(400)
            return
        self.connection.settimeout(5)
        try:
            body = self.rfile.read(int(length))
            value = json.loads(body)
            if len(body) != int(length) or not isinstance(value, dict):
                raise ValueError("Expected a complete report object")
        except (OSError, ValueError):
            self.send_error(400)
            return
        # A report has one owner even when browser callbacks race.
        with cast(ReportServer, self.server).report_lock:
            if self.report:
                self.send_error(409)
                return
            self.report.append(body)
            self.ready.set()
        self.send_response(200)
        self.end_headers()


def report_server(directory: Path, token: str, report: list[bytes], ready: threading.Event) -> ReportServer:
    return ReportServer(directory, token, report, ready)


def run_browser(browser: Path, output: Path, timeout: int, stop_process: Callable[[subprocess.Popen[Any]], None]) -> dict[str, Any]:
    token = uuid4().hex
    reports: list[bytes] = []
    ready = threading.Event()
    server = report_server(output / "Player", token, reports, ready)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    arguments = [str(browser), "--headless=new", "--no-first-run", "--no-default-browser-check",
                 "--disable-background-networking", "--autoplay-policy=no-user-gesture-required",
                 "--enable-unsafe-swiftshader", "--user-data-dir=" + str(output / "browser-profile"),
                 f"http://127.0.0.1:{server.server_port}/?token={token}"]
    process = None
    try:
        with (output / "browser.log").open("wb") as log:
            process = subprocess.Popen(arguments, stdout=log, stderr=subprocess.STDOUT)
            (output / "browser-pid.txt").write_text(str(process.pid), encoding="ascii")
            deadline = time.monotonic() + timeout
            while not ready.wait(min(0.2, max(0, deadline - time.monotonic()))):
                if process.poll() is not None:
                    raise RuntimeError(f"Browser exited before reporting: {process.returncode}")
                if deadline <= time.monotonic():
                    raise TimeoutError("Actual WebGL player emitted no report before deadline")
            (output / "player-report.json").write_bytes(reports[0])
            return {"pid": process.pid, "arguments": arguments, "reportToken": token,
                    "rendering": "headless browser; software WebGL allowed; no visual acceptance"}
    finally:
        try:
            if process is not None:
                stop_process(process)
        finally:
            server.shutdown()
            server.server_close()
            thread.join(timeout=5)
