#!/usr/bin/env bash
# Install and launch isolated Z.ai and OpenRouter backends without changing native
# Codex or Claude defaults.

set -euo pipefail

readonly PROFILE_NAME="devcontainer-zai"
readonly ZAI_RESPONSES_URL="https://api.z.ai/api/v1"
readonly ZAI_ANTHROPIC_URL="https://api.z.ai/api/anthropic"
readonly OPENROUTER_PROFILE_NAME="devcontainer-openrouter"
readonly OPENROUTER_CHAT_URL="https://openrouter.ai/api/v1"
readonly OPENROUTER_ANTHROPIC_URL="https://openrouter.ai/api"
# Current OpenRouter catalog defaults (verified against /api/v1/models); every
# slot is overridable through environment variables. See print_help.
readonly OPENROUTER_DEFAULT_MODEL="openai/gpt-5.6-sol"
readonly OPENROUTER_CLAUDE_FABLE_MODEL="anthropic/claude-fable-5.1"
readonly OPENROUTER_CLAUDE_OPUS_MODEL="anthropic/claude-opus-5"
readonly OPENROUTER_CLAUDE_SONNET_MODEL="anthropic/claude-sonnet-5"
readonly OPENROUTER_CLAUDE_HAIKU_MODEL="anthropic/claude-haiku-4.5"
readonly OPENROUTER_CLAUDE_SUBAGENT_MODEL="anthropic/claude-sonnet-5"

die() {
    printf '[ai-backends] ERROR: %s\n' "$*" >&2
    exit 1
}

resolve_action() {
    local invoked_as
    invoked_as="$(basename "$0")"
    case "${invoked_as}" in
        codex-zai|claude-zai|codex-openrouter|claude-openrouter)
            printf '%s\n' "${invoked_as}"
            ;;
        *)
            printf '%s\n' "${1:-help}"
            ;;
    esac
}

