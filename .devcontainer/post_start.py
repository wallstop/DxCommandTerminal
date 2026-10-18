"""The devcontainer post-start lifecycle, typed (issue #220).

Owns the order and the boundaries of the post-start flow: load the checkout's
managed credentials, repair the cache mounts, configure MCP clients offline
under a bounded lock, hold the OpenCode v2 readiness gate, refresh an existing
OpenCode service from the credential-bearing environment, and hand the agent
CLI refresh to a detached background job.

The shared contracts stay in their single implementations and are invoked
through their tracked entries: `ai-backends.sh env` (the one producer of the
credential environment), `cache-contract.sh` (the mount contract shared with
post-create.sh and the validate scripts), `install-env-autoload.sh` (the
shared, bounded autoload lock), `verify-opencode.sh`, and
`install-agent-clis.sh`.

Modes:
    (no arguments)       the full postStartCommand flow
    --prepare            updateContentCommand: stop after the readiness gate
    --attach             postAttachCommand: skip cache repair and the
                         background refresh
    --background-refresh private: the body of the detached background job
"""
from __future__ import annotations

import os
import re
import subprocess
import sys
import time
from pathlib import Path
from typing import IO, Any, MutableMapping, Optional

try:
    import fcntl
except ImportError:  # pragma: no cover - non-POSIX hosts never take locks
    fcntl = None  # type: ignore[assignment]

PREFIX = "[post-start]"
LOCK_WAIT_SECONDS = 30.0
SERVICE_POLL_ROUNDS = 5

# `ai-backends.sh env` emits `export KEY='value'` lines with embedded quotes
# escaped the way eval needs them (`'\''`). Parsed as data, never sourced. A
# record that does not rebuild into its own line is refused whole, so a
# newline-bearing value can never apply as a corrupt fragment.
_ENV_EXPORT = re.compile(r"^export ([A-Za-z_][A-Za-z0-9_]*)='(.*)'$", re.ASCII)
_QUOTE = "'"
_ESCAPED_QUOTE = "'\\''"

# Only the unity-mcp row decides the service probe; another server may fail
# freely (a whole-output match once stopped a healthy service).
_SERVICE_REJECTION = "Unauthorized|Authorization header|401|forbidden"


def script_dir() -> Path:
    return Path(__file__).resolve().parent


def tmp_dir() -> Path:
    return Path(os.environ.get("TMPDIR") or "/tmp")


def acquire_lock(path: Path, wait: float = LOCK_WAIT_SECONDS) -> Optional[IO[Any]]:
    """Hold an exclusive lock for up to `wait` seconds, mirroring flock -w.

    Returns the open handle (release by closing it) or None when the lock
    cannot be taken - a timeout or an unusable lock path, like a failing
    flock. When POSIX locks are unavailable the flow runs unlocked, like the
    bash guard.
    """
    try:
        handle = open(path, "a+b")
    except OSError:
        return None
    if fcntl is None:
        # No POSIX locks off Linux; run unlocked, like the bash command -v guard.
        return handle
    deadline = time.monotonic() + wait
    while True:
        try:
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            return handle
        except OSError:
            if time.monotonic() >= deadline:
                handle.close()
                return None
            time.sleep(0.2)


def apply_env_exports(text: str, environment: MutableMapping[str, str]) -> None:
    # Split on newlines only (bash `read -r` semantics): str.splitlines also
    # breaks on exotic Unicode boundaries and would fragment a value that
    # contains one into an unbuildable record.
    for line in text.split("\n"):
        match = _ENV_EXPORT.match(line)
        if match is None:
            continue
        key, value = match.group(1), match.group(2).replace(_ESCAPED_QUOTE, _QUOTE)
        if f"export {key}='{value.replace(_QUOTE, _ESCAPED_QUOTE)}'" != line:
            continue
        environment[key] = value


