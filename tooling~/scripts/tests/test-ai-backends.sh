#!/usr/bin/env bash
# =============================================================================
# Regression suite for .devcontainer/ai-backends.sh (claude-zai / codex-zai).
# Exercises a hostile provider environment, custom CODEX_HOME, key absence,
# argument forwarding, launcher installation, and file permissions - all
# against fake codex/claude CLIs so nothing is billed.
#
# Note: this copy carries one intentional divergence from the NovaSharp/Totem/
# Fortress byte-duplicated launcher: `exec claude
# ${claude_args[@]+"${claude_args[@]}"}` (set -u safe) so the launcher also
# works on macOS hosts running the system bash 3.2, where expanding an empty
# array under `set -u` is an error.
# =============================================================================
# shellcheck shell=bash
# Deliberate patterns: (..) subshells isolate per-test exports (SC2030/SC2031);
# A && B || C on grep -q is safe (B never fails); assert_contains is defined
# twice (once before helpers are used, once after) so SC2317 flags the stub.
# shellcheck disable=SC2030,SC2031,SC2015,SC2317
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKENDS="${SCRIPT_DIR}/../../../.devcontainer/ai-backends.sh"
WORK="$(mktemp -d)"
trap 'rm -rf "${WORK}"' EXIT

PASS=0
FAIL=0

ok() {
    PASS=$((PASS + 1))
    printf '  ok: %s\n' "$1"
}

bad() {
    FAIL=$((FAIL + 1))
    printf '  FAIL: %s\n' "$1" >&2
}

assert() {
    local label="$1"
    shift
    if "$@"; then
        ok "${label}"
    else
        bad "${label}"
    fi
}

assert_not_contains() {
    local file="$1" pattern="$2" label="$3"
    if grep -q -- "${pattern}" "${file}"; then
        bad "${label}"
    else
        ok "${label}"
    fi
}

assert_contains() {
    local file="$1" pattern="$2" label="$3"
    assert "${label}" grep -q -- "${pattern}" "${file}"
}

fake_cli_dir="${WORK}/bin"
mkdir -p "${fake_cli_dir}"
for name in codex claude; do
    cat > "${fake_cli_dir}/${name}" <<FAKE
#!/usr/bin/env bash
{
    echo "--- args ---"
    printf '%s\n' "\$@"
    echo "--- env ---"
    env | LC_ALL=C sort
} >> "\${FAKE_CLI_OUT:-/dev/stdout}"
exit 0
FAKE
    chmod +x "${fake_cli_dir}/${name}"
done

# The fake CLI dir must lead PATH, but system paths must follow so the
# ai-backends shebang (#!/usr/bin/env bash) still resolves.
SANDBOX_PATH="${fake_cli_dir}:/usr/bin:/bin:/usr/sbin:/sbin:${PATH}"

record() {
    # $1 = unique output file; remaining args = extra CLI arguments.
    # RECORD_CODEX_HOME optionally redirects the launcher's CODEX_HOME.
    local out="$1"
    shift
    FAKE_CLI_OUT="${out}" PATH="${SANDBOX_PATH}" \
        CODEX_HOME="${RECORD_CODEX_HOME:-${WORK}/codex-home}" \
        "${BACKENDS}" "$@" >/dev/null 2>&1
}

assert_contains() {
    local file="$1" pattern="$2" label="$3"
    assert "${label}" grep -q -- "${pattern}" "${file}"
}

echo "== codex-zai: profile, model, and key forwarding =="
out="${WORK}/codex-env.txt"
ZAI_API_KEY="test-key-codex" record "${out}" codex-zai --model glm-5.3
assert_contains "${out}" '^--profile$' "codex-zai passes --profile"
assert_contains "${out}" '^devcontainer-zai$' "codex-zai uses the zai profile"
assert_contains "${out}" 'model_provider="ZAI"' "codex-zai pins the ZAI provider"
assert_contains "${out}" 'model_reasoning_effort="max"' "codex-zai defaults reasoning to max"
assert_contains "${out}" '^ZAI_API_KEY=test-key-codex$' "codex-zai exports ZAI_API_KEY"