# Reads one KEY= entry from the checkout's .env.local, parsed as data and never
# sourced. AI_BACKENDS_REPO_ROOT overrides the repository root; by default it is
# derived from this script's real location. Prints the value; returns 1 when the
# file is absent or the key is missing/empty.
env_local_value() {
    local key="$1" root env_file value
    if [ -n "${AI_BACKENDS_REPO_ROOT:-}" ]; then
        root="${AI_BACKENDS_REPO_ROOT}"
    else
        root="$(cd "$(dirname "$(realpath "${BASH_SOURCE[0]}")")/.." && pwd)"
    fi
    env_file="${root}/.env.local"
    [ -f "${env_file}" ] || return 1
    local line candidate bom
    bom=$'\xEF\xBB\xBF'
    value=""
    while IFS= read -r line || [ -n "${line}" ]; do
        line="${line%$'\r'}"
        if [[ "${line}" == "${bom}"* ]]; then
            line="${line#"${bom}"}"
        fi
        if [[ "${line}" =~ ^[[:space:]]*(export[[:space:]]+)?${key}[[:space:]]*=(.*)$ ]]; then
            candidate="${BASH_REMATCH[2]}"
            candidate="${candidate#"${candidate%%[![:space:]]*}"}"
            candidate="${candidate%"${candidate##*[![:space:]]}"}"
        else
            continue
        fi
        value="${candidate}"
    done < "${env_file}"
    case "${value}" in
        \"*\")
            value="${value#\"}"
            value="${value%\"}"
            ;;
        \'*\')
            value="${value#\'}"
            value="${value%\'}"
            ;;
    esac
    [ -n "${value}" ] || return 1
    printf '%s' "${value}"
}

# Prints the first non-empty value for the given variable names. Documented
# precedence: every alias is checked in the process environment first (empty
# environment variables are ignored), then in .env.local in the same order.
find_credential() {
    local key value
    for key in "$@"; do
        value="$(printenv "${key}" || true)"
        if [ -n "${value}" ]; then
            printf '%s' "${value}"
            return 0
        fi
    done
    for key in "$@"; do
        if value="$(env_local_value "${key}")"; then
            printf '%s' "${value}"
            return 0
        fi
    done
    return 1
}

resolve_zai_key() {
    local zai_key
    if zai_key="$(find_credential ZAI_API_KEY Z_AI_API_KEY)"; then
        printf '%s' "${zai_key}"
        return
    fi
    die "Set ZAI_API_KEY (or Z_AI_API_KEY) in the environment or the checkout's .env.local before launching a Z.ai backend."
}

install_codex_profile() {
    local codex_home catalog_file catalog_path_toml catalog_tmp profile_file profile_tmp
    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    catalog_file="${codex_home}/${PROFILE_NAME}-models.json"
    profile_file="${codex_home}/${PROFILE_NAME}.config.toml"
    catalog_path_toml="${catalog_file//\\/\\\\}"
    catalog_path_toml="${catalog_path_toml//\"/\\\"}"

    mkdir -p "${codex_home}"
    chmod 700 "${codex_home}"

    catalog_tmp="$(mktemp "${codex_home}/.${PROFILE_NAME}-models.XXXXXX")"
    profile_tmp="$(mktemp "${codex_home}/.${PROFILE_NAME}-profile.XXXXXX")"
    trap 'rm -f "${catalog_tmp:-}" "${profile_tmp:-}"' RETURN

    # Model metadata follows Z.ai's current Codex Responses integration contract.
    cat >"${catalog_tmp}" <<'JSON'
{
  "models": [
    {
      "slug": "glm-5.3",
      "display_name": "glm-5.3",
      "description": "Z.ai flagship coding model",
      "default_reasoning_level": "max",
      "supported_reasoning_levels": [
        {
          "effort": "low",
          "description": "Light reasoning"
        },
        {
          "effort": "high",
          "description": "Enhanced reasoning"
        },
        {
          "effort": "max",
          "description": "Deep reasoning"
        }
      ],
      "shell_type": "shell_command",
      "visibility": "list",
      "supported_in_api": true,
      "priority": 0,
      "base_instructions": "",
      "supports_reasoning_summaries": true,
      "default_reasoning_summary": "none",
      "support_verbosity": false,
      "apply_patch_tool_type": "freeform",
      "truncation_policy": {
        "mode": "bytes",
        "limit": 10000
      },
      "context_window": 1048576,
      "max_context_window": 1048576,
      "effective_context_window_percent": 95,
      "supports_parallel_tool_calls": true,
      "experimental_supported_tools": [],
      "input_modalities": [
        "text"
      ]
    }
  ]
}
JSON

    cat >"${profile_tmp}" <<TOML
model_provider = "ZAI"
model = "glm-5.3"
model_reasoning_effort = "max"
model_catalog_json = "${catalog_path_toml}"

[model_providers.ZAI]
name = "ZAI"
base_url = "${ZAI_RESPONSES_URL}"
env_key = "ZAI_API_KEY"
wire_api = "responses"
request_max_retries = 4
stream_max_retries = 5
stream_idle_timeout_ms = 300000

[shell_environment_policy]
filters = { ZAI_API_KEY = "exclude", Z_AI_API_KEY = "exclude" }
TOML

    chmod 600 "${catalog_tmp}" "${profile_tmp}"
    mv "${catalog_tmp}" "${catalog_file}"
    mv "${profile_tmp}" "${profile_file}"
    trap - RETURN
}

install_launchers() {
    local bin_dir codex_command launcher script_path target
    if [ -n "${AI_BACKENDS_BIN_DIR:-}" ]; then
        bin_dir="${AI_BACKENDS_BIN_DIR}"
    elif case ":${PATH}:" in *":${HOME}/.local/bin:"*) true ;; *) false ;; esac; then
        bin_dir="${HOME}/.local/bin"
    else
        codex_command="$(command -v codex || true)"
        if [ -n "${codex_command}" ] && [ -w "$(dirname "${codex_command}")" ]; then
            bin_dir="$(dirname "${codex_command}")"
        else
            bin_dir="${HOME}/.local/bin"
        fi
    fi
    script_path="$(realpath "${BASH_SOURCE[0]}")"
    mkdir -p "${bin_dir}"

    for launcher in codex-zai claude-zai codex-openrouter claude-openrouter; do
        target="${bin_dir}/${launcher}"
        if [ -e "${target}" ] && [ ! -L "${target}" ]; then
            die "Refusing to replace non-symlink launcher: ${target}"
        fi
        ln -sfn "${script_path}" "${target}"
    done
}