def autoload(workspace: Path) -> None:
    """Load .env.local through `ai-backends.sh env`; process env wins.

    Mirrors env-autoload.sh: the disable flag keeps credentials away from
    every descendant (agent launchers set it to scrub inherited secrets), the
    active flag breaks re-entrancy, and both flags are left as the shell
    version left them.
    """
    if os.environ.get("DXT_ENV_AUTOLOAD_DISABLED") == "1":
        return
    if os.environ.get("DXT_ENV_AUTOLOAD_ACTIVE") == "1":
        return
    os.environ["DXT_ENV_AUTOLOAD_ACTIVE"] = "1"
    root = os.environ.get("DXT_WORKSPACE_ROOT") or str(workspace)
    producer = Path(root) / ".devcontainer" / "ai-backends.sh"
    if root and producer.is_file():
        completed = subprocess.run(
            ["bash", str(producer), "env"],
            env={**os.environ, "AI_BACKENDS_REPO_ROOT": root},
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True,
        )
        if completed.returncode == 0:
            apply_env_exports(completed.stdout, os.environ)
    os.environ.pop("DXT_ENV_AUTOLOAD_ACTIVE", None)
    os.environ.pop("DXT_WORKSPACE_ROOT", None)


def partial_output(expired: subprocess.TimeoutExpired) -> str:
    """What a killed command printed before its deadline. Bash command
    substitution kept it, and POSIX `subprocess.run` attaches it to the
    exception as bytes even under text mode."""
    output = expired.stdout or ""
    if isinstance(output, bytes):
        output = output.decode("utf-8", errors="replace")
    return output.rstrip("\n")


def run_output(command: list[str], timeout: float, merge_stderr: bool) -> str:
    """Capture output like bash command substitution: the exit code is not a
    verdict, a merged stderr is part of what the CLI had to say, and output
    written before the deadline still counts."""
    try:
        completed = subprocess.run(
            command,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT if merge_stderr else subprocess.DEVNULL,
            timeout=timeout,
            text=True,
        )
    except subprocess.TimeoutExpired as expired:
        return partial_output(expired)
    except OSError:
        return ""
    return completed.stdout.rstrip("\n")


def run_silent(command: list[str], timeout: float) -> int:
    """Run with output discarded, like `>/dev/null 2>&1`; return the code."""
    try:
        return subprocess.run(
            command,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=timeout,
        ).returncode
    except subprocess.TimeoutExpired:
        return 124
    except OSError:
        return 127


def run_visible(command: list[str], timeout: Optional[float] = None) -> int:
    """Run with the lifecycle's own stdio; return a non-zero code on failure."""
    try:
        return subprocess.run(command, timeout=timeout).returncode
    except subprocess.TimeoutExpired:
        return 124
    except OSError:
        return 127


def repair_cache_permissions(devcontainer: Path, workspace: Path) -> None:
    """Fatal on failure, as the sourced call was under set -e."""
    contract = devcontainer / "cache-contract.sh"
    completed = subprocess.run(
        ["bash", "-c", '. "$1"; cache_contract_repair_permissions', "_", str(contract)],
        cwd=workspace,
    )
    if completed.returncode != 0:
        sys.exit(completed.returncode)


def configure_mcp(devcontainer: Path) -> None:
    """Configuration must finish before clients start; failure stops the flow."""
    mcp_script = devcontainer.parent / "tooling~" / "scripts" / "mcp" / "unity-mcp.mjs"
    lock = acquire_lock(tmp_dir() / "dxt-mcp-configure.lock")
    if lock is None:
        print(f"{PREFIX} Timed out waiting for the MCP configure lock.", file=sys.stderr)
        sys.exit(1)
    try:
        # The lock guards the configure run only, like `flock node` did.
        code = run_visible(["node", str(mcp_script), "configure", "--offline"])
    finally:
        lock.close()
    if code != 0:
        sys.exit(code)


def ensure_opencode_v2(devcontainer: Path) -> None:
    """A pre-v2 image must not reach the attach point; one bounded repair."""
    verifier = devcontainer / "verify-opencode.sh"
    if run_visible(["bash", str(verifier)]) == 0:
        return
    print(f"{PREFIX} No usable OpenCode v2; attempting the user-scoped repair.", file=sys.stderr)
    run_visible(["bash", str(devcontainer / "install-agent-clis.sh")])
    code = run_visible(["bash", str(verifier)])
    if code != 0:
        sys.exit(code)


def install_autoload_block(devcontainer: Path, workspace: Path) -> None:
    completed = subprocess.run(
        [
            "bash",
            "-c",
            'source "$1"; install_env_local_autoload "$2"',
            "_",
            str(devcontainer / "install-env-autoload.sh"),
            str(workspace),
        ]
    )
    if completed.returncode != 0:
        print(f"{PREFIX} Could not update the shell autoload block.", file=sys.stderr)


