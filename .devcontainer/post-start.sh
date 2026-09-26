#!/usr/bin/env bash
# shellcheck shell=bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKSPACE_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${WORKSPACE_DIR}"
# Load .env.local even when the lifecycle runner does not source an rc file.
# shellcheck source=.devcontainer/env-autoload.sh
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/env-autoload.sh"
# Do not pass a legacy global BASH_ENV loader to child agents.
unset BASH_ENV
# shellcheck source=.devcontainer/install-env-autoload.sh
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/install-env-autoload.sh"
# shellcheck source=.devcontainer/cache-contract.sh
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/cache-contract.sh"
if [[ "${1:-}" != "--attach" ]]; then
    cache_contract_repair_permissions
fi
export PATH="${HOME}/.local/bin:${PATH}"
export NPM_CONFIG_PREFIX="${HOME}/.local"

# Configuration is local and works with the image's baked dependencies before npm
# install. Finish it before clients start, without probing a sleeping host.
mcp_script="${SCRIPT_DIR}/../tooling~/scripts/mcp/unity-mcp.mjs"
mcp_lock="${TMPDIR:-/tmp}/dxt-mcp-configure.lock"
if command -v flock >/dev/null 2>&1; then
    flock -w 30 "${mcp_lock}" node "${mcp_script}" configure --offline
else
    node "${mcp_script}" configure --offline
fi
# configure may have minted UNITY_MCP_BEARER_TOKEN into .env.local. Reload
# before restarting OpenCode so its service inherits the value.
# shellcheck source=.devcontainer/env-autoload.sh
# shellcheck disable=SC1091
source "${SCRIPT_DIR}/env-autoload.sh"

installer="${SCRIPT_DIR}/install-agent-clis.sh"
verifier="${SCRIPT_DIR}/verify-opencode.sh"

# A pre-v2 image must not reach the attach point. The normal image already has
# v2, so this is a no-op; an old container gets one bounded repair attempt.
ensure_opencode_v2() {
    if bash "${verifier}"; then
        return 0
    fi
    echo "[post-start] No usable OpenCode v2; attempting the user-scoped repair." >&2
    bash "${installer}" || true
    bash "${verifier}"
}

# OpenCode's background service can outlive a shell and miss credentials loaded
# by the lifecycle environment. Restart an existing service from this shell.
refresh_opencode_service() (
    local status output
    if command -v flock >/dev/null 2>&1; then
        exec 8>"${TMPDIR:-/tmp}/dxt-opencode-service.lock"
        if ! flock -w 30 8; then
            echo "[post-start] Timed out waiting for another OpenCode service refresh." >&2
            return 1
        fi
    fi
    status="$(opencode service status 2>/dev/null || true)"
    [[ "${status}" == http://* || "${status}" == https://* ]] || return 0
    if ! opencode service restart >/dev/null 2>&1; then
        echo "[post-start] OpenCode service restart failed; stopping the stale service." >&2
        opencode service stop >/dev/null 2>&1 || true
        return 1
    fi

    # The service command returns before its project config and MCP connections
    # finish initializing. Wait briefly so the first terminal command sees them.
    for attempt in 1 2 3 4 5; do
        output="$(timeout 3 opencode mcp list 2>&1 || true)"
        if [[ -z "${output}" || "${output}" == *"No MCP servers configured"* ]]; then
            sleep 1
            continue
        fi
        if [[ "${output}" == *"unity-mcp"* \
            && ( "${output}" == *"Unauthorized"* || "${output}" == *"Authorization header"* ) ]]; then
            sleep 1
            continue
        fi
        if [[ "${output}" == *"unity-mcp"* ]]; then
            return 0
        fi
        sleep 1
    done
    echo "[post-start] OpenCode service did not load its MCP config after ${attempt} attempts." >&2
    opencode service stop >/dev/null 2>&1 || true
    return 1
)

ensure_opencode_v2
[[ "${1:-}" == "--prepare" ]] && exit 0
install_env_local_autoload "${WORKSPACE_DIR}"
if ! refresh_opencode_service; then
    echo "[post-start] OpenCode service will retry on the next launch." >&2
fi
[[ "${1:-}" == "--attach" ]] && exit 0

# Every start checks the latest tags in the background. Image copies remain
# usable while offline, and the installer serializes overlapping starts. If it
# replaces OpenCode, refresh the service again after the install completes.
(
    bash "${installer}" || true
    # Pick up a credential file edited while the installer was running.
    # shellcheck source=.devcontainer/env-autoload.sh
    # shellcheck disable=SC1091
    source "${SCRIPT_DIR}/env-autoload.sh"
    refresh_opencode_service || true
) </dev/null >"${TMPDIR:-/tmp}/dxt-agent-cli-refresh.log" 2>&1 &
