"""The devcontainer lifecycle contract, verified end to end (issue #220).

Ports `test-devcontainer.sh` to typed stdlib Python: the OpenCode verifier
gate, the baked configuration contract, the npm range resolver, the shell
delegator, and the full post-start lifecycle with stubbed CLIs. The lifecycle
flow itself (`post_start.py`) is typed too; the suite drives it through the
real `.devcontainer` entries via subprocesses with a sanitized PATH, so a
regression fails here before a container build does.
"""
from __future__ import annotations

import importlib.util
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from typing import Dict, Iterable, List

REPO_ROOT = Path(__file__).parents[3]
DEVCONTAINER = REPO_ROOT / ".devcontainer" / "devcontainer.json"
DOCKERFILE = REPO_ROOT / ".devcontainer" / "Dockerfile"
INSTALLER = REPO_ROOT / ".devcontainer" / "install-agent-clis.sh"
INSTALLER_PY = REPO_ROOT / ".devcontainer" / "install_agent_clis.py"
POST_START = REPO_ROOT / ".devcontainer" / "post-start.sh"
POST_START_PY = REPO_ROOT / ".devcontainer" / "post_start.py"
VERIFY_SCRIPT = REPO_ROOT / ".devcontainer" / "verify-opencode.sh"

# The checkout's credentials and the loader's own control variables must not
# leak in from the caller, or process precedence hides what the lifecycle did.
LIFECYCLE_UNSET = (
    "ZAI_API_KEY",
    "Z_AI_API_KEY",
    "ZHIPU_API_KEY",
    "OPENROUTER_API_KEY",
    "GITHUB_TOKEN",
    "GH_TOKEN",
    "GITHUB_PERSONAL_ACCESS_TOKEN",
    "GITHUB_PAT",
    "GITHUB_MCP_PAT",
    "UNITY_MCP_BEARER_TOKEN",
    "AI_BACKENDS_REPO_ROOT",
    "DXT_WORKSPACE_ROOT",
    "DXT_ENV_AUTOLOAD_DISABLED",
    "DXT_ENV_AUTOLOAD_ACTIVE",
    "BASH_ENV",
)

# The lifecycle sandbox only resolves the stubbed CLIs plus the core utilities
# the scripts reach for (flock, grep, and friends).
LIFECYCLE_PATH = "/usr/bin:/bin:/usr/sbin:/sbin"

CREDENTIAL_NAMES = (
    "GITHUB_TOKEN",
    "GH_TOKEN",
    "GITHUB_PERSONAL_ACCESS_TOKEN",
    "GITHUB_PAT",
    "GITHUB_MCP_PAT",
    "ZAI_API_KEY",
    "Z_AI_API_KEY",
    "ZHIPU_API_KEY",
    "OPENROUTER_API_KEY",
)

SKIP_ON_WINDOWS = unittest.skipIf(sys.platform == "win32", "the lifecycle scripts are bash")

FAKE_OPENCODE_V2 = """#!/usr/bin/env bash
printf '%s\\n' "${DXT_FAKE_OPENCODE_VERSION:-opencode v2.0.16}"
"""

FAKE_OPENCODE2_V2 = """#!/usr/bin/env bash
printf '%s\\n' "${DXT_FAKE_OPENCODE2_VERSION:-opencode v2.0.16}"
"""

FAKE_NPM_REGISTRY = """#!/usr/bin/env bash
printf '%s\\n' "${DXT_FAKE_NPM_VIEW:-[\\"2.0.0\\",\\"2.0.16\\",\\"2.0.15\\"]}"
"""

STUB_NPM_OK = """#!/bin/bash
exit 0
"""

STUB_CACHE_CONTRACT = """#!/usr/bin/env bash
cache_contract_repair_permissions() { return 0; }
"""

STUB_INSTALLER_OK = """#!/usr/bin/env bash
exit 0
"""

