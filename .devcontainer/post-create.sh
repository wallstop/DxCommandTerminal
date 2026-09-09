#!/usr/bin/env bash
# shellcheck shell=bash
# =============================================================================
# DxCommandTerminal Devcontainer - Post-Create Bootstrap
# =============================================================================
# Runs once after the devcontainer is created. Repairs cache ownership, installs
# tooling at the user level (never sudo), configures MCP clients for every agent
# front end, installs the Z.AI backend launchers, and prints a welcome panel.
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKSPACE_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
LOG_PREFIX="[post-create]"

if [[ -t 1 ]]; then
    BLUE='\033[0;34m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    RED='\033[0;31m'
    CYAN='\033[0;36m'
    BOLD='\033[1m'
    NC='\033[0m'
else
    BLUE='' GREEN='' YELLOW='' RED='' CYAN='' BOLD='' NC=''
fi

log_info() { echo -e "${BLUE}${LOG_PREFIX}${NC} $1"; }
log_success() { echo -e "${GREEN}${LOG_PREFIX} ✓${NC} $1"; }
log_warning() { echo -e "${YELLOW}${LOG_PREFIX} ⚠${NC} $1"; }
log_error() { echo -e "${RED}${LOG_PREFIX} ✗${NC} $1" >&2; }

log_header() {
    echo ""
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${BLUE}  ${BOLD}$1${NC}"
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
}

run_optional() {
    local label="$1"
    shift
    log_info "$label"
    if "$@"; then
        log_success "$label completed"
    else
        log_warning "$label failed (continuing)"
    fi
}

ensure_path_line() {
    local rc_file="$1"
    # The $HOME literal is intentional: it must be written unexpanded so it
    # expands in the user's shell at source time.
    # shellcheck disable=SC2016
    local path_line='export PATH="$HOME/.local/bin:$PATH"'
    if [[ ! -f "$rc_file" ]]; then
        return
    fi
    if ! grep -Fqx "$path_line" "$rc_file"; then
        {
            echo ""
            echo "# Ensure npm user-global binaries are available"
            echo "$path_line"
        } >> "$rc_file"
    fi
}

# `waitFor: updateContentCommand` lets post-create and post-start overlap, and
# both configure the MCP clients. Without a lock, two runs starting from an
# .env.local with no bearer token would each mint one and write different values
# into the generated client configs.
MCP_CONFIGURE_LOCK="${TMPDIR:-/tmp}/dxt-mcp-configure.lock"

# Appends a guarded block to interactive rc files so every new shell exports
# the checkout's managed credentials (from the environment or .env.local,
# parsed as data) before the user launches claude/codex/opencode/nanocoder.
# The block sources .devcontainer/env-autoload.sh from the checkout, so
# updates to the loader apply to new shells without touching rc files again.
ensure_env_local_autoload() {
    local workspace_root="$1"
    # Escape single quotes so the literal path is written quoted and
    # unexpanded into the rc file, mirroring ensure_path_line.
    local root_quoted="${workspace_root//\'/\'\\\'\'}"
    local marker="# >>> dxcommandterminal .env.local autoload >>>"
    local block rc_file
    block=$(cat <<EOF
${marker}
DXT_WORKSPACE_ROOT='${root_quoted}'
if [ -f '${root_quoted}/.devcontainer/env-autoload.sh' ]; then
    . '${root_quoted}/.devcontainer/env-autoload.sh'
fi
# <<< dxcommandterminal .env.local autoload <<<
EOF
)
    for rc_file in "$HOME/.bashrc" "$HOME/.profile"; do
        [[ -f "$rc_file" ]] || continue
        if ! grep -Fqx "${marker}" "$rc_file"; then
            {
                echo ""
                printf '%s\n' "$block"
            } >> "$rc_file"
        fi
    done
}

configure_agent_mcps() {
    local configure=(node "${WORKSPACE_DIR}/tooling~/tooling~/scripts/mcp/unity-mcp.mjs" configure --offline)
    if command -v flock >/dev/null 2>&1; then
        flock -w 180 "${MCP_CONFIGURE_LOCK}" "${configure[@]}"
        return
    fi
    log_warning "flock is unavailable; configuring without an overlap lock"
    "${configure[@]}"
}

install_agent_clis() {
    local installer="${SCRIPT_DIR}/install-agent-clis.sh"
    if [[ ! -f "${installer}" ]]; then
        log_warning "install-agent-clis.sh not found; skipping agent CLI refresh"
        return 1
    fi
    if bash "${installer}"; then
        return 0
    fi
    # Keep post-create resilient when offline or the npm registry is unreachable.
    log_warning "One or more agent CLI refreshes failed (continuing)"
    return 1
}

configure_npm_prefix() {
    log_header "Configuring npm Global Prefix (sudo-free installs)"
    mkdir -p "$HOME/.local/bin"
    log_info "Setting npm prefix to $HOME/.local"
    npm config set prefix "$HOME/.local"
    local current_prefix
    current_prefix="$(npm config get prefix)"
    if [[ "$current_prefix" != "$HOME/.local" ]]; then
        log_error "npm prefix is '$current_prefix', expected '$HOME/.local'"
        return 1
    fi
    log_success "npm prefix configured: $current_prefix (plain npm install -g needs no sudo)"
    export PATH="$HOME/.local/bin:$PATH"
    run_optional "Ensuring ~/.bashrc exports ~/.local/bin" ensure_path_line "$HOME/.bashrc"
    run_optional "Ensuring ~/.profile exports ~/.local/bin" ensure_path_line "$HOME/.profile"
}

validate_workspace() {
    log_header "Validating Workspace (Unity UPM package)"
    cd "${WORKSPACE_DIR}"
    local checks_passed=0
    local checks_total=0
    local item
    for item in package.json Runtime Editor Tests; do
        ((++checks_total))
        if [[ -e "${item}" ]]; then
            log_success "${item} found"
            ((++checks_passed))
        else
            log_warning "${item} not found"
        fi
    done
    echo ""
    log_info "Workspace validation: ${checks_passed}/${checks_total} checks passed"
    return 0
}

print_summary() {
    log_header "DxCommandTerminal Dev Environment Ready"

    echo ""
    echo -e "  ${CYAN}${BOLD}Project:${NC}        DxCommandTerminal (Unity UPM package, min 2021.3)"
    echo -e "  ${CYAN}${BOLD}Workspace:${NC}      ${WORKSPACE_DIR}"
    echo -e "  ${CYAN}${BOLD}Unity project:${NC}  host-side (see UNITY_PROJECT_PATH in .env.local)"
    echo ""
    echo -e "  ${BOLD}Toolchain${NC}"
    echo -e "    .NET SDK:      $(dotnet --version 2>/dev/null || echo 'N/A')"
    echo -e "    PowerShell:    $(pwsh -NoProfile -Command '$PSVersionTable.PSVersion.ToString()' 2>/dev/null || echo 'N/A')"
    echo -e "    Node.js:       $(node --version 2>/dev/null || echo 'N/A')"
    echo -e "    claude:        $(claude --version 2>/dev/null | head -n1 || echo 'N/A')"
    echo -e "    codex:         $(codex --version 2>/dev/null | head -n1 || echo 'N/A')"
    echo -e "    opencode:      $(opencode --version 2>/dev/null | head -n1 || echo 'N/A')"
    echo -e "    nanocoder:     $(nanocoder --version 2>/dev/null | head -n1 || echo 'N/A')"
    echo ""
    echo -e "  ${BOLD}Quick Commands${NC}"
    echo -e "    ${CYAN}claude-zai${NC} / ${CYAN}codex-zai${NC}            # Agents on the Z.AI subscription"
    echo -e "    ${CYAN}claude-openrouter${NC} / ${CYAN}codex-openrouter${NC}  # Agents on any OpenRouter model"
    echo -e "    ${CYAN}npm run unity:mcp:probe${NC}          # Check the host Unity bridge"
    echo -e "    ${CYAN}npm run unity:capture${NC}            # Capture Unity editor/game state"
    echo -e "    ${CYAN}npm run unity:mcp:configure${NC}      # Re-sync MCP config after .env.local edits"
    echo -e "    ${CYAN}dotnet tool run csharpier format .${NC}  # Format C# sources"
    echo -e "    ${CYAN}npm test${NC}                          # Node tooling tests"
    echo -e "    ${CYAN}pre-commit run --all-files${NC}        # Repo hooks"
    echo ""
    log_success "Environment setup complete!"
    echo ""
}

main() {
    local exit_code=0

    echo ""
    echo -e "${BLUE}╔══════════════════════════════════════════════════════════════════╗${NC}"
    echo -e "${BLUE}║        ${BOLD}DxCommandTerminal Devcontainer - Post-Create Bootstrap${NC}${BLUE}       ║${NC}"
    echo -e "${BLUE}╚══════════════════════════════════════════════════════════════════╝${NC}"
    echo ""

    # Step 1: fix volume permissions FIRST so later writes (npm, dotnet, pip)
    # land in a writable home directory.
    log_header "Repairing Cache Mount Permissions"
    # shellcheck source=.devcontainer/cache-contract.sh
    if source "${SCRIPT_DIR}/cache-contract.sh" && cache_contract_repair_permissions; then
        log_success "Cache mounts are user-writable (no sudo needed anywhere)"
    else
        log_error "Volume permission repair failed; cannot continue safely."
        return 1
    fi

    configure_npm_prefix || { log_error "npm prefix configuration failed"; return 1; }

    # Step 2: refresh the agent CLIs to npm's latest tags (foreground on create).
    log_header "Refreshing Agent CLIs (claude, codex, opencode, nanocoder)"
    install_agent_clis || true

    # Step 3: workspace bootstrap.
    log_header "Bootstrapping Workspace"
    cd "${WORKSPACE_DIR}"
    run_optional "Restoring .NET local tools (CSharpier)" dotnet tool restore
    # npm install reuses the persistent modules tree; npm ci would remove it.
    # The npm project lives under tooling~/ so Unity never imports node_modules.
    run_optional "Installing workspace npm dependencies" npm --prefix tooling~ install --prefer-offline --no-audit --no-fund
    run_optional "Configuring MCP servers for every agent front end" configure_agent_mcps
    run_optional "Installing Z.AI and OpenRouter agent launchers" \
        bash "${SCRIPT_DIR}/ai-backends.sh" install
    run_optional "Installing .env.local shell autoload" \
        ensure_env_local_autoload "${containerWorkspaceFolder:-${WORKSPACE_DIR}}"
    run_optional "Configuring git safe.directory" \
        git config --global --add safe.directory "${containerWorkspaceFolder:-${WORKSPACE_DIR}}"
    run_optional "Installing pre-commit hooks" pre-commit install

    # Step 4: validate environment (warn-only).
    validate_workspace || { log_error "Workspace validation failed"; exit_code=1; }

    print_summary
    return "${exit_code}"
}

if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then
    main "$@"
fi