install_backend_support() {
    install_codex_profile
    install_openrouter_codex_profile
    install_launchers
    printf '[ai-backends] Installed codex-zai, claude-zai, codex-openrouter, and claude-openrouter launchers.\n'
}

# OpenRouter resolves the key from the environment first, then from the checkout's
# .env.local (parsed as data, never sourced) via find_credential/env_local_value.
resolve_openrouter_key() {
    local openrouter_key
    if openrouter_key="$(find_credential OPENROUTER_API_KEY)"; then
        printf '%s' "${openrouter_key}"
        return
    fi
    die "Set OPENROUTER_API_KEY (environment or the checkout's .env.local) before launching an OpenRouter backend."
}

# Prevent a global BASH_ENV loader from restoring managed keys after the
# provider launcher applies its scrub policy.
prepare_scrubbed_agent_environment() {
    export DXT_ENV_AUTOLOAD_DISABLED=1
    export BASH_ENV=/dev/null
}

# Shared Claude sandbox handling. $1 is the resolved subprocess-scrub request
# (auto|0|1). Sets CLAUDE_SCRUB and, inside a container, CLAUDE_SANDBOX_SETTINGS.
claude_sandbox_prepare() {
    local container_mode scrub_request scrub
    container_mode="${AI_BACKENDS_CONTAINER_MODE:-auto}"
    case "${container_mode}" in
        auto)
            if [ -f /.dockerenv ] || [ -f /run/.containerenv ]; then
                container_mode=yes
            else
                container_mode=no
            fi
            ;;
        yes|no) ;;
        *) die "AI_BACKENDS_CONTAINER_MODE must be auto, yes, or no." ;;
    esac

    scrub_request="$1"
    case "${scrub_request}" in
        auto)
            if [ "${container_mode}" = "yes" ]; then
                # The devcontainer is already the isolation boundary. Claude's
                # additional Linux scrub sandbox still requires CLONE_NEWUSER
                # and mount operations that Docker's default profiles deny.
                scrub=0
            else
                scrub=1
            fi
            ;;
        0|1) scrub="${scrub_request}" ;;
        *) die "The subprocess env scrub must be auto, 0, or 1." ;;
    esac

    if [ "$(uname -s)" = "Linux" ] && [ "${scrub}" = "1" ]; then
        command -v bwrap >/dev/null 2>&1 \
            || die "bubblewrap is required for Claude subprocess isolation; install bubblewrap and socat."
        command -v socat >/dev/null 2>&1 \
            || die "socat is required for Claude sandbox networking; install bubblewrap and socat."
        bwrap --unshare-user --ro-bind / / --bind /proc /proc --dev /dev true \
            || die "Claude subprocess isolation cannot create its nested sandbox. Set the subprocess env scrub to 0 to rely on the outer devcontainer boundary."
    fi

    CLAUDE_SCRUB="${scrub}"
    CLAUDE_SANDBOX_SETTINGS=""
    if [ "${container_mode}" = "yes" ]; then
        # The outer devcontainer is the primary isolation boundary. Anthropic's
        # weaker mode still needs blocked namespaces, so keep the inner sandbox
        # off unless the caller explicitly opts into the scrubber and preflight.
        CLAUDE_SANDBOX_SETTINGS='{"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}}'
    fi
}