STUB_NODE = """#!/usr/bin/env bash
if [[ "$*" == *"configure --offline"* && ! -f .env.local ]]; then
    printf '%s\\n' 'UNITY_MCP_BEARER_TOKEN=generated-token-123' >> .env.local
    printf '%s\\n' 'Z_AI_API_KEY=zai-file-key' >> .env.local
    printf '%s\\n' 'GITHUB_PERSONAL_ACCESS_TOKEN=github-file-key' >> .env.local
fi
"""

STUB_OPENCODE = """#!/usr/bin/env bash
printf '%s\\n' "$*" >> opencode-calls.txt
case "${1:-}" in
    --version) printf '%s\\n' 'opencode v2.0.16' ;;
    service)
        case "${2:-}" in
            status) printf '%s\\n' 'http://127.0.0.1:49374' ;;
            restart)
                {
                    printf 'UNITY=%s\\n' "${UNITY_MCP_BEARER_TOKEN-<unset>}"
                    printf 'GITHUB=%s\\n' "${GITHUB_TOKEN-<unset>}"
                    printf 'ZAI=%s\\n' "${ZAI_API_KEY-<unset>}"
                    printf 'BASH_ENV=%s\\n' "${BASH_ENV-<unset>}"
                } > service-token.txt
                ;;
            stop) : ;;
        esac
        ;;
    mcp)
        if [[ "${2:-}" == "list" ]]; then
            if [[ "${DXT_FAKE_MCP_STDERR:-0}" == "1" ]]; then
                # The CLI reports the failed server on stderr, exit nonzero.
                printf '%s\\n' '✗ unity-mcp failed: Unauthorized' >&2
                exit 1
            elif [[ "${DXT_FAKE_MCP_MULTIROW:-0}" == "1" ]]; then
                # A healthy project row next to a rejected stale one.
                printf '%s\\n' '✓ unity-mcp connected'
                printf '%s\\n' '✗ unity-mcp failed: Unauthorized'
            elif [[ "${DXT_FAKE_MCP_FAILURE:-0}" == "1" ]]; then
                printf '%s\\n' '✗ unity-mcp failed: Unauthorized'
            elif [[ "${DXT_FAKE_OTHER_MCP_FAILURE:-0}" == "1" ]]; then
                # A healthy unity bridge next to a failing optional server.
                printf '%s\\n' '✗ github failed: Unauthorized'
                printf '%s\\n' '✓ unity-mcp connected'
            else
                printf '%s\\n' '✓ unity-mcp connected'
            fi
        fi
        ;;
esac
"""

STUB_OPENCODE_V1 = """#!/usr/bin/env bash
case "${1:-}" in
    --version) printf '%s\\n' 'opencode v1.18.32' ;;
    mcp) [[ "${2:-}" == "list" ]] && printf '%s\\n' '✓ unity-mcp connected' ;;
esac
"""


def write_script(path: Path, body: str) -> None:
    path.write_text(body, encoding="utf-8")
    path.chmod(0o755)


def sanitize_env(unset: Iterable[str] = LIFECYCLE_UNSET) -> Dict[str, str]:
    return {key: value for key, value in os.environ.items() if key not in unset}


def line_count(text: str, needle: str) -> int:
    return sum(1 for line in text.splitlines() if needle in line)


