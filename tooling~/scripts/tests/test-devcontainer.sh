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
resolved_version="$(printf '%s\n' '["2.0.0","2.0.16","2.0.15"]' \
    | jq -r 'if type == "array" then max_by((split(".")[0:3] | map(tonumber? // 0))) else . end')"
[[ "${resolved_version}" == "2.0.16" ]] || fail "range version fixture resolved to ${resolved_version}"
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
# The checkout's credentials must not leak in from the caller's environment, or
# process precedence hides what the lifecycle loaded from .env.local.
run_lifecycle() {
    DXT_FAKE_MCP_FAILURE="${DXT_FAKE_MCP_FAILURE:-0}" \
        HOME="${LIFECYCLE_ROOT}/home" WORKSPACE_FOLDER="${LIFECYCLE_ROOT}" \
        PATH="${LIFECYCLE_ROOT}/bin:/usr/bin:/bin:/usr/sbin:/sbin" \
        env -u ZAI_API_KEY -u Z_AI_API_KEY -u ZHIPU_API_KEY -u OPENROUTER_API_KEY \
        -u GITHUB_TOKEN -u GH_TOKEN -u GITHUB_PERSONAL_ACCESS_TOKEN -u GITHUB_PAT \
        -u UNITY_MCP_BEARER_TOKEN \
        bash "${LIFECYCLE_ROOT}/.devcontainer/post-start.sh" "$@" \
        >"${LIFECYCLE_ROOT}/lifecycle.log" 2>&1
}
run_lifecycle
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
grep -Fq 'did not load its MCP config' "${LIFECYCLE_ROOT}/lifecycle.log" \
    || fail "post-attach accepted an unauthorized OpenCode MCP service"
grep -Fq 'service stop' "${LIFECYCLE_ROOT}/opencode-calls.txt" \
    || fail "post-attach did not stop the unusable OpenCode service"
grep -Fq "DXT_WORKSPACE_ROOT='${LIFECYCLE_ROOT}'" "${LIFECYCLE_ROOT}/home/.bashrc" \
    || fail "post-attach did not migrate the interactive autoload block"

printf 'PASS: devcontainer OpenCode verification contract\n'