echo "== codex-zai: Z_AI_API_KEY alias and reasoning override =="
out="${WORK}/codex-alias.txt"
(
    unset ZAI_API_KEY
    Z_AI_API_KEY="alias-key" CODEX_ZAI_REASONING_EFFORT=low record "${out}" codex-zai
)
assert_contains "${out}" '^ZAI_API_KEY=alias-key$' "Z_AI_API_KEY alias is honored"
assert_contains "${out}" 'model_reasoning_effort="low"' "reasoning effort override applied"

echo "== codex-zai: .env.local fallback for the Z.AI key =="
mkdir -p "${WORK}/zai-envroot"
printf '# comment\nZ_AI_API_KEY="zai-key-from-envfile"\n' > "${WORK}/zai-envroot/.env.local"
out="${WORK}/codex-zai-envfile.txt"
(
    unset ZAI_API_KEY Z_AI_API_KEY
    export AI_BACKENDS_REPO_ROOT="${WORK}/zai-envroot"
    record "${out}" codex-zai
)
assert_contains "${out}" '^ZAI_API_KEY=zai-key-from-envfile$' \
    ".env.local Z_AI_API_KEY alias is resolved (quotes stripped)"

printf 'ZAI_API_KEY=zai-file-key\n' > "${WORK}/zai-envroot/.env.local"
out="${WORK}/codex-zai-envfile2.txt"
(
    unset ZAI_API_KEY Z_AI_API_KEY
    export AI_BACKENDS_REPO_ROOT="${WORK}/zai-envroot"
    record "${out}" codex-zai
)
assert_contains "${out}" '^ZAI_API_KEY=zai-file-key$' ".env.local ZAI_API_KEY is resolved"

echo "== codex-zai: environment beats .env.local; empty env vars are ignored =="
out="${WORK}/codex-zai-precedence.txt"
(
    export ZAI_API_KEY="env-wins"
    export AI_BACKENDS_REPO_ROOT="${WORK}/zai-envroot"
    record "${out}" codex-zai
)
assert_contains "${out}" '^ZAI_API_KEY=env-wins$' "environment beats .env.local"

out="${WORK}/codex-zai-empty-env.txt"
(
    export ZAI_API_KEY=""
    export AI_BACKENDS_REPO_ROOT="${WORK}/zai-envroot"
    record "${out}" codex-zai
)
assert_contains "${out}" '^ZAI_API_KEY=zai-file-key$' "empty environment variable is ignored"

echo "== codex-zai: default .env.local location is derived from the script path =="
mkdir -p "${WORK}/defroot/.devcontainer"
cp "${BACKENDS}" "${WORK}/defroot/.devcontainer/ai-backends.sh"
printf 'ZAI_API_KEY=default-root-key\n' > "${WORK}/defroot/.env.local"
out="${WORK}/codex-zai-defroot.txt"
(
    unset ZAI_API_KEY Z_AI_API_KEY AI_BACKENDS_REPO_ROOT
    FAKE_CLI_OUT="${out}" PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
        "${WORK}/defroot/.devcontainer/ai-backends.sh" codex-zai >/dev/null 2>&1
)
assert_contains "${out}" '^ZAI_API_KEY=default-root-key$' \
    "default .env.local resolves next to the checkout root"

echo "== codex-zai: hostile env is not leaked into the profile files =="
grep -q 'test-key-codex' "${WORK}/codex-home/devcontainer-zai.config.toml" \
    && bad "profile contains the raw key" || ok "profile does not embed the raw key"

echo "== codex-zai: missing key fails loudly =="
mkdir -p "${WORK}/empty-root"
if env -u ZAI_API_KEY -u Z_AI_API_KEY AI_BACKENDS_REPO_ROOT="${WORK}/empty-root" \
    PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
    "${BACKENDS}" codex-zai >/dev/null 2>"${WORK}/missing-key.err"; then
    bad "missing key should fail"
else
    assert_contains "${WORK}/missing-key.err" 'ZAI_API_KEY' "missing key error mentions ZAI_API_KEY"
fi

echo "== codex-zai: invalid reasoning effort fails =="
if ZAI_API_KEY="test-key" CODEX_ZAI_REASONING_EFFORT=extreme PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
    "${BACKENDS}" codex-zai >/dev/null 2>"${WORK}/bad-effort.err"; then
    bad "invalid reasoning effort should fail"
else
    assert_contains "${WORK}/bad-effort.err" 'low, high, or max' "invalid effort error is precise"
fi