@SKIP_ON_WINDOWS
class OpenCodeVerifierContract(unittest.TestCase):
    """A v1-only or mismatched pair must never pass the verifier gate."""

    def setUp(self) -> None:
        self.work = Path(tempfile.mkdtemp(prefix="dxt-verify-"))
        self.addCleanup(shutil.rmtree, self.work, True)
        self.bin = self.work / "bin"
        self.bin.mkdir()
        write_script(self.bin / "opencode", FAKE_OPENCODE_V2)
        write_script(self.bin / "opencode2", FAKE_OPENCODE2_V2)
        # Exclude the host's real CLIs so the alias-absent case cannot see one.
        self.verify_path = f"{self.bin}:/usr/bin:/bin"

    def run_verifier(self, **env_overrides: str) -> subprocess.CompletedProcess[str]:
        env = sanitize_env()
        env["PATH"] = self.verify_path
        env.update(env_overrides)
        return subprocess.run(
            ["/bin/bash", str(VERIFY_SCRIPT)],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )

    def test_v2_pair_verifies_the_alias(self) -> None:
        result = self.run_verifier()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("opencode and opencode2 agree", result.stdout)

    def test_a_valid_v2_command_passes_without_the_optional_alias(self) -> None:
        (self.bin / "opencode2").unlink()
        result = self.run_verifier()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("OpenCode opencode v2.0.16", result.stdout)

    def test_v1_output_is_rejected(self) -> None:
        result = self.run_verifier(
            DXT_FAKE_OPENCODE_VERSION="opencode v1.18.32",
            DXT_FAKE_OPENCODE2_VERSION="opencode v1.18.32",
        )
        self.assertNotEqual(result.returncode, 0)

    def test_mismatched_aliases_are_rejected(self) -> None:
        result = self.run_verifier(
            DXT_FAKE_OPENCODE_VERSION="opencode v2.0.16",
            DXT_FAKE_OPENCODE2_VERSION="opencode v1.18.32",
        )
        self.assertNotEqual(result.returncode, 0)


@SKIP_ON_WINDOWS
class BakedConfigurationContract(unittest.TestCase):
    """The image and its lifecycle wiring keep the documented shape."""

    def test_devcontainer_json_mounts_the_workspace_and_defers_bash_env(self) -> None:
        text = DEVCONTAINER.read_text(encoding="utf-8")
        for expected in (
            '"workspaceMount": "source=${localWorkspaceFolder},target=/workspaces/package,'
            'type=bind,consistency=consistent"',
            '"workspaceFolder": "/workspaces/package"',
            '"UV_PROJECT_ENVIRONMENT": '
            '"${localEnv:UV_PROJECT_ENVIRONMENT:/workspaces/package/tooling~/.venv-container-linux}"',
            '"postAttachCommand": "bash .devcontainer/post-start.sh --attach"',
            '"waitFor": "postCreateCommand"',
        ):
            with self.subTest(expected=expected):
                self.assertIn(expected, text)
        self.assertNotIn('"BASH_ENV"', text, "devcontainer must not inject credentials into every Bash subprocess")

    def test_post_start_delegates_to_the_typed_flow(self) -> None:
        shim = POST_START.read_text(encoding="utf-8")
        self.assertIn("exec python3", shim, "post-start.sh must stay a thin shim over the typed entry")
        self.assertIn("post_start.py", shim)
        text = POST_START_PY.read_text(encoding="utf-8")
        for expected in (
            '"BASH_ENV"',
            '"ai-backends.sh"',
            '"configure", "--offline"',
            '"service", "status"',
            '"service", "restart"',
            "Unauthorized|Authorization header|401|forbidden",
        ):
            with self.subTest(expected=expected):
                self.assertIn(expected, text)

    def test_installer_queries_the_registry_without_sort_v(self) -> None:
        text = INSTALLER_PY.read_text(encoding="utf-8")
        self.assertIn('"version", "--json"', text)
        self.assertNotIn("sort -V", text, "the npm range parser still sorts package-name-prefixed output")

    def test_dockerfile_verifies_opencode_twice_without_the_prefix_expression(self) -> None:
        text = DOCKERFILE.read_text(encoding="utf-8")
        self.assertIn("WORKDIR /workspaces/package", text)
        self.assertIn("COPY .devcontainer/verify-opencode.sh /tmp/verify-opencode.sh", text)
        self.assertEqual(
            line_count(text, "bash /tmp/verify-opencode.sh"),
            2,
            "the verifier must run at create and at attach",
        )
        self.assertIsNone(
            re.search(r"(?:^|\s)2\.", text, re.MULTILINE),
            "the pre-fix OpenCode version expression remains",
        )
        # The live regex is the guard the retired fixed-string grep never
        # was: any "2." version pin in the Dockerfile fails this suite.


