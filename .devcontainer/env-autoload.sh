# shellcheck shell=bash
# Sourced from interactive shell rc files and trusted lifecycle scripts. It loads
# the checkout's managed credentials for native agent CLIs and MCP tools. The
# caller may set DXT_WORKSPACE_ROOT; otherwise this script derives the repository
# root from its own path. .env.local is parsed as data, never sourced. Process
# environment values win, and a missing checkout is a no-op.
if [ "${DXT_ENV_AUTOLOAD_DISABLED:-}" = "1" ]; then
    # Keep this flag set so descendant Bash processes cannot re-import secrets.
    # shellcheck disable=SC2317
    return 0 2>/dev/null || exit 0
fi
if [ "${DXT_ENV_AUTOLOAD_ACTIVE:-}" = "1" ]; then
    # shellcheck disable=SC2317
    return 0 2>/dev/null || exit 0
fi
export DXT_ENV_AUTOLOAD_ACTIVE=1

dxt_workspace_root="${DXT_WORKSPACE_ROOT:-}"
if [ -z "${dxt_workspace_root}" ]; then
    dxt_workspace_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]:-}")/.." && pwd)"
fi
if [ -n "${dxt_workspace_root}" ] \
    && [ -f "${dxt_workspace_root}/.devcontainer/ai-backends.sh" ]; then
    dxt_env_exports="$(AI_BACKENDS_REPO_ROOT="${dxt_workspace_root}" \
        bash "${dxt_workspace_root}/.devcontainer/ai-backends.sh" env 2>/dev/null)" || dxt_env_exports=""
    if [ -n "$dxt_env_exports" ]; then
        eval "${dxt_env_exports}"
    fi
    unset -v dxt_env_exports
fi
unset -v dxt_workspace_root DXT_WORKSPACE_ROOT DXT_ENV_AUTOLOAD_ACTIVE
