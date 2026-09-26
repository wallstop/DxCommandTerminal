#!/usr/bin/env bash
# shellcheck shell=bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../../.." && pwd)"
VERIFY_SCRIPT="${REPO_ROOT}/.devcontainer/verify-opencode.sh"
DOCKERFILE="${REPO_ROOT}/.devcontainer/Dockerfile"
INSTALLER="${REPO_ROOT}/.devcontainer/install-agent-clis.sh"
POST_START="${REPO_ROOT}/.devcontainer/post-start.sh"
DEVCONTAINER="${REPO_ROOT}/.devcontainer/devcontainer.json"
WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/dxt-devcontainer-test.XXXXXX")"
trap 'rm -rf "${WORK_DIR}"' EXIT

fail() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

assert_contains() {
    local file="$1" text="$2"
    grep -Fq -- "${text}" "${file}" || fail "${file} does not contain: ${text}"
}

assert_count() {
    local file="$1" text="$2" expected="$3" actual
    actual="$(grep -Fc -- "${text}" "${file}" || true)"
    [[ "${actual}" -eq "${expected}" ]] \
        || fail "${file} contains ${text} ${actual} times; expected ${expected}"
}

mkdir -p "${WORK_DIR}/bin"
cat >"${WORK_DIR}/bin/opencode" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "${DXT_FAKE_OPENCODE_VERSION:-opencode v2.0.16}"
EOF
cat >"${WORK_DIR}/bin/opencode2" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "${DXT_FAKE_OPENCODE2_VERSION:-opencode v2.0.16}"
EOF
chmod +x "${WORK_DIR}/bin/opencode" "${WORK_DIR}/bin/opencode2"

# Exclude the host's real CLIs so the alias-absent case cannot see one.
VERIFY_PATH="${WORK_DIR}/bin:/usr/bin:/bin"
PATH="${VERIFY_PATH}" bash "${VERIFY_SCRIPT}" >"${WORK_DIR}/v2.out"
grep -Fq "opencode and opencode2 agree" "${WORK_DIR}/v2.out" \
    || fail "v2 output did not verify the alias"

mv "${WORK_DIR}/bin/opencode2" "${WORK_DIR}/opencode2.saved"
PATH="${VERIFY_PATH}" bash "${VERIFY_SCRIPT}" >"${WORK_DIR}/no-alias.out"
grep -Fq "OpenCode opencode v2.0.16" "${WORK_DIR}/no-alias.out" \
    || fail "a valid v2 command was rejected when the optional alias was absent"
mv "${WORK_DIR}/opencode2.saved" "${WORK_DIR}/bin/opencode2"

if DXT_FAKE_OPENCODE_VERSION='opencode v1.18.32' \
    DXT_FAKE_OPENCODE2_VERSION='opencode v1.18.32' \
    PATH="${VERIFY_PATH}" bash "${VERIFY_SCRIPT}" >/dev/null 2>&1; then
    fail "v1 output was accepted"
fi

if DXT_FAKE_OPENCODE_VERSION='opencode v2.0.16' \
    DXT_FAKE_OPENCODE2_VERSION='opencode v1.18.32' \
    PATH="${VERIFY_PATH}" bash "${VERIFY_SCRIPT}" >/dev/null 2>&1; then
    fail "mismatched aliases were accepted"
fi

# shellcheck disable=SC2016
assert_contains "${DEVCONTAINER}" '"workspaceMount": "source=${localWorkspaceFolder},target=/workspaces/package,type=bind,consistency=consistent"'
assert_contains "${DEVCONTAINER}" '"workspaceFolder": "/workspaces/package"'
assert_contains "${DEVCONTAINER}" '"postAttachCommand": "bash .devcontainer/post-start.sh --attach"'
if grep -Fq '"BASH_ENV"' "${DEVCONTAINER}"; then
    fail "devcontainer must not inject credentials into every Bash subprocess"