install_openrouter_codex_profile() {
    local codex_home profile_file profile_tmp
    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    profile_file="${codex_home}/${OPENROUTER_PROFILE_NAME}.config.toml"

    mkdir -p "${codex_home}"
    chmod 700 "${codex_home}"

    profile_tmp="$(mktemp "${codex_home}/.${OPENROUTER_PROFILE_NAME}-profile.XXXXXX")"
    trap 'rm -f "${profile_tmp:-}"' RETURN

    # OpenRouter exposes an OpenAI-compatible Responses API; modern Codex only
    # speaks Responses (wire_api = "chat" was removed in codex 0.15x). Command-
    # backed auth (not env_key) makes Codex fetch OpenRouter's live model
    # catalog, so non-OpenAI models get real context-window and reasoning
    # metadata instead of "Unknown model" fallbacks. The auth command inherits
    # the Codex process environment (shell_environment_policy does not apply to
    # it), so the launcher's export reaches it, while the filter below still
    # keeps the key away from agent-spawned subprocesses and stdio MCP servers.
    cat >"${profile_tmp}" <<TOML
model_provider = "OPENROUTER"
model = "${OPENROUTER_DEFAULT_MODEL}"

[model_providers.OPENROUTER]
name = "OPENROUTER"
base_url = "${OPENROUTER_CHAT_URL}"
wire_api = "responses"
request_max_retries = 4
stream_max_retries = 5
stream_idle_timeout_ms = 300000

[model_providers.OPENROUTER.auth]
command = "sh"
args = ["-c", "printf '%s' \"\${OPENROUTER_API_KEY}\""]

[shell_environment_policy]
filters = { OPENROUTER_API_KEY = "exclude" }
TOML

    chmod 600 "${profile_tmp}"
    mv "${profile_tmp}" "${profile_file}"
    trap - RETURN
}

launch_codex_zai() {
    local catalog_file codex_home model reasoning_effort zai_key
    command -v codex >/dev/null 2>&1 || die "codex is not installed."
    zai_key="$(resolve_zai_key)"
    export ZAI_API_KEY="${zai_key}"
    unset Z_AI_API_KEY ZHIPU_API_KEY

    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    catalog_file="${codex_home}/${PROFILE_NAME}-models.json"
    if [ ! -f "${codex_home}/${PROFILE_NAME}.config.toml" ] || [ ! -f "${catalog_file}" ]; then
        install_codex_profile
    fi

    model="${CODEX_ZAI_MODEL:-glm-5.3}"
    reasoning_effort="${CODEX_ZAI_REASONING_EFFORT:-max}"
    case "${reasoning_effort}" in
        low|high|max) ;;
        *) die "CODEX_ZAI_REASONING_EFFORT must be low, high, or max." ;;
    esac
    prepare_scrubbed_agent_environment
    exec codex \
        --profile "${PROFILE_NAME}" \
        --model "${model}" \
        --config 'model_provider="ZAI"' \
        --config "model_reasoning_effort=\"${reasoning_effort}\"" \
        --config "model_catalog_json=\"${catalog_file}\"" \
        "$@"
}