echo "== claude-zai: endpoint, token, scrubbed environment =="
out="${WORK}/claude-env.txt"
env -u ANTHROPIC_API_KEY PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
    CLAUDE_ZAI_CONFIG_DIR="${WORK}/claude-zai-home" \
    AI_BACKENDS_CONTAINER_MODE=no CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB=0 \
    ZAI_API_KEY="test-key-claude" \
    ANTHROPIC_API_KEY=shadowed \
    ANTHROPIC_BASE_URL=https://evil.example \
    CLAUDE_CODE_USE_BEDROCK=1 \
    CLAUDE_CODE_USE_VERTEX=1 \
    CLAUDE_CODE_USE_GATEWAY=1 \
    "${BACKENDS}" claude-zai --dangerously-skip-permissions >>"${out}" 2>&1
assert_contains "${out}" '^ANTHROPIC_AUTH_TOKEN=test-key-claude$' "claude-zai sets ANTHROPIC_AUTH_TOKEN"
assert_contains "${out}" '^ANTHROPIC_BASE_URL=https://api.z.ai/api/anthropic$' "claude-zai points at the Z.AI anthropic endpoint"
assert_contains "${out}" '^API_TIMEOUT_MS=3000000$' \
    "claude-zai defaults API_TIMEOUT_MS to the Z.AI-recommended 3000000"
assert_contains "${out}" '^ANTHROPIC_DEFAULT_SONNET_MODEL=glm-5.3\[1m\]$' "claude-zai maps sonnet to glm-5.3[1m]"
assert_contains "${out}" '^ANTHROPIC_DEFAULT_HAIKU_MODEL=glm-5.3-flash\[1m\]$' "claude-zai maps haiku to glm-5.3-flash[1m]"
assert_contains "${out}" '^CLAUDE_CONFIG_DIR=' "claude-zai isolates CLAUDE_CONFIG_DIR"
assert_not_contains "${out}" '^ANTHROPIC_API_KEY=' "competing ANTHROPIC_API_KEY is removed"
assert_not_contains "${out}" '^CLAUDE_CODE_USE_BEDROCK=' "Bedrock selector is removed"
assert_not_contains "${out}" '^CLAUDE_CODE_USE_VERTEX=' "Vertex selector is removed"
assert_not_contains "${out}" '^CLAUDE_CODE_USE_GATEWAY=' "Gateway selector is removed"
assert_not_contains "${out}" '^ZAI_API_KEY=' "ZAI key is scrubbed from the claude process"
assert_contains "${out}" '^--dangerously-skip-permissions$' "extra arguments are forwarded"

echo "== claude-zai: .env.local fallback =="
out="${WORK}/claude-zai-envfile.txt"
(
    unset ZAI_API_KEY Z_AI_API_KEY
    export AI_BACKENDS_REPO_ROOT="${WORK}/zai-envroot"
    PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
        CLAUDE_ZAI_CONFIG_DIR="${WORK}/claude-zai-home2" \
        AI_BACKENDS_CONTAINER_MODE=no CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB=0 \
        "${BACKENDS}" claude-zai >>"${out}" 2>&1
)
assert_contains "${out}" '^ANTHROPIC_AUTH_TOKEN=zai-file-key$' \
    "claude-zai resolves the Z.AI key from .env.local"

echo "== claude-zai: container mode disables the inner sandbox =="
out="${WORK}/claude-container.txt"
PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
    AI_BACKENDS_CONTAINER_MODE=yes CLAUDE_ZAI_SUBPROCESS_ENV_SCRUB=0 \
    ZAI_API_KEY="k2" "${BACKENDS}" claude-zai >>"${out}" 2>&1
assert_contains "${out}" '"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}' \
    "container mode passes the weaker-sandbox settings"

echo "== install: launchers and profile files =="
bin_dir="${WORK}/launchers"
mkdir -p "${WORK}/codex-home2"
assert "install exits zero" env -u ZAI_API_KEY -u Z_AI_API_KEY PATH="${SANDBOX_PATH}" \
    CODEX_HOME="${WORK}/codex-home2" AI_BACKENDS_BIN_DIR="${bin_dir}" \
    "${BACKENDS}" install