@SKIP_ON_WINDOWS
class EnvExportParsingContract(unittest.TestCase):
    """The autoload parses `ai-backends.sh env` output as data, never sourced."""

    @classmethod
    def setUpClass(cls) -> None:
        spec = importlib.util.spec_from_file_location("dxt_post_start", POST_START_PY)
        assert spec is not None and spec.loader is not None
        cls.post_start = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.post_start)

    def parse(self, text: str) -> Dict[str, str]:
        environment: Dict[str, str] = {}
        self.post_start.apply_env_exports(text, environment)
        return environment

    def test_single_quoted_values_apply_with_escaped_quotes_decoded(self) -> None:
        self.assertEqual(self.parse("export KEY='a'\\''b'\n"), {"KEY": "a'b"})

    def test_a_multi_line_value_is_skipped_whole(self) -> None:
        # The producer wraps a newline-bearing value across lines; applying
        # the first fragment would corrupt the credential, so the record is
        # refused in full.
        self.assertEqual(self.parse("export GOOD='a'\nexport BAD='b'\\''\nc'\n"), {"GOOD": "a"})

    def test_non_export_lines_are_ignored(self) -> None:
        self.assertEqual(self.parse("# comment\nnope=1\nexport OK='v'\n"), {"OK": "v"})


@SKIP_ON_WINDOWS
class RunOutputTimeoutContract(unittest.TestCase):
    """run_output keeps what a probe printed before its deadline, like bash
    command substitution kept what `timeout` did not stop in time."""

    @classmethod
    def setUpClass(cls) -> None:
        spec = importlib.util.spec_from_file_location("dxt_post_start", POST_START_PY)
        assert spec is not None and spec.loader is not None
        cls.post_start = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.post_start)

    def write_stub(self, stdout_line: str, stderr_line: str) -> Path:
        stub = Path(tempfile.mkdtemp(prefix="dxt-run-output-")) / "stub.sh"
        self.addCleanup(shutil.rmtree, stub.parent, True)
        write_script(
            stub,
            "#!/usr/bin/env bash\n"
            f"printf '%s\\n' '{stdout_line}'\n"
            f"printf '%s\\n' '{stderr_line}' >&2\n"
            "sleep 30\n",
        )
        return stub

    def test_stdout_printed_before_the_deadline_is_returned(self) -> None:
        stub = self.write_stub("http://127.0.0.1:49374", "ignored: stderr discarded")
        result = self.post_start.run_output([str(stub)], timeout=1, merge_stderr=False)
        self.assertEqual(result, "http://127.0.0.1:49374", "a status URL printed before the deadline was discarded")

    def test_stderr_merged_output_printed_before_the_deadline_is_returned(self) -> None:
        stub = self.write_stub("ignored: stdout merged", "✗ unity-mcp failed: Unauthorized")
        result = self.post_start.run_output([str(stub)], timeout=1, merge_stderr=True)
        self.assertEqual(
            result,
            "ignored: stdout merged\n✗ unity-mcp failed: Unauthorized",
            "a rejection printed before the deadline was discarded",
        )

    def test_a_missing_binary_is_empty(self) -> None:
        self.assertEqual(self.post_start.run_output(["dxt-definitely-missing-binary"], timeout=1, merge_stderr=True), "")