launch_claude_zai() {
    local config_dir timeout_ms zai_key
    local -a claude_args=()
    command -v claude >/dev/null 2>&1 || die "claude is not installed."
    zai_key="$(resolve_zai_key)"
    config_dir="${CLAUDE_ZAI_CONFIG_DIR:-${HOME}/.claude-zai}"
    # 3000000 matches the API_TIMEOUT_MS Z.ai publishes for GLM coding-plan
    # Claude Code setups; deep-reasoning requests on long contexts exceed
    # conservative defaults. Override with ZAI_API_TIMEOUT_MS.
    timeout_ms="${ZAI_API_TIMEOUT_MS:-3000000}"
    case "${timeout_ms}" in
        ''|*[!0-9]*) die "ZAI_API_TIMEOUT_MS must be a positive integer." ;;
        0) die "ZAI_API_TIMEOUT_MS must be greater than zero." ;;
    esac
    mkdir -p "${config_dir}"
    chmod 700 "${config_dir}"
    unset ZAI_API_KEY Z_AI_API_KEY ZHIPU_API_KEY
    prepare_scrubbed_agent_environment

    claude_sandbox_prepare "${CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB:-auto}"

    if [ -n "${CLAUDE_SANDBOX_SETTINGS}" ]; then
        claude_args+=(--settings "${CLAUDE_SANDBOX_SETTINGS}")
    fi

    unset ANTHROPIC_API_KEY \
        ANTHROPIC_MODEL \
        ANTHROPIC_DEFAULT_MODEL \
        ANTHROPIC_DEFAULT_HAIKU_MODEL \
        ANTHROPIC_DEFAULT_SONNET_MODEL \
        ANTHROPIC_DEFAULT_OPUS_MODEL \
        ANTHROPIC_SMALL_FAST_MODEL \
        CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST \
        CLAUDE_CODE_USE_ANTHROPIC_AWS \
        CLAUDE_CODE_USE_BEDROCK \
        CLAUDE_CODE_USE_VERTEX \
        CLAUDE_CODE_USE_FOUNDRY \
        CLAUDE_CODE_USE_MANTLE \
        CLAUDE_CODE_USE_GATEWAY
    export ANTHROPIC_AUTH_TOKEN="${zai_key}"
    export ANTHROPIC_BASE_URL="${ZAI_ANTHROPIC_URL}"
    export ANTHROPIC_DEFAULT_HAIKU_MODEL="${CLAUDE_ZAI_HAIKU_MODEL:-glm-5.3-flash[1m]}"
    export ANTHROPIC_DEFAULT_SONNET_MODEL="${CLAUDE_ZAI_SONNET_MODEL:-glm-5.3[1m]}"
    export ANTHROPIC_DEFAULT_OPUS_MODEL="${CLAUDE_ZAI_OPUS_MODEL:-glm-5.3[1m]}"
    export API_TIMEOUT_MS="${timeout_ms}"
    export CLAUDE_CODE_AUTO_COMPACT_WINDOW=1000000
    export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
    export CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST=1
    export CLAUDE_CODE_SUBPROCESS_ENV_SCRUB="${CLAUDE_SCRUB}"
    export CLAUDE_CONFIG_DIR="${config_dir}"
    exec claude ${claude_args[@]+"${claude_args[@]}"} "$@"
}

launch_codex_openrouter() {
    local codex_home model openrouter_key profile_file
    command -v codex >/dev/null 2>&1 || die "codex is not installed."
    openrouter_key="$(resolve_openrouter_key)"
    export OPENROUTER_API_KEY="${openrouter_key}"

    codex_home="${CODEX_HOME:-${HOME}/.codex}"
    profile_file="${codex_home}/${OPENROUTER_PROFILE_NAME}.config.toml"
    if [ ! -f "${profile_file}" ]; then
        install_openrouter_codex_profile
    fi

    model="${CODEX_OPENROUTER_MODEL:-${OPENROUTER_DEFAULT_MODEL}}"
    prepare_scrubbed_agent_environment
    exec codex \
        --profile "${OPENROUTER_PROFILE_NAME}" \
        --model "${model}" \
        --config 'model_provider="OPENROUTER"' \
        "$@"
}