assert "codex-zai launcher is a symlink" test -L "${bin_dir}/codex-zai"
assert "claude-zai launcher is a symlink" test -L "${bin_dir}/claude-zai"
assert "profile toml exists" test -f "${WORK}/codex-home2/devcontainer-zai.config.toml"
assert "model catalog exists" test -f "${WORK}/codex-home2/devcontainer-zai-models.json"
assert "catalog lists glm-5.3" grep -q 'glm-5.3' "${WORK}/codex-home2/devcontainer-zai-models.json"
assert "profile uses responses endpoint" grep -q 'https://api.z.ai/api/v1' "${WORK}/codex-home2/devcontainer-zai.config.toml"
assert_not_contains "${WORK}/codex-home2/devcontainer-zai.config.toml" 'API_KEY=' "profile keeps the key out of the toml"
assert "codex home is 0700" test "$(stat -c %a "${WORK}/codex-home2" 2>/dev/null || stat -f %Lp "${WORK}/codex-home2")" = "700"
assert "profile toml is 0600" test "$(stat -c %a "${WORK}/codex-home2/devcontainer-zai.config.toml" 2>/dev/null || stat -f %Lp "${WORK}/codex-home2/devcontainer-zai.config.toml")" = "600"

echo "== install: refuses to replace non-symlink launchers =="
mkdir -p "${WORK}/launchers2"
printf '#!/bin/sh\nexit 0\n' > "${WORK}/launchers2/codex-zai"
if PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home2" AI_BACKENDS_BIN_DIR="${WORK}/launchers2" \
    "${BACKENDS}" install >/dev/null 2>"${WORK}/refuse.err"; then
    bad "install should refuse to replace a non-symlink launcher"
else
    assert_contains "${WORK}/refuse.err" 'non-symlink' "refusal message explains the problem"
fi

echo "== codex-openrouter: profile, model, and key forwarding =="
out="${WORK}/codex-or.txt"
OPENROUTER_API_KEY="or-key-codex" record "${out}" codex-openrouter
assert_contains "${out}" '^--profile$' "codex-openrouter passes --profile"
assert_contains "${out}" '^devcontainer-openrouter$' "codex-openrouter uses the openrouter profile"
assert_contains "${out}" 'model_provider="OPENROUTER"' "codex-openrouter pins the OPENROUTER provider"
assert_contains "${out}" '^--model$' "codex-openrouter passes --model"
assert_contains "${out}" '^openai/gpt-5.6-sol$' "codex-openrouter defaults to a current flagship model"
assert_contains "${out}" '^OPENROUTER_API_KEY=or-key-codex$' "codex-openrouter exports OPENROUTER_API_KEY"

echo "== codex-openrouter: model override and profile file contents =="
out="${WORK}/codex-or-model.txt"
RECORD_CODEX_HOME="${WORK}/codex-home-or" OPENROUTER_API_KEY="or-key" \
    CODEX_OPENROUTER_MODEL="anthropic/claude-sonnet-5" record "${out}" codex-openrouter
assert_contains "${out}" '^anthropic/claude-sonnet-5$' "CODEX_OPENROUTER_MODEL override applied"
assert "openrouter profile toml exists" test -f "${WORK}/codex-home-or/devcontainer-openrouter.config.toml"
assert "profile uses chat completions endpoint" grep -q 'base_url = "https://openrouter.ai/api/v1"' \
    "${WORK}/codex-home-or/devcontainer-openrouter.config.toml"
assert "profile uses the responses wire api" grep -q 'wire_api = "responses"' \
    "${WORK}/codex-home-or/devcontainer-openrouter.config.toml"
assert "profile uses command-backed auth" \
    grep -q '\[model_providers\.OPENROUTER\.auth\]' \
    "${WORK}/codex-home-or/devcontainer-openrouter.config.toml"
assert_not_contains "${WORK}/codex-home-or/devcontainer-openrouter.config.toml" 'env_key' \
    "env_key mode (skips the live model catalog) is not used"

echo "== codex-openrouter: auth command prints the key from the environment =="
# Extract the auth command exactly as Codex would parse it from the TOML
# basic string, then run it and compare stdout with the exported key.
auth_args="$(sed -n 's/^args = \["-c", "\(.*\)"\]$/\1/p' \
    "${WORK}/codex-home-or/devcontainer-openrouter.config.toml")"