fi
assert_contains "${DEVCONTAINER}" '"waitFor": "postCreateCommand"'
# shellcheck disable=SC2016
assert_contains "${POST_START}" 'source "${SCRIPT_DIR}/env-autoload.sh"'
assert_contains "${POST_START}" 'unset BASH_ENV'
assert_contains "${POST_START}" 'opencode service status'
assert_contains "${POST_START}" 'opencode service restart'
assert_contains "${POST_START}" 'Unauthorized'
assert_contains "${INSTALLER}" 'version --json'
if grep -Fq 'sort -V' "${INSTALLER}"; then
    fail "the npm range parser still sorts package-name-prefixed output"
fi
# Exercise the real resolver: `npm view <range> version` returns an array whose
# entries are not in version order, and an exact spec returns a bare string.
mkdir -p "${WORK_DIR}/registry"
cat >"${WORK_DIR}/registry/npm" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "${DXT_FAKE_NPM_VIEW:-[\"2.0.0\",\"2.0.16\",\"2.0.15\"]}"
EOF
chmod +x "${WORK_DIR}/registry/npm"
resolve_version() {
    PATH="${WORK_DIR}/registry:/usr/bin:/bin" DXT_FAKE_NPM_VIEW="$1" \
        bash -c 'source "$1"; resolve_latest_version "@opencode/cli@2"' _ "${INSTALLER}"
}
[[ "$(resolve_version '["2.0.0","2.0.16","2.0.15"]')" == "2.0.16" ]] \
    || fail "the resolver did not pick the highest version from an unordered array"
[[ "$(resolve_version '"2.0.16"')" == "2.0.16" ]] \
    || fail "the resolver did not pass a single version through"
assert_contains "${DOCKERFILE}" 'WORKDIR /workspaces/package'
assert_contains "${DOCKERFILE}" 'COPY .devcontainer/verify-opencode.sh /tmp/verify-opencode.sh'
assert_count "${DOCKERFILE}" 'bash /tmp/verify-opencode.sh' 2
if grep -Fq '(^|[[:space:]])2\.' "${DOCKERFILE}"; then
    fail "the pre-fix OpenCode version expression remains"
fi

# Exercise the lifecycle with non-Bash stubs. Configuration mints a token after
# the first environment load; the service restart must receive the reloaded token.
LIFECYCLE_ROOT="${WORK_DIR}/lifecycle"
mkdir -p "${LIFECYCLE_ROOT}/.devcontainer" "${LIFECYCLE_ROOT}/tooling~/scripts/mcp" \
    "${LIFECYCLE_ROOT}/tooling~/node_modules" "${LIFECYCLE_ROOT}/bin" "${LIFECYCLE_ROOT}/home"
LIFECYCLE_ROOT="$(cd "${LIFECYCLE_ROOT}" && pwd)"
cp "${REPO_ROOT}/.devcontainer/post-start.sh" \
    "${REPO_ROOT}/.devcontainer/env-autoload.sh" \
    "${REPO_ROOT}/.devcontainer/install-env-autoload.sh" \
    "${REPO_ROOT}/.devcontainer/verify-opencode.sh" \
    "${REPO_ROOT}/.devcontainer/ai-backends.sh" \
    "${LIFECYCLE_ROOT}/.devcontainer/"
cat >"${LIFECYCLE_ROOT}/.devcontainer/cache-contract.sh" <<'EOF'
#!/usr/bin/env bash
cache_contract_repair_permissions() { return 0; }
EOF
cat >"${LIFECYCLE_ROOT}/.devcontainer/install-agent-clis.sh" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
cat >"${LIFECYCLE_ROOT}/bin/node" <<'EOF'
#!/usr/bin/env bash
if [[ "$*" == *"configure --offline"* && ! -f .env.local ]]; then
    printf '%s\n' 'UNITY_MCP_BEARER_TOKEN=generated-token-123' >> .env.local
    printf '%s\n' 'Z_AI_API_KEY=zai-file-key' >> .env.local
    printf '%s\n' 'GITHUB_PERSONAL_ACCESS_TOKEN=github-file-key' >> .env.local
fi
EOF
cat >"${LIFECYCLE_ROOT}/bin/timeout" <<'EOF'
#!/usr/bin/env bash
if [[ "${1:-}" =~ ^[0-9]+$ ]]; then
    shift
