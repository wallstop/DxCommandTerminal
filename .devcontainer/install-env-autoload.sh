#!/usr/bin/env bash
# shellcheck shell=bash

# The sourced entry point retains the lifecycle's shared, bounded lock.
install_env_local_autoload() (
    local installer_dir
    installer_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    if command -v flock >/dev/null 2>&1; then
        exec 9>"${TMPDIR:-/tmp}/dxt-env-autoload.lock"
        if ! flock -w 30 9; then
            printf '%s\n' 'Timed out waiting for the environment autoload lock.' >&2
            return 1
        fi
    fi
    python3 "${installer_dir}/install_env_autoload.py" "$1"
)