assert "auth args line exists" test -n "${auth_args}"
auth_cmd="${auth_args//\\\"/\"}"
auth_out="$(OPENROUTER_API_KEY="probe-token" sh -c "${auth_cmd}")"
assert "auth command prints the exported key" test "${auth_out}" = "probe-token"
auth_out_empty="$(env -u OPENROUTER_API_KEY sh -c "${auth_cmd}")"
assert "auth command prints nothing without the key" test -z "${auth_out_empty}"

echo "== generated codex profiles parse as valid TOML (strict) =="
if command -v python3 >/dev/null 2>&1 && python3 -c 'import tomllib' >/dev/null 2>&1; then
    if python3 - "${WORK}/codex-home-or/devcontainer-openrouter.config.toml" <<'PY'
import sys, tomllib
with open(sys.argv[1], "rb") as f:
    data = tomllib.load(f)
prov = data["model_providers"]["OPENROUTER"]
assert prov["base_url"] == "https://openrouter.ai/api/v1", prov["base_url"]
assert prov["wire_api"] == "responses"
assert prov["auth"]["command"] == "sh"
assert prov["auth"]["args"] == ["-c", 'printf \'%s\' "${OPENROUTER_API_KEY}"'], prov["auth"]["args"]
assert "env_key" not in prov
assert data["shell_environment_policy"]["filters"] == {"OPENROUTER_API_KEY": "exclude"}
PY
    then
        ok "openrouter profile parses; auth args survive TOML escaping"
    else
        bad "openrouter profile TOML validation failed"
    fi
    if python3 - "${WORK}/codex-home/devcontainer-zai.config.toml" <<'PY'
import sys, tomllib
with open(sys.argv[1], "rb") as f:
    data = tomllib.load(f)
prov = data["model_providers"]["ZAI"]
assert prov["base_url"] == "https://api.z.ai/api/v1", prov["base_url"]
assert prov["wire_api"] == "responses"
assert prov["env_key"] == "ZAI_API_KEY"
assert "auth" not in prov
assert "experimental_bearer_token" not in prov
assert data["shell_environment_policy"]["filters"] == {
    "ZAI_API_KEY": "exclude", "Z_AI_API_KEY": "exclude"
}
PY
    then
        ok "zai profile parses; key resolution stays environment-based"
    else
        bad "zai profile TOML validation failed"
    fi
    catalog="${WORK}/codex-home/devcontainer-zai-models.json"
    if node -e "const c=require(process.argv[1]); if(c.models[0].slug!=='glm-5.3') process.exit(1)" "${catalog}"; then
        ok "zai model catalog parses as JSON"
    else
        bad "zai model catalog JSON validation failed"
    fi
else
    ok "python3/tomllib unavailable; strict TOML parse skipped"
fi
assert_not_contains "${WORK}/codex-home-or/devcontainer-openrouter.config.toml" 'or-key' \
    "openrouter profile keeps the raw key out of the toml"
assert "profile filters the key from subprocesses" grep -q 'OPENROUTER_API_KEY = "exclude"' \
    "${WORK}/codex-home-or/devcontainer-openrouter.config.toml"

echo "== codex-openrouter: missing key fails loudly =="
if env -u OPENROUTER_API_KEY AI_BACKENDS_REPO_ROOT="${WORK}/empty-root" PATH="${SANDBOX_PATH}" \
    CODEX_HOME="${WORK}/codex-home" "${BACKENDS}" codex-openrouter >/dev/null 2>"${WORK}/or-missing.err"; then
    bad "missing OPENROUTER_API_KEY should fail"
else
    assert_contains "${WORK}/or-missing.err" 'OPENROUTER_API_KEY' "missing key error mentions OPENROUTER_API_KEY"
fi

echo "== codex-openrouter: .env.local fallback =="
out="${WORK}/codex-or-envfile.txt"
mkdir -p "${WORK}/envroot"
printf '# comment\nOPENROUTER_API_KEY="or-key-from-envfile"\n' > "${WORK}/envroot/.env.local"
(
    unset OPENROUTER_API_KEY
    export AI_BACKENDS_REPO_ROOT="${WORK}/envroot"
    record "${out}" codex-openrouter
)
assert_contains "${out}" '^OPENROUTER_API_KEY=or-key-from-envfile$' ".env.local key is resolved (quotes stripped)"

