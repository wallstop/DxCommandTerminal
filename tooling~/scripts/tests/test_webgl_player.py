"""Exercise the actual loopback report protocol without Unity or a browser dependency."""
from __future__ import annotations

from contextlib import contextmanager
import http.client
import importlib.util
import json
import socket
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import Mock, patch
from urllib.parse import urlsplit

SPEC = importlib.util.spec_from_file_location("webgl_player", Path(__file__).parents[1] / "t13" / "webgl_player.py")
assert SPEC is not None and SPEC.loader is not None
webgl = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(webgl)


class BrowserReportTests(unittest.TestCase):
    def setUp(self) -> None:
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.report: list[bytes] = []
        self.ready = threading.Event()
        self.server = webgl.report_server(Path(self.directory.name), "owner-token", self.report, self.ready)
        self.thread = threading.Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.01}, daemon=True)
        self.thread.start()
        self.addCleanup(self.close)

    def close(self) -> None:
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)
        self.assertFalse(self.thread.is_alive())

    def request(self, path: str, body: bytes, length: str | None = None) -> tuple[int, dict[str, str]]:
        connection = http.client.HTTPConnection("127.0.0.1", self.server.server_port, timeout=5)
        try:
            headers = {} if length is None else {"Content-Length": length}
            connection.request("POST", path, body, headers)
            response = connection.getresponse()
            response.read()
            return response.status, dict(response.getheaders())
        finally:
            connection.close()

    def test_wrong_or_absent_owner_cannot_claim_a_report(self) -> None:
        for path in ("/report", "/report?token=other", "/report?token=owner-token&extra=1"):
            with self.subTest(path=path):
                self.assertEqual(self.request(path, b'{"passed":true}')[0], 404)
                self.assertEqual(self.report, [])
                self.assertFalse(self.ready.is_set())

    def test_malformed_or_oversized_body_does_not_consume_the_owner(self) -> None:
        for body, length in ((b"not-json", None), (b"[]", None), (b"null", None),
                             (b"{}", "-1"), (b"{}", "1048577"), (b"{}", "bogus")):
            with self.subTest(body=body, length=length):
                self.assertEqual(self.request("/report?token=owner-token", body, length)[0], 400)
                self.assertEqual(self.report, [])
                self.assertFalse(self.ready.is_set())
        self.assertEqual(self.request("/report?token=owner-token", b'{"passed":false}')[0], 200)
        self.assertEqual(json.loads(self.report[0]), {"passed": False})

    def test_idle_browser_connection_cannot_block_report_delivery(self) -> None:
        with socket.create_connection(("127.0.0.1", self.server.server_port), timeout=5) as idle:
            idle.sendall(b"GET / HTTP/1.1\r\n")
            self.assertEqual(self.request("/report?token=owner-token", b'{"passed":true}')[0], 200)
            self.assertTrue(self.ready.wait(1))
            idle.sendall(b"\r\n")
            response = http.client.HTTPResponse(idle)
            response.begin()
            response.read()

    def test_browser_cleanup_failure_still_closes_the_report_listener(self) -> None:
        original = webgl.report_server
        servers = []
        def open_server(*arguments):
            server = original(*arguments)
            servers.append(server)
            return server
        def launch(arguments, **kwargs):
            url = urlsplit(arguments[-1])
            connection = http.client.HTTPConnection(url.hostname, url.port, timeout=5)
            try:
                connection.request("POST", "/report?" + url.query, b'{"passed":true}')
                response = connection.getresponse()
                self.assertEqual(response.status, 200)
                response.read()
            finally:
                connection.close()
            return Mock(pid=42)
        @contextmanager
        def own_process(process):
            try:
                yield process
            finally:
                raise RuntimeError("cannot stop")
        try:
            with patch.object(webgl, "report_server", side_effect=open_server), patch.object(webgl.subprocess, "Popen", side_effect=launch):
                with self.assertRaisesRegex(RuntimeError, "cannot stop"):
                    webgl.run_browser(Path("browser"), Path(self.directory.name), 5, own_process)
            self.assertEqual(servers[0].fileno(), -1)
        finally:
            for server in servers:
                server.shutdown()
                server.server_close()

    def test_report_is_exact_and_cannot_be_replaced(self) -> None:
        body = '{"passed": true, "failure": "line\\nquoted \\\" Ω"}'.encode()
        status, headers = self.request("/report?token=owner-token", body)
        self.assertEqual(status, 200)
        self.assertTrue(self.ready.wait(1))
        self.assertEqual(self.report, [body])
        self.assertEqual(headers["Cross-Origin-Opener-Policy"], "same-origin")
        self.assertEqual(headers["Cross-Origin-Embedder-Policy"], "require-corp")
        self.assertEqual(self.request("/report?token=owner-token", b'{"passed":false}')[0], 409)
        self.assertEqual(self.report, [body])


if __name__ == "__main__":
    unittest.main()
