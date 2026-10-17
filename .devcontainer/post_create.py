"""The devcontainer post-create bootstrap, typed (issue #220).

Owns the once-per-container order: repair the cache mounts, point the npm
global prefix at ~/.local so installs never need sudo, hold the OpenCode v2
readiness gate, then bootstrap the workspace - .NET tools, npm dependencies,
offline MCP configuration under the shared bounded lock, the agent launchers,
the .env.local autoload block, git safe.directory, and pre-commit - and print
the welcome panel.

The shared contracts stay in their single implementations and are invoked
through their tracked entries: `cache-contract.sh` (the mount contract shared
with post_start.py and the validate scripts), `install-agent-clis.sh`,
`verify-opencode.sh`, `install-env-autoload.sh` (the shared, bounded autoload
lock), and `ai-backends.sh install`.

One deliberate fidelity fix: the bash summary piped the CLI version probes
through `head -n1`, whose exit status masked a missing CLI and left an empty
cell; the typed panel answers N/A there, which is what the piped
`|| echo 'N/A'` always meant.
"""
from __future__ import annotations

import os
import subprocess
import sys
import time
from pathlib import Path
from typing import IO, Any, Callable, Optional

try:
    import fcntl
except ImportError:  # pragma: no cover - non-POSIX hosts never take locks
    fcntl = None  # type: ignore[assignment]

PREFIX = "[post-create]"
MCP_LOCK_WAIT_SECONDS = 180.0

# Child probes (the installer, the verifier, the version checks) write through
# the same pipe this panel uses; bash flushed per line, so keep that order
# when stdout is a pipe (a terminal already line-buffers).
_reconfigure = getattr(sys.stdout, "reconfigure", None)
if callable(_reconfigure):
    try:
        _reconfigure(line_buffering=True)
    except (OSError, ValueError):  # pragma: no cover - a closed or exotic stdout
        pass

# Colored only on a terminal, like `[[ -t 1 ]]` picked in bash.
if sys.stdout.isatty():
    BLUE = "\033[0;34m"
    GREEN = "\033[0;32m"
    YELLOW = "\033[1;33m"
    RED = "\033[0;31m"
    CYAN = "\033[0;36m"
    BOLD = "\033[1m"
    NC = "\033[0m"
else:
    BLUE = GREEN = YELLOW = RED = CYAN = BOLD = NC = ""

# The $HOME literal is intentional: it must be written unexpanded so it
# expands in the user's shell at source time.
RC_PATH_LINE = 'export PATH="$HOME/.local/bin:$PATH"'
RC_PATH_COMMENT = "# Ensure npm user-global binaries are available"


def script_dir() -> Path:
    return Path(__file__).resolve().parent


def tmp_dir() -> Path:
    return Path(os.environ.get("TMPDIR") or "/tmp")


def log_info(message: str) -> None:
    print(f"{BLUE}{PREFIX}{NC} {message}")


def log_success(message: str) -> None:
    print(f"{GREEN}{PREFIX} ✓{NC} {message}")


def log_warning(message: str) -> None:
    print(f"{YELLOW}{PREFIX} ⚠{NC} {message}")


def log_error(message: str) -> None:
    print(f"{RED}{PREFIX} ✗{NC} {message}", file=sys.stderr)


def log_header(title: str) -> None:
    print()
    print(f"{BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━{NC}")
    print(f"{BLUE}  {BOLD}{title}{NC}")
    print(f"{BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━{NC}")


def run_visible(command: list[str]) -> int:
    """Run with the lifecycle's own stdio; return a non-zero code on failure."""
    try:
        return subprocess.run(command).returncode
    except OSError:
        return 127


def run_optional(label: str, command: Optional[list[str]] = None, step: Optional[Callable[[], bool]] = None) -> bool:
    """Report the labeled step, then keep going whatever it answered."""
    log_info(label)
    if command is not None:
        ok = run_visible(command) == 0
    else:
        assert step is not None
        ok = step()
    if ok:
        log_success(f"{label} completed")
    else:
        log_warning(f"{label} failed (continuing)")
    return ok


def acquire_lock(path: Path, wait: float = MCP_LOCK_WAIT_SECONDS) -> Optional[IO[Any]]:
    """Hold an exclusive lock for up to `wait` seconds, mirroring flock -w.

    Returns the open handle (release by closing it) or None on timeout. When
    POSIX locks are unavailable the flow runs unlocked, like the bash
    command -v guard.
    """
    if fcntl is None:
        return open(path, "a+b")
    handle = open(path, "a+b")
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


def repair_cache_permissions(devcontainer: Path, workspace: Path) -> bool:
    """The sourced call was fatal under set -e; the caller stops on False."""
    contract = devcontainer / "cache-contract.sh"
    completed = subprocess.run(
        ["bash", "-c", '. "$1"; cache_contract_repair_permissions', "_", str(contract)],
        cwd=workspace,
    )
    return completed.returncode == 0