echo "== claude-openrouter: endpoint, token, explicit empty ANTHROPIC_API_KEY =="
out="${WORK}/claude-or.txt"
env -u OPENROUTER_API_KEY PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
    CLAUDE_OPENROUTER_CONFIG_DIR="${WORK}/claude-or-home" \
    AI_BACKENDS_CONTAINER_MODE=no CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB=0 \
    OPENROUTER_API_KEY="or-key-claude" \
    ANTHROPIC_API_KEY=shadowed \
    ANTHROPIC_BASE_URL=https://evil.example \
    CLAUDE_CODE_USE_BEDROCK=1 \
    CLAUDE_CODE_USE_VERTEX=1 \
    "${BACKENDS}" claude-openrouter --model anthropic/claude-sonnet-5 >>"${out}" 2>&1
assert_contains "${out}" '^ANTHROPIC_AUTH_TOKEN=or-key-claude$' "claude-openrouter sets ANTHROPIC_AUTH_TOKEN"
assert_contains "${out}" '^ANTHROPIC_BASE_URL=https://openrouter.ai/api$' "claude-openrouter points at the OpenRouter anthropic endpoint"
assert_contains "${out}" '^ANTHROPIC_API_KEY=$' "ANTHROPIC_API_KEY is explicitly empty (OpenRouter requirement)"
assert_contains "${out}" '^ANTHROPIC_DEFAULT_SONNET_MODEL=anthropic/claude-sonnet-5$' "claude-openrouter maps sonnet to claude-sonnet-5"
assert_contains "${out}" '^ANTHROPIC_DEFAULT_OPUS_MODEL=anthropic/claude-opus-5$' "claude-openrouter maps opus to claude-opus-5"
assert_contains "${out}" '^ANTHROPIC_DEFAULT_HAIKU_MODEL=anthropic/claude-haiku-4.5$' "claude-openrouter maps haiku to claude-haiku-4.5"
assert_contains "${out}" '^ANTHROPIC_DEFAULT_FABLE_MODEL=anthropic/claude-fable-5.1$' "claude-openrouter maps the fable class"
assert_contains "${out}" '^CLAUDE_CODE_SUBAGENT_MODEL=' "claude-openrouter sets the subagent model"
assert_contains "${out}" '^CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY=1$' "gateway model discovery is enabled by default"
assert_contains "${out}" '^CLAUDE_CONFIG_DIR=' "claude-openrouter isolates CLAUDE_CONFIG_DIR"
assert_not_contains "${out}" '^CLAUDE_CODE_USE_BEDROCK=' "Bedrock selector is removed"
assert_not_contains "${out}" '^CLAUDE_CODE_USE_VERTEX=' "Vertex selector is removed"
assert_not_contains "${out}" '^OPENROUTER_API_KEY=' "OPENROUTER key is scrubbed from the claude process"
assert_contains "${out}" '^--model$' "extra arguments are forwarded"

echo "== claude-openrouter: model overrides and discovery opt-out =="
out="${WORK}/claude-or-model.txt"
PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
    CLAUDE_OPENROUTER_CONFIG_DIR="${WORK}/claude-or-home" \
    AI_BACKENDS_CONTAINER_MODE=no CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB=0 \
    OPENROUTER_API_KEY="or-key" \
    CLAUDE_OPENROUTER_SONNET_MODEL="openai/gpt-5.6-sol" \
    CLAUDE_OPENROUTER_GATEWAY_DISCOVERY=0 \
    "${BACKENDS}" claude-openrouter >>"${out}" 2>&1
assert_contains "${out}" '^ANTHROPIC_DEFAULT_SONNET_MODEL=openai/gpt-5.6-sol$' "CLAUDE_OPENROUTER_SONNET_MODEL override applied"
assert_contains "${out}" '^CLAUDE_CODE_ENABLE_GATEWAY_MODEL_DISCOVERY=0$' "gateway discovery can be disabled"

echo "== claude-openrouter: container mode disables the inner sandbox =="
out="${WORK}/claude-or-container.txt"
PATH="${SANDBOX_PATH}" CODEX_HOME="${WORK}/codex-home" \
    AI_BACKENDS_CONTAINER_MODE=yes CLAUDE_OPENROUTER_SUBPROCESS_ENV_SCRUB=0 \
    OPENROUTER_API_KEY="or-key" "${BACKENDS}" claude-openrouter >>"${out}" 2>&1
assert_contains "${out}" '"sandbox":{"enabled":false,"enableWeakerNestedSandbox":true}' \
    "container mode passes the weaker-sandbox settings"