launch_claude_openrouter() {
    local config_dir openrouter_key timeout_ms
    local -a claude_args=()
    command -v claude >/dev/null 2>&1 || die "claude is not installed."
    openrouter_key="$(resolve_openrouter_key)"
    config_dir="${CLAUDE_OPENROUTER_CONFIG_DIR:-${HOME}/.claude-openrouter}"
    timeout_ms="${OPENROUTER_API_TIMEOUT_MS:-300000}"
    case "${timeout_ms}" in
        ''|*[!0-9]*) die "OPENROUTER_API_TIMEOUT_MS must be a positive integer." ;;
        0) die "OPENROUTER_API_TIMEOUT_MS must be greater than zero." ;;
    esac
    mkdir -p "${config_dir}"
    chmod 700 "${config_dir}"
    unset OPENROUTER_API_KEY
    prepare_scrubbed_agent_environment

    claude_sandbox_prepare "${CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB:-auto}"

    if [ -n "${CLAUDE_SANDBOX_SETTINGS}" ]; then
        claude_args+=(--settings "${CLAUDE_SANDBOX_SETTINGS}")
    fi

    unset ANTHROPIC_API_KEY \
        ANTHROPIC_MODEL \
        ANTHROPIC_DEFAULT_MODEL \
        ANTHROPIC_DEFAULT_FABLE_MODEL \
        ANTHROPIC_DEFAULT_HAIKU_MODEL \
        ANTHROPIC_DEFAULT_SONNET_MODEL \
        ANTHROPIC_DEFAULT_OPUS_MODEL \
        ANTHROPIC_SMALL_FAST_MODEL \
        CLAUDE_CODE_SUBAGENT_MODEL \
        CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST \
        CLAUDE_CODE_USE_ANTHROPIC_AWS \
        CLAUDE_CODE_USE_BEDROCK \
        CLAUDE_CODE_USE_VERTEX \
        CLAUDE_CODE_USE_FOUNDRY \
        CLAUDE_CODE_USE_MANTLE \
        CLAUDE_CODE_USE_GATEWAY
    # OpenRouter's Anthropic skin authenticates with a bearer token; ANTHROPIC_API_KEY
    # must exist but be explicitly empty so Claude Code never falls back to Anthropic.
    export ANTHROPIC_AUTH_TOKEN="${openrouter_key}"
    export ANTHROPIC_API_KEY=""
    export ANTHROPIC_BASE_URL="${OPENROUTER_ANTHROPIC_URL}"
    export ANTHROPIC_DEFAULT_FABLE_MODEL="${CLAUDE_OPENROUTER_FABLE_MODEL:-${OPENROUTER_CLAUDE_FABLE_MODEL}}"
    export ANTHROPIC_DEFAULT_HAIKU_MODEL="${CLAUDE_OPENROUTER_HAIKU_MODEL:-${OPENROUTER_CLAUDE_HAIKU_MODEL}}"
    export ANTHROPIC_DEFAULT_SONNET_MODEL="${CLAUDE_OPENROUTER_SONNET_MODEL:-${OPENROUTER_CLAUDE_SONNET_MODEL}}"
    export ANTHROPIC_DEFAULT_OPUS_MODEL="${CLAUDE_OPENROUTER_OPUS_MODEL:-${OPENROUTER_CLAUDE_OPUS_MODEL}}"
    export CLAUDE_CODE_SUBAGENT_MODEL="${CLAUDE_OPENROUTER_SUBAGENT_MODEL:-${OPENROUTER_CLAUDE_SUBAGENT_MODEL}}"
    export API_TIMEOUT_MS="${timeout_ms}"
    export CLAUDE_CODE_AUTO_COMPACT_WINDOW=1000000
    export CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1
    export CLAUDE_CODE_PROVIDER_MANAGED_BY_HOST=1
    export CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY="${CLAUDE_OPENROUTER_GATEWAY_DISCOVERY:-1}"
    export CLAUDE_CODE_SUBPROCESS_ENV_SCRUB="${CLAUDE_SCRUB}"
    export CLAUDE_CONFIG_DIR="${config_dir}"
    exec claude ${claude_args[@]+"${claude_args[@]}"} "$@"
}

# Emits `export KEY='value'` lines for every managed credential that resolves
# from the environment or .env.local; keys absent from both are omitted. The
# output is safe to eval: values are single-quoted with embedded quotes escaped.
print_env_exports() {
    emit_export() {
        local canonical="$1" value escaped
        shift
        if ! value="$(find_credential "$@")"; then
            return 0
        fi
        escaped="$(printf '%s' "${value}" | sed "s/'/'\\\\''/g")"
        printf "export %s='%s'\n" "${canonical}" "${escaped}"
    }
    emit_export ZAI_API_KEY ZAI_API_KEY Z_AI_API_KEY
    # Native opencode's built-in "Z.AI Coding Plan" provider reads ZHIPU_API_KEY.
    emit_export ZHIPU_API_KEY ZHIPU_API_KEY ZAI_API_KEY Z_AI_API_KEY
    emit_export OPENROUTER_API_KEY OPENROUTER_API_KEY
    emit_export GITHUB_TOKEN GITHUB_TOKEN GH_TOKEN GITHUB_PERSONAL_ACCESS_TOKEN GITHUB_PAT
    emit_export UNITY_MCP_BEARER_TOKEN UNITY_MCP_BEARER_TOKEN
    emit_export UNITY_PROJECT_PATH UNITY_PROJECT_PATH
}