def ensure_path_line(rc_file: Path) -> bool:
    """Append the user-global PATH export once; an absent rc file is fine."""
    if not rc_file.is_file():
        return True
    content = rc_file.read_text(encoding="utf-8")
    if RC_PATH_LINE in content.splitlines():
        return True
    with open(rc_file, "a", encoding="utf-8") as rc:
        rc.write(f"\n{RC_PATH_COMMENT}\n{RC_PATH_LINE}\n")
    return True


def configure_npm_prefix() -> bool:
    """Point npm's global prefix at ~/.local; the read-back is the verdict.

    Inside the bash `configure_npm_prefix || {{...}}` guard set -e was
    suppressed, so a failed `npm config set` fell through to this read-back
    and failed here; the typed flow keeps that shape.
    """
    log_header("Configuring npm Global Prefix (sudo-free installs)")
    home = os.environ.get("HOME", "")
    try:
        os.makedirs(f"{home}/.local/bin", exist_ok=True)
    except OSError:
        pass  # The prefix steps below surface an unwritable home.
    log_info(f"Setting npm prefix to {home}/.local")
    run_visible(["npm", "config", "set", "prefix", f"{home}/.local"])
    read_back = subprocess.run(
        ["npm", "config", "get", "prefix"],
        stdout=subprocess.PIPE,
        text=True,
    )
    current = read_back.stdout.rstrip("\n")
    expected = f"{home}/.local"
    if current != expected:
        log_error(f"npm prefix is '{current}', expected '{expected}'")
        return False
    log_success(f"npm prefix configured: {current} (plain npm install -g needs no sudo)")
    os.environ["PATH"] = f"{home}/.local/bin:{os.environ.get('PATH', '')}"
    run_optional("Ensuring ~/.bashrc exports ~/.local/bin", step=lambda: ensure_path_line(Path(home) / ".bashrc"))
    run_optional("Ensuring ~/.profile exports ~/.local/bin", step=lambda: ensure_path_line(Path(home) / ".profile"))
    return True


def install_agent_clis(devcontainer: Path) -> bool:
    """Refresh the CLIs, then fail readiness unless a usable v2 remains."""
    installer = devcontainer / "install-agent-clis.sh"
    verifier = devcontainer / "verify-opencode.sh"
    refresh_status = 0
    if not installer.is_file():
        log_warning("install-agent-clis.sh not found; checking the baked OpenCode CLI")
    else:
        refresh_status = run_visible(["bash", str(installer)])
    if run_visible(["bash", str(verifier)]) == 0:
        if refresh_status != 0:
            log_warning("Agent refresh failed; the baked OpenCode v2 command is usable")
        return True
    log_error("No usable OpenCode v2 command is available")
    return False


def configure_agent_mcps(devcontainer: Path, workspace: Path) -> bool:
    """Offline configure under the lock post-start shares; overlap-safe."""
    mcp_script = devcontainer.parent / "tooling~" / "scripts" / "mcp" / "unity-mcp.mjs"
    lock = acquire_lock(tmp_dir() / "dxt-mcp-configure.lock")
    if lock is None:
        print(f"{PREFIX} Timed out waiting for the MCP configure lock.", file=sys.stderr)
        return False
    try:
        # The lock guards the configure run only, like `flock node` did.
        return run_visible(["node", str(mcp_script), "configure", "--offline"]) == 0
    finally:
        lock.close()


def install_autoload_block(devcontainer: Path, workspace: str) -> bool:
    completed = subprocess.run(
        [
            "bash",
            "-c",
            'source "$1"; install_env_local_autoload "$2"',
            "_",
            str(devcontainer / "install-env-autoload.sh"),
            workspace,
        ]
    )
    return completed.returncode == 0


def validate_workspace(workspace: Path) -> None:
    """Warn-only inventory of the package layout; never fails the flow."""
    log_header("Validating Workspace (Unity UPM package)")
    checks_passed = 0
    checks_total = 0
    for item in ("package.json", "Runtime", "Editor", "Tests"):
        checks_total += 1
        if (workspace / item).exists():
            log_success(f"{item} found")
            checks_passed += 1
        else:
            log_warning(f"{item} not found")
    print()
    log_info(f"Workspace validation: {checks_passed}/{checks_total} checks passed")


def version_probe(command: list[str]) -> str:
    """First stdout line, or N/A when the probe is missing or fails."""
    try:
        completed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    except OSError:
        return "N/A"
    if completed.returncode != 0:
        return "N/A"
    output = completed.stdout.rstrip("\n")
    return output.split("\n", 1)[0] if output else "N/A"