echo "== install: launchers and profile files =="
bin_dir="${WORK}/launchers"
mkdir -p "${WORK}/codex-home2"
assert "install exits zero" env -u ZAI_API_KEY -u Z_AI_API_KEY PATH="${SANDBOX_PATH}" \
    CODEX_HOME="${WORK}/codex-home2" AI_BACKENDS_BIN_DIR="${bin_dir}" \
    "${BACKENDS}" install
assert "codex-zai launcher is a symlink" test -L "${bin_dir}/codex-zai"
assert "claude-zai launcher is a symlink" test -L "${bin_dir}/claude-zai"
assert "codex-openrouter launcher is a symlink" test -L "${bin_dir}/codex-openrouter"
assert "claude-openrouter launcher is a symlink" test -L "${bin_dir}/claude-openrouter"

echo "== env: exports managed credentials from environment and .env.local =="
printf 'ZAI_API_KEY=zai-file-key\nGITHUB_PERSONAL_ACCESS_TOKEN="gh-token-file"\nUNITY_PROJECT_PATH=/unity/path\nOPENROUTER_API_KEY="it'"'"'s quoted"\n' \
    > "${WORK}/zai-envroot/.env.local"
out="${WORK}/env-exports.txt"
(
    unset ZAI_API_KEY Z_AI_API_KEY ZHIPU_API_KEY OPENROUTER_API_KEY \
        GITHUB_TOKEN GH_TOKEN GITHUB_PERSONAL_ACCESS_TOKEN GITHUB_PAT \
        UNITY_MCP_BEARER_TOKEN UNITY_PROJECT_PATH
    export AI_BACKENDS_REPO_ROOT="${WORK}/zai-envroot"
    "${BACKENDS}" env
) >"${out}" 2>/dev/null
assert_contains "${out}" "^export ZAI_API_KEY='zai-file-key'\$" "env exports the Z.AI key"
assert_contains "${out}" "^export ZHIPU_API_KEY='zai-file-key'\$" \
    "env mirrors the Z.AI key into ZHIPU_API_KEY for native opencode"
assert_contains "${out}" "^export OPENROUTER_API_KEY='it'\\\\''s quoted'\$" \
    "env escapes single quotes for safe eval"
assert_contains "${out}" "^export GITHUB_TOKEN='gh-token-file'\$" \
    "env maps GitHub aliases onto GITHUB_TOKEN"
assert_contains "${out}" "^export UNITY_PROJECT_PATH='/unity/path'\$" "env exports UNITY_PROJECT_PATH"

echo "== env: unset keys are omitted and output evaluates cleanly =="
out="${WORK}/env-eval.txt"
(
    unset ZAI_API_KEY Z_AI_API_KEY ZHIPU_API_KEY OPENROUTER_API_KEY \
        GITHUB_TOKEN GH_TOKEN GITHUB_PERSONAL_ACCESS_TOKEN GITHUB_PAT \
        UNITY_MCP_BEARER_TOKEN UNITY_PROJECT_PATH
    export AI_BACKENDS_REPO_ROOT="${WORK}/empty-root"
    export ZAI_API_KEY="env-only-zai"
    "${BACKENDS}" env
) >"${out}" 2>/dev/null
eval_script="${WORK}/eval-env.sh"
cp "${out}" "${eval_script}"
assert "env output evaluates and sets the variable" \
    bash -c "set -e; . '${eval_script}'; test \"\${ZAI_API_KEY}\" = 'env-only-zai'"
assert_not_contains "${out}" 'OPENROUTER' "env omits keys absent from environment and .env.local"
assert_not_contains "${out}" 'UNITY' "env omits unset Unity values"

echo "== env: environment precedence over .env.local =="
out="${WORK}/env-precedence.txt"
(
    unset ZAI_API_KEY Z_AI_API_KEY
    export AI_BACKENDS_REPO_ROOT="${WORK}/zai-envroot"
    export ZAI_API_KEY="env-zai-wins"
    "${BACKENDS}" env
) >"${out}" 2>/dev/null
assert_contains "${out}" "^export ZAI_API_KEY='env-zai-wins'\$" "env honors environment precedence"