@SKIP_ON_WINDOWS
class NpmRangeResolverContract(unittest.TestCase):
    """Exercise the real resolver against a stubbed registry."""

    @classmethod
    def setUpClass(cls) -> None:
        spec = importlib.util.spec_from_file_location("dxt_install_agent_clis", INSTALLER_PY)
        assert spec is not None and spec.loader is not None
        cls.installer = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cls.installer)

    def resolve(self, fake_view: str) -> str:
        registry = Path(tempfile.mkdtemp(prefix="dxt-registry-"))
        self.addCleanup(shutil.rmtree, registry, True)
        write_script(registry / "npm", FAKE_NPM_REGISTRY)
        env = sanitize_env()
        env["PATH"] = f"{registry}:/usr/bin:/bin"
        env["DXT_FAKE_NPM_VIEW"] = fake_view
        saved_path = os.environ.get("PATH")
        saved_view = os.environ.get("DXT_FAKE_NPM_VIEW")
        os.environ["PATH"] = env["PATH"]
        os.environ["DXT_FAKE_NPM_VIEW"] = fake_view
        try:
            return self.installer.resolve_latest_version("@opencode/cli@2")
        finally:
            if saved_path is None:
                os.environ.pop("PATH", None)
            else:
                os.environ["PATH"] = saved_path
            if saved_view is None:
                os.environ.pop("DXT_FAKE_NPM_VIEW", None)
            else:
                os.environ["DXT_FAKE_NPM_VIEW"] = saved_view

    def test_unordered_versions_resolve_to_the_highest(self) -> None:
        # `npm view <range> version` returns an array whose entries are not in
        # version order, and an exact spec returns a bare string.
        self.assertEqual(self.resolve('["2.0.0","2.0.16","2.0.15"]'), "2.0.16")

    def test_exact_spec_passes_through(self) -> None:
        self.assertEqual(self.resolve('"2.0.16"'), "2.0.16")


@SKIP_ON_WINDOWS
class InstallerDelegatorContract(unittest.TestCase):
    """The tracked entry point must run the refresh through its delegator."""

    def test_empty_registry_fails_closed_with_every_refresh_counted(self) -> None:
        work = Path(tempfile.mkdtemp(prefix="dxt-delegator-"))
        self.addCleanup(shutil.rmtree, work, True)
        delegation = work / "delegation"
        delegation.mkdir()
        write_script(delegation / "npm", STUB_NPM_OK)
        # The PATH holds only the delegation directory: a python3 symlink
        # serves the delegator, the stub's /bin/bash shebang needs nothing
        # else, and no real CLI can leak a different answer.
        for command in ("python3", "dirname"):
            resolved = shutil.which(command)
            assert resolved is not None, command
            (delegation / command).symlink_to(resolved)
        env = sanitize_env()
        env.update(
            {
                "PATH": str(delegation),
                "HOME": str(work / "home"),
                "TMPDIR": str(work),
                "NPM_CONFIG_PREFIX": "",
                "BASH_ENV": "",
            }
        )
        (work / "home").mkdir()
        result = subprocess.run(
            ["/bin/bash", str(INSTALLER)],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )
        combined = result.stdout + result.stderr
        # An empty registry with no installed CLIs fails closed with every
        # refresh counted (four packages plus the OpenCode v2 gate).
        self.assertIn("5 agent CLI refresh(es) failed", combined)