def print_summary(workspace: Path) -> None:
    log_header("DxCommandTerminal Dev Environment Ready")
    print()
    print(f"  {CYAN}{BOLD}Project:{NC}        DxCommandTerminal (Unity UPM package, min 2021.3)")
    print(f"  {CYAN}{BOLD}Workspace:{NC}      {workspace}")
    print(f"  {CYAN}{BOLD}Unity project:{NC}  host-side (see UNITY_PROJECT_PATH in .env.local)")
    print()
    print(f"  {BOLD}Toolchain{NC}")
    print(f"    .NET SDK:      {version_probe(['dotnet', '--version'])}")
    print(f"    PowerShell:    {version_probe(['pwsh', '-NoProfile', '-Command', '$PSVersionTable.PSVersion.ToString()'])}")
    print(f"    Node.js:       {version_probe(['node', '--version'])}")
    print(f"    claude:        {version_probe(['claude', '--version'])}")
    print(f"    codex:         {version_probe(['codex', '--version'])}")
    print(f"    opencode:      {version_probe(['opencode', '--version'])}")
    print(f"    nanocoder:     {version_probe(['nanocoder', '--version'])}")
    print()
    print(f"  {BOLD}Quick Commands{NC}")
    print(f"    {CYAN}claude-zai{NC} / {CYAN}codex-zai{NC}            # Agents on the Z.AI subscription")
    print(f"    {CYAN}claude-openrouter{NC} / {CYAN}codex-openrouter{NC}  # Agents on any OpenRouter model")
    print(f"    {CYAN}npm run unity:mcp:probe{NC}          # Check the host Unity bridge")
    print(f"    {CYAN}npm run unity:capture{NC}            # Capture Unity editor/game state")
    print(f"    {CYAN}npm run unity:mcp:configure{NC}      # Re-sync MCP config after .env.local edits")
    print(f"    {CYAN}dotnet tool run csharpier format .{NC}  # Format C# sources")
    print(f"    {CYAN}npm test{NC}                          # Node tooling tests")
    print(f"    {CYAN}pre-commit run --all-files{NC}        # Repo hooks")
    print()
    log_success("Environment setup complete!")
    print()


def main() -> int:
    devcontainer = script_dir()
    workspace = devcontainer.parent
    os.chdir(workspace)
    # Do not pass a legacy global BASH_ENV loader to child agents.
    os.environ.pop("BASH_ENV", None)

    print()
    print(f"{BLUE}╔══════════════════════════════════════════════════════════════════╗{NC}")
    print(f"{BLUE}║        {BOLD}DxCommandTerminal Devcontainer - Post-Create Bootstrap{NC}{BLUE}       ║{NC}")
    print(f"{BLUE}╚══════════════════════════════════════════════════════════════════╝{NC}")
    print()

    # Step 1: fix volume permissions FIRST so later writes (npm, dotnet, pip)
    # land in a writable home directory.
    log_header("Repairing Cache Mount Permissions")
    if not repair_cache_permissions(devcontainer, workspace):
        log_error("Volume permission repair failed; cannot continue safely.")
        return 1
    log_success("Cache mounts are user-writable (no sudo needed anywhere)")

    if not configure_npm_prefix():
        log_error("npm prefix configuration failed")
        return 1

    # Step 2: refresh the agent CLIs, but fail readiness if no usable v2 exists.
    log_header("Refreshing Agent CLIs (claude, codex, opencode, nanocoder)")
    if not install_agent_clis(devcontainer):
        log_error("OpenCode v2 readiness check failed")
        return 1

    # Step 3: workspace bootstrap. `containerWorkspaceFolder` is the mounted
    # path VS Code advertises; fall back to this checkout when absent.
    log_header("Bootstrapping Workspace")
    run_optional("Restoring .NET local tools (CSharpier)", ["dotnet", "tool", "restore"])
    # npm install reuses the persistent modules tree; npm ci would remove it.
    # The npm project lives under tooling~/ so Unity never imports node_modules.
    run_optional(
        "Installing workspace npm dependencies",
        ["npm", "--prefix", "tooling~", "install", "--prefer-online", "--no-audit", "--no-fund"],
    )
    run_optional("Configuring MCP servers for every agent front end", step=lambda: configure_agent_mcps(devcontainer, workspace))
    run_optional("Installing Z.AI and OpenRouter agent launchers", ["bash", str(devcontainer / "ai-backends.sh"), "install"])
    lifecycle_target = os.environ.get("containerWorkspaceFolder") or str(workspace)
    run_optional("Installing .env.local shell autoload", step=lambda: install_autoload_block(devcontainer, lifecycle_target))
    run_optional("Configuring git safe.directory", ["git", "config", "--global", "--add", "safe.directory", lifecycle_target])
    run_optional("Installing pre-commit hooks", ["pre-commit", "install"])

    # Step 4: validate environment (warn-only).
    validate_workspace(workspace)

    print_summary(workspace)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