echo "== env-autoload: rc snippet exports .env.local credentials for native agents =="
AUTOLOAD="${SCRIPT_DIR}/../../../.devcontainer/env-autoload.sh"
assert "env-autoload snippet exists" test -f "${AUTOLOAD}"
mkdir -p "${WORK}/autoload-root/.devcontainer"
cp "${BACKENDS}" "${WORK}/autoload-root/.devcontainer/ai-backends.sh"
printf 'ZAI_API_KEY=zai-file-key\nOPENROUTER_API_KEY=or-file-key\n' > "${WORK}/autoload-root/.env.local"
out="${WORK}/autoload-env.txt"
(
    unset ZAI_API_KEY Z_AI_API_KEY ZHIPU_API_KEY OPENROUTER_API_KEY \
        GITHUB_TOKEN GH_TOKEN GITHUB_PERSONAL_ACCESS_TOKEN GITHUB_PAT \
        UNITY_MCP_BEARER_TOKEN UNITY_PROJECT_PATH
    DXT_WORKSPACE_ROOT="${WORK}/autoload-root" \
        bash -c '. "$1"; env | LC_ALL=C sort' _ "${AUTOLOAD}" >"${out}"
)
assert_contains "${out}" '^ZAI_API_KEY=zai-file-key$' "autoload exports the Z.AI key"
assert_contains "${out}" '^OPENROUTER_API_KEY=or-file-key$' "autoload exports the OpenRouter key"
assert_contains "${out}" '^ZHIPU_API_KEY=zai-file-key$' "autoload mirrors the Z.AI key for native opencode"
assert_not_contains "${out}" '^DXT_WORKSPACE_ROOT=' "autoload unsets its own plumbing variable"

echo "== env-autoload: environment precedence and safe no-op =="
out="${WORK}/autoload-precedence.txt"
(
    export ZAI_API_KEY="env-wins"
    DXT_WORKSPACE_ROOT="${WORK}/autoload-root" \
        bash -c '. "$1"; printf "%s\n" "${ZAI_API_KEY}"' _ "${AUTOLOAD}" >"${out}"
)
assert_contains "${out}" '^env-wins$' "autoload keeps environment precedence"

out="${WORK}/autoload-noop.txt"
(
    unset ZAI_API_KEY
    DXT_WORKSPACE_ROOT="${WORK}/missing-root" \
        bash -c '. "$1"; printf "exit=%s zai=%s\n" "$?" "${ZAI_API_KEY-UNSET}"' _ "${AUTOLOAD}" >"${out}" 2>&1
)
assert_contains "${out}" '^exit=0 zai=UNSET$' "autoload is a silent no-op without the checkout"

echo "== post-create: installs the autoload rc block idempotently =="
out="${WORK}/pc-home"
mkdir -p "${out}/.config"
# The devcontainer image ships .bashrc and .profile; ensure_env_local_autoload
# skips missing rc files (same contract as ensure_path_line).
touch "${out}/.bashrc" "${out}/.profile"
(
    # Sourcing post-create.sh only defines functions and enables -euo pipefail
    # (main is guarded); relax the flags for the assertions below.
    . "${SCRIPT_DIR}/../../../.devcontainer/post-create.sh"
    set +e +u
    HOME="${WORK}/pc-home"; export HOME
    ensure_env_local_autoload "${WORK}/autoload-root"
    ensure_env_local_autoload "${WORK}/autoload-root"
) >/dev/null 2>&1
assert "bashrc exists" test -f "${WORK}/pc-home/.bashrc"
assert "profile exists" test -f "${WORK}/pc-home/.profile"
assert "bashrc has exactly one autoload block" \
    test "$(grep -c 'dxcommandterminal .env.local autoload' "${WORK}/pc-home/.bashrc")" = "2"
assert "profile has exactly one autoload block" \
    test "$(grep -c 'dxcommandterminal .env.local autoload' "${WORK}/pc-home/.profile")" = "2"
assert "bashrc block pins the workspace root" \
    grep -Fq "DXT_WORKSPACE_ROOT='${WORK}/autoload-root'" "${WORK}/pc-home/.bashrc"
assert "bashrc block guards the snippet path" \
    grep -Fq "[ -f '${WORK}/autoload-root/.devcontainer/env-autoload.sh' ]" "${WORK}/pc-home/.bashrc"

echo ""
echo "ai-backends: ${PASS} passed, ${FAIL} failed"
if [[ "${FAIL}" -gt 0 ]]; then
    exit 1
fi
exit 0
