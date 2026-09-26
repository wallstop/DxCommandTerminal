#!/usr/bin/env bash
# shellcheck shell=bash

# Refresh the user-scoped agent CLIs. The image provides a build-time copy, so an
# offline launch keeps working while an online launch moves each CLI to its npm
# release line without requiring sudo.

set -euo pipefail

readonly NPM_PREFIX="${NPM_CONFIG_PREFIX:-${HOME}/.local}"
readonly LOG_PREFIX="[agent-clis]"
readonly PACKAGES=(
    "@openai/codex@latest"
    "@opencode/cli@2"
    "@nanocollective/nanocoder@latest"
    "@anthropic-ai/claude-code@latest"
)
readonly COMMANDS=(
    "codex"
    "opencode"
    "nanocoder"
    "claude"
)

log() {
    echo "${LOG_PREFIX} $*"
}

warn() {
    echo "${LOG_PREFIX} WARN: $*" >&2
}

if ! command -v npm >/dev/null 2>&1; then
    warn "npm is unavailable; keeping the image-provided agent CLIs."
    exit 0
fi

mkdir -p "${NPM_PREFIX}/bin" "${NPM_PREFIX}/lib"
export PATH="${NPM_PREFIX}/bin:${PATH}"

# Several VS Code lifecycle hooks can overlap during a rebuild. One updater is
# enough; the image-provided commands remain available to the other callers.
if command -v flock >/dev/null 2>&1; then
    exec 9>"${TMPDIR:-/tmp}/dxt-install-agent-clis.lock"
    if ! flock -n 9; then
        log "another agent CLI refresh is already running."
        exit 0
    fi
fi

if npm list --global --depth=0 opencode-ai >/dev/null 2>&1; then
    log "removing legacy opencode-ai package."
    if ! timeout 300 npm uninstall --global opencode-ai --silent --no-fund --no-audit; then
        warn "failed to remove the legacy opencode-ai package."
        exit 1
    fi
fi

command_version() {
    local command_name="$1"
    local output=""
    case "${command_name}" in
        codex) output="$(timeout 10 codex --version 2>/dev/null || true)" ;;
        opencode|opencode2) output="$(timeout 10 "${command_name}" --version 2>/dev/null || true)" ;;
        nanocoder) output="$(timeout 10 nanocoder --version 2>/dev/null || true)" ;;
        claude) output="$(timeout 10 claude --version 2>/dev/null || true)" ;;
        *) return 1 ;;
    esac
    grep -Eo '[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?' <<< "${output}" | head -n 1
}

resolve_latest_version() {
    local package_spec="$1"
    timeout 20 npm view "${package_spec}" version --json 2>/dev/null \
        | jq -r 'if type == "array" then max_by((split(".")[0:3] | map(tonumber? // 0))) else . end' \
        | tr -d '[:space:]' || true
}

failures=0
for index in "${!PACKAGES[@]}"; do
    package_spec="${PACKAGES[$index]}"
    package_name="${package_spec%@*}"
    command_name="${COMMANDS[$index]}"
    installed="$(command_version "${command_name}" || true)"
    latest="$(resolve_latest_version "${package_spec}")"

    if [[ -z "${latest}" ]]; then
        if command -v "${command_name}" >/dev/null 2>&1; then
            log "registry unavailable; keeping ${package_name}@${installed:-unknown}."
        else
            warn "registry unavailable and ${package_name} is not installed."
            ((failures += 1))
        fi
        continue
    fi

    if [[ "${installed}" == "${latest}" ]]; then
        log "${package_name}@${installed} is current."
        continue
    fi

    if [[ ! "${latest}" =~ ^[0-9]+\.[0-9]+\.[0-9]+([-+][0-9A-Za-z.-]+)?$ ]]; then
        warn "Registry returned an invalid version for ${package_name}."
        ((failures += 1))
        continue
    fi

    log "installing ${package_spec} (current: ${installed:-missing})..."
    installed_ok=false
    for attempt in 1 2 3; do
        if timeout 300 npm install -g "${package_spec}" \
            --allow-scripts=@opencode/cli,@anthropic-ai/claude-code \
            --silent --no-fund --no-audit; then
            if [[ "$(command_version "${command_name}" || true)" == "${latest}" ]]; then
                installed_ok=true
                break
            fi
        fi
        warn "${package_name} install attempt ${attempt}/3 failed."
        sleep "$((attempt * 2))"
    done

    if [[ "${installed_ok}" == "true" ]]; then
        log "${package_name}@$(command_version "${command_name}" || echo "${latest}") is ready."
    else
        ((failures += 1))
    fi
done

opencode_version="$(command_version opencode || true)"
if [[ "${opencode_version}" != 2.* ]]; then
    warn "OpenCode v2 is required; found ${opencode_version:-missing}."
    ((failures += 1))
elif command -v opencode2 >/dev/null 2>&1 \
    && [[ "$(command_version opencode2 || true)" != "${opencode_version}" ]]; then
    warn "opencode2 does not match opencode ${opencode_version}."
    ((failures += 1))
fi

if [[ "${failures}" -gt 0 ]]; then
    warn "${failures} agent CLI refresh(es) failed; see messages above."
    exit 1
fi