print_help() {
    cat <<'HELP'
Usage:
  bash .devcontainer/ai-backends.sh install
  bash .devcontainer/ai-backends.sh env
  codex-zai [codex arguments...]
  claude-zai [claude arguments...]
  codex-openrouter [codex arguments...]
  claude-openrouter [claude arguments...]

The ordinary `codex` and `claude` commands retain their native backends.

`env` prints `export KEY='value'` lines for every managed credential found in
the environment or the checkout's .env.local (never sourced, parsed as data):
ZAI_API_KEY, ZHIPU_API_KEY (for native opencode's Z.AI Coding Plan provider),
OPENROUTER_API_KEY, GITHUB_TOKEN (from any GitHub alias), UNITY_MCP_BEARER_TOKEN,
and UNITY_PROJECT_PATH. Native tools can pick the credentials up with:
eval "$(bash .devcontainer/ai-backends.sh env)".

Z.ai backends: set ZAI_API_KEY (or Z_AI_API_KEY) in the environment or the
checkout's .env.local. Optional overrides: CODEX_ZAI_MODEL,
CODEX_ZAI_REASONING_EFFORT, ZAI_API_TIMEOUT_MS (default 3000000),
CLAUDE_ZAI_CONFIG_DIR,
CLAUDE_ZAI_HAIKU_MODEL, CLAUDE_ZAI_SONNET_MODEL, CLAUDE_ZAI_OPUS_MODEL,
AI_BACKENDS_CONTAINER_MODE, CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB (auto, 0, or 1).

OpenRouter backends: set OPENROUTER_API_KEY in the environment or the
checkout's .env.local (parsed as data, never sourced). Optional overrides:
CODEX_OPENROUTER_MODEL (default openai/gpt-5.6-sol),
CLAUDE_OPENROUTER_FABLE_MODEL (default anthropic/claude-fable-5.1),
CLAUDE_OPENROUTER_OPUS_MODEL (default anthropic/claude-opus-5),
CLAUDE_OPENROUTER_SONNET_MODEL (default anthropic/claude-sonnet-5),
CLAUDE_OPENROUTER_HAIKU_MODEL (default anthropic/claude-haiku-4.5),
CLAUDE_OPENROUTER_SUBAGENT_MODEL, CLAUDE_OPENROUTER_CONFIG_DIR,
CLAUDE_OPENROUTER_GATEWAY_DISCOVERY (0 or 1, default 1),
OPENROUTER_API_TIMEOUT_MS, AI_BACKENDS_CONTAINER_MODE,
CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB (auto, 0, or 1).
HELP
}

action="$(resolve_action "${1:-}")"
if [ "$(basename "$0")" != "codex-zai" ] \
    && [ "$(basename "$0")" != "claude-zai" ] \
    && [ "$(basename "$0")" != "codex-openrouter" ] \
    && [ "$(basename "$0")" != "claude-openrouter" ] \
    && [ "$#" -gt 0 ]; then
    shift
fi

case "${action}" in
    install) install_backend_support ;;
    env) print_env_exports ;;
    codex-zai) launch_codex_zai "$@" ;;
    claude-zai) launch_claude_zai "$@" ;;
    codex-openrouter) launch_codex_openrouter "$@" ;;
    claude-openrouter) launch_claude_openrouter "$@" ;;
    help|-h|--help) print_help ;;
    *) die "Unknown action: ${action}" ;;
esac
