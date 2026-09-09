#!/usr/bin/env bash
# shellcheck shell=bash

# Shared devcontainer cache mount contract.
# Keep these arrays aligned by index: source[i] mounts to target[i].
#
# Entries:
#   1. dxt-nuget-cache          -> NuGet package cache for .NET restore
#   2. dxt-dotnet-tools         -> Global dotnet tools (CSharpier)
#   3. dxt-powershell-modules   -> PowerShell module cache
#   4. dxt-python-cache         -> pip wheel/download cache
#   5. dxt-npm-cache            -> npm download cache
#   6. dxt-node-modules         -> Linux devcontainer node_modules tree
#
# This container runs no local Unity build. Unity stays on the host and is driven
# through the unity-mcp bridge, so there is deliberately no Unity Library mount.

# Re-source guard: this file is sourced by post-create.sh, post-start.sh, and
# validate scripts; multiple sources in one shell would otherwise re-declare
# readonly arrays and abort under `set -e`.
[[ "${_DXT_CACHE_CONTRACT_LOADED:-}" == "1" ]] && return 0
_DXT_CACHE_CONTRACT_LOADED=1

# Workspace root resolution. Prefer WORKSPACE_FOLDER (set by devcontainer.json
# remoteEnv == ${containerWorkspaceFolder}). When unset - e.g. during
# postCreateCommand, which runs BEFORE remoteEnv is applied - derive it from this
# script's location: .devcontainer/cache-contract.sh -> parent is the root.
CACHE_WORKSPACE_ROOT="${WORKSPACE_FOLDER:-}"
if [[ -z "${CACHE_WORKSPACE_ROOT}" ]]; then
    _dxt_cache_contract_source="${BASH_SOURCE[0]:?cache-contract.sh must be sourced by path so the workspace root can be derived}"
    _dxt_cache_contract_source="${_dxt_cache_contract_source//\\//}"
    CACHE_WORKSPACE_ROOT="$(cd -- "$(dirname -- "${_dxt_cache_contract_source}")/.." && pwd)"
    unset _dxt_cache_contract_source
fi
readonly CACHE_WORKSPACE_ROOT

readonly CACHE_MOUNT_SOURCES=(
    "dxt-nuget-cache"
    "dxt-dotnet-tools"
    "dxt-powershell-modules"
    "dxt-python-cache"
    "dxt-npm-cache"
    "dxt-node-modules"
)

readonly CACHE_MOUNT_TARGETS=(
    "/home/vscode/.nuget"
    "/home/vscode/.dotnet/tools"
    "/home/vscode/.local/share/powershell"
    "/home/vscode/.cache/pip"
    "/home/vscode/.npm"
    "${CACHE_WORKSPACE_ROOT}/tooling~/node_modules"
)

cache_contract_validate_shape() {
    if [[ "${#CACHE_MOUNT_SOURCES[@]}" -eq 0 ]] \
        || [[ "${#CACHE_MOUNT_TARGETS[@]}" -eq 0 ]] \
        || [[ "${#CACHE_MOUNT_SOURCES[@]}" -ne "${#CACHE_MOUNT_TARGETS[@]}" ]]; then
        return 1
    fi
    return 0
}

cache_contract_describe_workspace_root() {
    if [[ -n "${WORKSPACE_FOLDER:-}" ]]; then
        echo "CACHE_WORKSPACE_ROOT=${CACHE_WORKSPACE_ROOT} (from WORKSPACE_FOLDER env)"
    else
        echo "CACHE_WORKSPACE_ROOT=${CACHE_WORKSPACE_ROOT} (derived from script location; WORKSPACE_FOLDER unset)"
    fi
}

cache_contract_get_owner_uid() {
    local target="$1"
    local owner_uid
    if owner_uid="$(stat -c %u "$target" 2>/dev/null)" && [[ "$owner_uid" =~ ^[0-9]+$ ]]; then
        echo "$owner_uid"
        return 0
    fi
    if owner_uid="$(stat -f %u "$target" 2>/dev/null)" && [[ "$owner_uid" =~ ^[0-9]+$ ]]; then
        echo "$owner_uid"
        return 0
    fi
    return 1
}

cache_contract_is_container_runtime() {
    if [[ -f "/.dockerenv" ]]; then
        return 0
    fi
    if [[ "${DEVCONTAINER:-}" == "true" ]] || [[ "${REMOTE_CONTAINERS:-}" == "true" ]]; then
        return 0
    fi
    if grep -qaE '(docker|containerd|kubepods)' /proc/1/cgroup 2>/dev/null; then
        return 0
    fi
    return 1
}

# Repair only managed caches and npm's own files. Never recursively chown the
# host bind mount. A user-owned root can still contain files left by `sudo npm`.
cache_contract_repair_permissions() {
    local current_uid current_gid target probe
    current_uid="$(id -u)"
    current_gid="$(id -g)"
    cache_contract_validate_shape || return 1
    for target in "${CACHE_MOUNT_TARGETS[@]}" "${HOME}/.local"; do
        cache_contract_repair_directory "$target" "$current_uid" "$current_gid" || return 1
    done
    for target in "${HOME}/.npmrc" "${CACHE_WORKSPACE_ROOT}/tooling~/package-lock.json" \
        "${CACHE_WORKSPACE_ROOT}/package.json"; do
        if [[ -f "$target" && ! -w "$target" ]]; then
            # Host bind mounts can be writable without supporting ownership changes.
            sudo -n chown -h "$current_uid:$current_gid" "$target" || true
            if [[ ! -w "$target" ]]; then
                echo "[cache] $target is not writable; fix its host permissions." >&2
                return 1
            fi
        fi
    done
    probe="${CACHE_WORKSPACE_ROOT}/.dxt-write-probe-$$"
    if ! touch "$probe"; then
        echo "[cache] Workspace is not writable; fix the host checkout's ownership." >&2
        return 1
    fi
    rm -f "$probe"
}

cache_contract_repair_directory() {
    local target="$1" current_uid="$2" current_gid="$3" probe
    if [[ ! -d "$target" ]]; then
        sudo -n install -d -o "$current_uid" -g "$current_gid" "$target" || return 1
    fi
    sudo -n find "$target" -xdev ! -uid "$current_uid" \
        -exec chown -h "$current_uid:$current_gid" {} + || return 1
    probe="$target/.dxt-write-probe-$$"
    touch "$probe" && rm -f "$probe"
}
