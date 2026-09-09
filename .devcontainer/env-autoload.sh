# shellcheck shell=bash
# Sourced from interactive shell rc files (the block is installed by
# .devcontainer/post-create.sh) so native agent CLIs - claude, codex, opencode
# (via the ZHIPU_API_KEY mirror), nanocoder, gh, and MCP tooling - resolve the
# checkout's credentials automatically. The caller sets DXT_WORKSPACE_ROOT to
# the checkout root before sourcing. .env.local is parsed as data, never
# sourced; process environment values win, and a missing checkout or file is a
# silent no-op.
if [ -n "${DXT_WORKSPACE_ROOT:-}" ] \
    && [ -f "${DXT_WORKSPACE_ROOT}/.devcontainer/ai-backends.sh" ]; then
    dxt_env_exports="$(AI_BACKENDS_REPO_ROOT="${DXT_WORKSPACE_ROOT}" \
        bash "${DXT_WORKSPACE_ROOT}/.devcontainer/ai-backends.sh" env 2>/dev/null)" || dxt_env_exports=""
    if [ -n "${dxt_env_exports}" ]; then
        eval "${dxt_env_exports}"
    fi
    unset -v dxt_env_exports
fi
unset -v DXT_WORKSPACE_ROOT