fi
exec "$@"
EOF
cat >"${LIFECYCLE_ROOT}/bin/sleep" <<'EOF'
#!/usr/bin/env bash
exit 0
EOF
cat >"${LIFECYCLE_ROOT}/bin/opencode" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' "$*" >> opencode-calls.txt
case "${1:-}" in
    --version) printf '%s\n' 'opencode v2.0.16' ;;
    service)
        case "${2:-}" in
            status) printf '%s\n' 'http://127.0.0.1:49374' ;;
            restart)
                {
                    printf 'UNITY=%s\n' "${UNITY_MCP_BEARER_TOKEN-<unset>}"
                    printf 'GITHUB=%s\n' "${GITHUB_TOKEN-<unset>}"
                    printf 'ZAI=%s\n' "${ZAI_API_KEY-<unset>}"
                } > service-token.txt
                ;;
            stop) : ;;
        esac
        ;;
    mcp)
        if [[ "${2:-}" == "list" ]]; then
            if [[ "${DXT_FAKE_MCP_FAILURE:-0}" == "1" ]]; then
                printf '%s\n' '✗ unity-mcp failed: Unauthorized'
            else
                printf '%s\n' '✓ unity-mcp connected'
            fi
        fi
        ;;
esac
EOF
chmod +x "${LIFECYCLE_ROOT}/bin/node" "${LIFECYCLE_ROOT}/bin/opencode" \
    "${LIFECYCLE_ROOT}/bin/timeout" "${LIFECYCLE_ROOT}/bin/sleep" \
    "${LIFECYCLE_ROOT}/.devcontainer/install-agent-clis.sh"
touch "${LIFECYCLE_ROOT}/home/.bashrc" "${LIFECYCLE_ROOT}/home/.profile"
: >"${LIFECYCLE_ROOT}/package.json"
# The checkout's credentials and the loader's own control variables must not
# leak in from the caller, or process precedence hides what the lifecycle did.
run_lifecycle() {
    DXT_FAKE_MCP_FAILURE="${DXT_FAKE_MCP_FAILURE:-0}" \
        HOME="${LIFECYCLE_ROOT}/home" WORKSPACE_FOLDER="${LIFECYCLE_ROOT}" \
        PATH="${LIFECYCLE_ROOT}/bin:/usr/bin:/bin:/usr/sbin:/sbin" \
        env -u ZAI_API_KEY -u Z_AI_API_KEY -u ZHIPU_API_KEY -u OPENROUTER_API_KEY \
        -u GITHUB_TOKEN -u GH_TOKEN -u GITHUB_PERSONAL_ACCESS_TOKEN -u GITHUB_PAT \
        -u UNITY_MCP_BEARER_TOKEN -u AI_BACKENDS_REPO_ROOT \
        -u DXT_WORKSPACE_ROOT -u DXT_ENV_AUTOLOAD_DISABLED -u DXT_ENV_AUTOLOAD_ACTIVE \
        -u BASH_ENV \
        bash "${LIFECYCLE_ROOT}/.devcontainer/post-start.sh" "$@" \
        >"${LIFECYCLE_ROOT}/lifecycle.log" 2>&1
}
run_lifecycle
# The first run also spawns the background refresh, which rewrites the same
# service-token.txt. Wait for it so the assertions never race it.
deadline=$((SECONDS + 30))
while [[ "$(grep -c '^service restart$' "${LIFECYCLE_ROOT}/opencode-calls.txt" || true)" -lt 2 ]]; do
    if [[ "${SECONDS}" -ge "${deadline}" ]]; then
        fail "the background OpenCode refresh never finished ($(wc -l <"${LIFECYCLE_ROOT}/opencode-calls.txt" 2>/dev/null || printf 0) call(s) seen)"
    fi
    sleep 0.2
done
assert_contains "${LIFECYCLE_ROOT}/service-token.txt" 'UNITY=generated-token-123' \
    "post-start passes the newly generated bearer token to OpenCode"
assert_contains "${LIFECYCLE_ROOT}/service-token.txt" 'GITHUB=github-file-key' \
    "post-start normalizes the GitHub alias"
assert_contains "${LIFECYCLE_ROOT}/service-token.txt" 'ZAI=zai-file-key' \
    "post-start normalizes the Z.AI alias"