def refresh_opencode_service() -> bool:
    """Restart an outliving service so it inherits the current credentials.

    Only a positively identified rejection destroys state: an unrecognized or
    empty probe is reported and the service is left running.
    """
    lock = acquire_lock(tmp_dir() / "dxt-opencode-service.lock")
    if lock is None:
        print(f"{PREFIX} Timed out waiting for another OpenCode service refresh.", file=sys.stderr)
        return False
    status = run_output(["opencode", "service", "status"], 20, merge_stderr=False)
    if not status.startswith(("http://", "https://")):
        # No running service: nothing to refresh, and no failure to report.
        return True
    if run_silent(["opencode", "service", "restart"], 120) != 0:
        print(f"{PREFIX} OpenCode service restart failed; stopping the stale service.", file=sys.stderr)
        run_silent(["opencode", "service", "stop"], 20)
        return False

    # The service command returns before its project config and MCP
    # connections finish initializing; wait briefly for them.
    rejected = False
    for _ in range(SERVICE_POLL_ROUNDS):
        output = run_output(["opencode", "mcp", "list"], 15, merge_stderr=True)
        if not output or "No MCP servers configured" in output:
            time.sleep(1)
            continue
        # Only unity-mcp rows decide this; another server may fail freely,
        # and every one of the rows must be healthy (a stale duplicate entry
        # can keep running on rejected credentials otherwise).
        unity_lines = [line for line in output.splitlines() if "unity-mcp" in line]
        if not unity_lines:
            print(f"{PREFIX} OpenCode service listed no unity-mcp entry; leaving it running.", file=sys.stderr)
            return True
        if any(re.search(_SERVICE_REJECTION, line) is not None for line in unity_lines):
            rejected = True
            time.sleep(1)
            continue
        return True
    if rejected:
        print(f"{PREFIX} OpenCode service rejected the Unity credentials; stopping it.", file=sys.stderr)
        run_silent(["opencode", "service", "stop"], 20)
        return False
    print(f"{PREFIX} OpenCode service did not load its MCP config in time; leaving it running.", file=sys.stderr)
    return False


def background_refresh(devcontainer: Path, workspace: Path) -> None:
    """Every start checks the latest tags in the background; best effort."""
    run_visible(["bash", str(devcontainer / "install-agent-clis.sh")])
    # Pick up a credential file edited while the installer was running.
    autoload(workspace)
    refresh_opencode_service()


def main(argv: list[str]) -> int:
    mode = argv[0] if argv else ""
    if mode not in ("", "--prepare", "--attach", "--background-refresh"):
        print("Usage: post-start.sh [--prepare|--attach]", file=sys.stderr)
        return 2
    devcontainer = script_dir()
    workspace = devcontainer.parent
    os.chdir(workspace)
    # Do not pass a legacy global BASH_ENV loader to child agents.
    os.environ.pop("BASH_ENV", None)
    if mode == "--background-refresh":
        background_refresh(devcontainer, workspace)
        return 0
    autoload(workspace)
    if mode != "--attach":
        repair_cache_permissions(devcontainer, workspace)
    home = os.environ.get("HOME", "")
    os.environ["PATH"] = f"{home}/.local/bin:{os.environ.get('PATH', '')}"
    os.environ["NPM_CONFIG_PREFIX"] = f"{home}/.local"
    configure_mcp(devcontainer)
    # configure may have minted UNITY_MCP_BEARER_TOKEN into .env.local. Reload
    # before restarting OpenCode so its service inherits the value.
    autoload(workspace)
    ensure_opencode_v2(devcontainer)
    if mode == "--prepare":
        return 0
    install_autoload_block(devcontainer, workspace)
    if not refresh_opencode_service():
        print(f"{PREFIX} OpenCode service will retry on the next launch.", file=sys.stderr)
    if mode == "--attach":
        return 0
    # The installer serializes overlapping starts; the service is refreshed
    # again afterwards because the install may have replaced the binary under
    # the running service.
    log_path = tmp_dir() / "dxt-agent-cli-refresh.log"
    with open(log_path, "ab") as log:
        subprocess.Popen(
            [sys.executable, str(script_dir() / "post_start.py"), "--background-refresh"],
            stdin=subprocess.DEVNULL,
            stdout=log,
            stderr=log,
            start_new_session=True,
            cwd=workspace,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
