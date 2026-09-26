#!/usr/bin/env bash
# shellcheck shell=bash

set -euo pipefail

readonly LOG_PREFIX="[verify-opencode]"

fail() {
    printf '%s ERROR: %s\n' "${LOG_PREFIX}" "$*" >&2
    exit 1
}

command -v opencode >/dev/null 2>&1 || fail "opencode is not installed"

opencode_version="$(opencode --version)" \
    || fail "opencode --version failed"

# The v2 CLI prints "opencode v2.x.y". Accept a bare version too, but require
# the major version to be separated from the command name.
if ! printf '%s\n' "${opencode_version}" | grep -Eq '(^|[[:space:]])v?2\.'; then
    fail "OpenCode v2 is required; found ${opencode_version:-empty output}"
fi

# The stable command is `opencode`. During the v2 beta, the npm package also
# installs `opencode2`; verify it when present, but do not make the alias a
# requirement for valid non-npm installations.
if command -v opencode2 >/dev/null 2>&1; then
    opencode2_version="$(opencode2 --version)" \
        || fail "opencode2 --version failed"
    if [[ "${opencode_version}" != "${opencode2_version}" ]]; then
        fail "opencode2 (${opencode2_version:-empty output}) does not match opencode (${opencode_version})"
    fi
    printf '%s OpenCode %s (opencode and opencode2 agree)\n' \
        "${LOG_PREFIX}" "${opencode_version}"
else
    printf '%s OpenCode %s\n' "${LOG_PREFIX}" "${opencode_version}"
fi