printf '%s\n' 'UNITY_MCP_BEARER_TOKEN=rotated-token' 'Z_AI_API_KEY=zai-file-key' \
    'GITHUB_PERSONAL_ACCESS_TOKEN=github-file-key' > "${LIFECYCLE_ROOT}/.env.local"
run_lifecycle --attach
assert_contains "${LIFECYCLE_ROOT}/service-token.txt" 'UNITY=rotated-token' \
    "post-attach refreshes the OpenCode service token"
DXT_FAKE_MCP_FAILURE=1 run_lifecycle --attach
grep -Fq 'rejected the Unity credentials' "${LIFECYCLE_ROOT}/lifecycle.log" \
    || fail "post-attach accepted an unauthorized OpenCode MCP service"
grep -Fq 'service stop' "${LIFECYCLE_ROOT}/opencode-calls.txt" \
    || fail "post-attach did not stop the unusable OpenCode service"
grep -Fq "DXT_WORKSPACE_ROOT='${LIFECYCLE_ROOT}'" "${LIFECYCLE_ROOT}/home/.bashrc" \
    || fail "post-attach did not migrate the interactive autoload block"

# An unterminated marker must never let the rewrite drop the rest of the file.
printf '%s\n' 'export EDITOR=code' \
    '# >>> dxcommandterminal .env.local autoload >>>' \
    "DXT_WORKSPACE_ROOT='${LIFECYCLE_ROOT}'" \
    'alias ll="ls -la"' > "${LIFECYCLE_ROOT}/home/.bashrc"
if HOME="${LIFECYCLE_ROOT}/home" \
    PATH="${LIFECYCLE_ROOT}/bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    bash -c 'source "$1"; install_env_local_autoload "$2"' \
    _ "${LIFECYCLE_ROOT}/.devcontainer/install-env-autoload.sh" "${LIFECYCLE_ROOT}" \
    >/dev/null 2>&1; then
    fail "an unterminated autoload block was reported as installed"
fi
grep -Fqx 'alias ll="ls -la"' "${LIFECYCLE_ROOT}/home/.bashrc" \
    || fail "an unterminated autoload block truncated the rest of the rc file"

# A reversed marker pair is malformed too: the rewrite would drop the tail.
printf '%s\n' 'export KEEPME=1' \
    '# <<< dxcommandterminal .env.local autoload <<<' \
    '# >>> dxcommandterminal .env.local autoload >>>' \
    "DXT_WORKSPACE_ROOT='${LIFECYCLE_ROOT}'" > "${LIFECYCLE_ROOT}/home/.bashrc"
if HOME="${LIFECYCLE_ROOT}/home" \
    PATH="${LIFECYCLE_ROOT}/bin:/usr/bin:/bin:/usr/sbin:/sbin" \
    bash -c 'source "$1"; install_env_local_autoload "$2"' \
    _ "${LIFECYCLE_ROOT}/.devcontainer/install-env-autoload.sh" "${LIFECYCLE_ROOT}" \
    >/dev/null 2>&1; then
    fail "a reversed autoload block was reported as installed"
fi
grep -Fqx 'export KEEPME=1' "${LIFECYCLE_ROOT}/home/.bashrc" \
    || fail "a reversed autoload block truncated the rest of the rc file"

# The gate this change exists for: a v1-only image must not reach the attach
# point. The stub reports v1 through the same commands the verifier reads.
cat >"${LIFECYCLE_ROOT}/bin/opencode" <<'EOF'
#!/usr/bin/env bash
case "${1:-}" in
    --version) printf '%s\n' 'opencode v1.18.32' ;;
    mcp) [[ "${2:-}" == "list" ]] && printf '%s\n' '✓ unity-mcp connected' ;;
esac
EOF
chmod +x "${LIFECYCLE_ROOT}/bin/opencode"
if run_lifecycle --attach; then
    fail "post-start attached with a v1-only OpenCode"
fi
grep -Fq 'OpenCode v2 is required' "${LIFECYCLE_ROOT}/lifecycle.log" \
    || fail "post-start did not report the missing OpenCode v2"

printf 'PASS: devcontainer OpenCode verification contract\n'