@SKIP_ON_WINDOWS
class LifecycleContract(unittest.TestCase):
    """post-start mints, normalizes, and rotates credentials; failures stop it."""

    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="dxt-lifecycle-"))
        self.addCleanup(shutil.rmtree, self.root, True)
        self.devcontainer = self.root / ".devcontainer"
        self.devcontainer.mkdir()
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.home = self.root / "home"
        self.home.mkdir()
        (self.root / "tooling~/scripts/mcp").mkdir(parents=True)
        (self.root / "tooling~/node_modules").mkdir()
        for name in (
            POST_START.name,
            POST_START_PY.name,
            "env-autoload.sh",
            "install-env-autoload.sh",
            "install_env_autoload.py",
            VERIFY_SCRIPT.name,
            "verify_opencode.py",
            "ai-backends.sh",
        ):
            shutil.copy2(REPO_ROOT / ".devcontainer" / name, self.devcontainer / name)
        write_script(self.devcontainer / "cache-contract.sh", STUB_CACHE_CONTRACT)
        write_script(self.devcontainer / "install-agent-clis.sh", STUB_INSTALLER_OK)
        write_script(self.bin / "node", STUB_NODE)
        write_script(self.bin / "opencode", STUB_OPENCODE)
        (self.home / ".bashrc").touch()
        (self.home / ".profile").touch()
        (self.root / "package.json").write_text("", encoding="utf-8")

    def calls(self) -> List[str]:
        calls = self.root / "opencode-calls.txt"
        if not calls.exists():
            return []
        return calls.read_text(encoding="utf-8").splitlines()

    def service_tokens(self) -> str:
        return (self.root / "service-token.txt").read_text(encoding="utf-8")

    def seed(self, *lines: str) -> None:
        (self.root / ".env.local").write_text("".join(f"{line}\n" for line in lines), encoding="utf-8")

    def run_lifecycle(self, *args: str, **env_overrides: str) -> subprocess.CompletedProcess[str]:
        env = sanitize_env()
        env.update(
            {
                "DXT_FAKE_MCP_FAILURE": env_overrides.pop("mcp_failure", "0"),
                "DXT_FAKE_MCP_STDERR": env_overrides.pop("mcp_stderr", "0"),
                "DXT_FAKE_MCP_MULTIROW": env_overrides.pop("mcp_multirow", "0"),
                "DXT_FAKE_OTHER_MCP_FAILURE": env_overrides.pop("other_failure", "0"),
                "HOME": str(self.home),
                "WORKSPACE_FOLDER": str(self.root),
                "PATH": f"{self.bin}:{LIFECYCLE_PATH}",
                **env_overrides,
            }
        )
        return subprocess.run(
            ["/bin/bash", str(self.devcontainer / "post-start.sh"), *args],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )

    def test_first_run_mints_credentials_and_reaches_the_service_twice(self) -> None:
        result = self.run_lifecycle()
        self.assertEqual(result.returncode, 0, result.stderr)
        # The first run also spawns the background refresh, which rewrites the
        # same service-token.txt. Wait for it so the assertions never race it.
        deadline = time.monotonic() + 30
        while self.calls().count("service restart") < 2:
            if time.monotonic() >= deadline:
                self.fail(f"the background OpenCode refresh never finished ({len(self.calls())} call(s) seen)")
            time.sleep(0.2)
        self.assertIn("UNITY=generated-token-123", self.service_tokens(), "post-start passes the newly generated bearer token to OpenCode")
        self.assertIn("GITHUB=github-file-key", self.service_tokens(), "post-start normalizes the GitHub alias")
        self.assertIn("ZAI=zai-file-key", self.service_tokens(), "post-start normalizes the Z.AI alias")

    def test_attach_passes_the_github_mcp_alias_through(self) -> None:
        self.seed("GITHUB_MCP_PAT=github-mcp-key")
        result = self.run_lifecycle("--attach")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("GITHUB=github-mcp-key", self.service_tokens(), "post-start normalizes the GITHUB_MCP_PAT alias")

    def test_env_exports_the_supplying_alias_alongside_the_canonical_name(self) -> None:
        # `env` must export a credential under the alias that supplied it as
        # well as the canonical name: generated OpenCode configs reference the
        # alias ({env:GITHUB_MCP_PAT}, {env:Z_AI_API_KEY}), and a {env:NAME}
        # lookup resolves only when that exact name is exported.
        self.seed("GITHUB_MCP_PAT=github-mcp-key", "Z_AI_API_KEY=zai-underscore-key")
        env = sanitize_env(CREDENTIAL_NAMES)
        env["AI_BACKENDS_REPO_ROOT"] = str(self.root)
        result = subprocess.run(
            ["/bin/bash", str(self.devcontainer / "ai-backends.sh"), "env"],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )
        self.assertEqual(result.returncode, 0, result.stderr)
        for expected in (
            "export GITHUB_TOKEN='github-mcp-key'",
            "export GITHUB_MCP_PAT='github-mcp-key'",
            "export ZAI_API_KEY='zai-underscore-key'",
            "export Z_AI_API_KEY='zai-underscore-key'",
        ):
            with self.subTest(expected=expected):
                self.assertIn(expected, result.stdout)

    def test_attach_refreshes_a_rotated_token(self) -> None:
        self.seed(
            "UNITY_MCP_BEARER_TOKEN=rotated-token",
            "Z_AI_API_KEY=zai-file-key",
            "GITHUB_PERSONAL_ACCESS_TOKEN=github-file-key",
        )
        result = self.run_lifecycle("--attach")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("UNITY=rotated-token", self.service_tokens(), "post-attach refreshes the OpenCode service token")

    def test_unauthorized_unity_service_is_rejected_and_stopped(self) -> None:
        result = self.run_lifecycle("--attach", mcp_failure="1")
        self.assertIn("rejected the Unity credentials", result.stderr)
        self.assertIn("service stop", self.calls(), "post-attach did not stop the unusable OpenCode service")

    def test_optional_server_failure_keeps_the_healthy_service(self) -> None:
        # Another server failing must never stop a healthy service (issue: a
        # global Unauthorized match in the readiness probe).
        result = self.run_lifecycle("--attach", other_failure="1")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn("service stop", self.calls(), "an unauthorized optional server stopped a healthy OpenCode service")
        self.assertNotIn("rejected the Unity credentials", result.stderr, "an unauthorized optional server was reported as a Unity credential failure")
        self.assertIn(f"DXT_WORKSPACE_ROOT='{self.root}'", (self.home / ".bashrc").read_text(encoding="utf-8"), "post-attach did not migrate the interactive autoload block")

    def run_autoload_installer(self) -> subprocess.CompletedProcess[str]:
        env = sanitize_env()
        env["HOME"] = str(self.home)
        env["PATH"] = f"{self.bin}:{LIFECYCLE_PATH}"
        return subprocess.run(
            [
                "/bin/bash",
                "-c",
                'source "$1"; install_env_local_autoload "$2"',
                "_",
                str(self.devcontainer / "install-env-autoload.sh"),
                str(self.root),
            ],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )

    def write_rc(self, *lines: str) -> None:
        (self.home / ".bashrc").write_text("".join(f"{line}\n" for line in lines), encoding="utf-8")

    def test_unterminated_autoload_marker_fails_and_keeps_the_tail(self) -> None:
        self.write_rc(
            "export EDITOR=code",
            "# >>> dxcommandterminal .env.local autoload >>>",
            f"DXT_WORKSPACE_ROOT='{self.root}'",
            'alias ll="ls -la"',
        )
        result = self.run_autoload_installer()
        self.assertNotEqual(result.returncode, 0, "an unterminated autoload block was reported as installed")
        self.assertIn('alias ll="ls -la"', (self.home / ".bashrc").read_text(encoding="utf-8").splitlines(), "an unterminated autoload block truncated the rest of the rc file")

    def test_reversed_autoload_marker_fails_and_keeps_the_tail(self) -> None:
        self.write_rc(
            "export KEEPME=1",
            "# <<< dxcommandterminal .env.local autoload <<<",
            "# >>> dxcommandterminal .env.local autoload >>>",
            f"DXT_WORKSPACE_ROOT='{self.root}'",
        )
        result = self.run_autoload_installer()
        self.assertNotEqual(result.returncode, 0, "a reversed autoload block was reported as installed")
        self.assertIn("export KEEPME=1", (self.home / ".bashrc").read_text(encoding="utf-8").splitlines(), "a reversed autoload block truncated the rest of the rc file")

    def test_autoload_block_lands_after_the_interactive_guard_without_leaking(self) -> None:
        self.write_rc(
            "# If not running interactively, do not do anything",
            "case $- in",
            "    *i*) ;;",
            "      *) return;;",
            "esac",
        )
        result = self.run_autoload_installer()
        self.assertEqual(result.returncode, 0, "a clean rc file could not receive the autoload block")
        self.assertIn("# >>> dxcommandterminal .env.local autoload >>>", (self.home / ".bashrc").read_text(encoding="utf-8").splitlines(), "the autoload block was not installed")
        # The inner shell must expand $HOME and the credentials, not this one.
        self.seed("Z_AI_API_KEY=zai-file-key", "GITHUB_TOKEN=github-key")
        env = sanitize_env((*CREDENTIAL_NAMES, "UNITY_MCP_BEARER_TOKEN", "DXT_WORKSPACE_ROOT"))
        env["HOME"] = str(self.home)
        env["PATH"] = f"{self.bin}:{LIFECYCLE_PATH}"
        leaked = subprocess.run(
            ["/bin/bash", "-c", '. "$HOME/.bashrc"; env | grep -cE "^(ZAI_API_KEY|GITHUB_TOKEN)=" || true'],
            capture_output=True,
            text=True,
            env=env,
            timeout=120,
        )
        self.assertEqual(leaked.stdout.strip(), "0", f"a non-interactive .bashrc source imported {leaked.stdout.strip()} credential(s)")

    def test_attach_rejects_a_v1_only_opencode(self) -> None:
        # The gate this change exists for: a v1-only image must not reach the
        # attach point. The stub reports v1 through the same commands the
        # verifier reads.
        write_script(self.bin / "opencode", STUB_OPENCODE_V1)
        result = self.run_lifecycle("--attach")
        self.assertNotEqual(result.returncode, 0, "post-start attached with a v1-only OpenCode")
        self.assertIn("OpenCode v2 is required", result.stderr)

    def test_a_legacy_bash_env_loader_never_reaches_the_service(self) -> None:
        # post-start strips BASH_ENV before any child runs: a global loader
        # would hand every agent subprocess the file's contents.
        self.seed("UNITY_MCP_BEARER_TOKEN=rotated-token")
        result = self.run_lifecycle("--attach", BASH_ENV="/tmp/leak-loader.sh")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(
            "BASH_ENV=<unset>",
            self.service_tokens(),
            "a legacy BASH_ENV loader reached the restarted OpenCode service",
        )

    def test_the_autoload_disable_flag_keeps_credentials_from_the_service(self) -> None:
        # The same flag agent launchers set to scrub inherited secrets must
        # also keep the checkout's own credentials out of this lifecycle.
        self.seed("UNITY_MCP_BEARER_TOKEN=scrubbed-token")
        result = self.run_lifecycle("--attach", DXT_ENV_AUTOLOAD_DISABLED="1")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(
            "UNITY=<unset>",
            self.service_tokens(),
            "DXT_ENV_AUTOLOAD_DISABLED did not keep the seeded credential from the service",
        )

    def test_a_rejection_reported_on_stderr_still_stops_the_service(self) -> None:
        # The probe reads the mcp list the way bash command substitution did:
        # stderr merged, output used whatever the exit code was.
        result = self.run_lifecycle("--attach", mcp_stderr="1")
        self.assertIn("rejected the Unity credentials", result.stderr)
        self.assertIn("service stop", self.calls(), "a stderr-reported rejection did not stop the OpenCode service")

    def test_a_rejected_row_beside_a_healthy_one_still_stops_the_service(self) -> None:
        # Every unity-mcp row decides the probe, not just the first: a stale
        # duplicate entry running on rejected credentials must be stopped.
        result = self.run_lifecycle("--attach", mcp_multirow="1")
        self.assertIn("rejected the Unity credentials", result.stderr)
        self.assertIn("service stop", self.calls(), "a rejected duplicate unity-mcp row did not stop the service")

    def test_prepare_stops_after_the_readiness_gate(self) -> None:
        # updateContentCommand: repair, configure, and verify v2 - but never
        # touch the service, the rc files, or the background refresh.
        result = self.run_lifecycle("--prepare")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertNotIn("service restart", self.calls(), "--prepare restarted the OpenCode service")
        self.assertNotIn(
            "# >>> dxcommandterminal .env.local autoload >>>",
            (self.home / ".bashrc").read_text(encoding="utf-8"),
            "--prepare installed the interactive autoload block",
        )

    def test_an_unknown_mode_is_refused(self) -> None:
        result = self.run_lifecycle("--frobnicate")
        self.assertEqual(result.returncode, 2, "an unknown lifecycle mode was not refused")


if __name__ == "__main__":
    unittest.main()
