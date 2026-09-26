#!/usr/bin/env bash
# shellcheck shell=bash

# Install or migrate the guarded interactive-shell block. The function is
# sourced by lifecycle scripts; it never loads credentials by itself.
install_env_local_autoload() (
    local workspace_root="$1"
    local root_quoted marker end_marker block rc_file temporary rewritten
    if command -v flock >/dev/null 2>&1; then
        exec 9>"${TMPDIR:-/tmp}/dxt-env-autoload.lock"
        if ! flock -w 30 9; then
            printf '%s\n' 'Timed out waiting for the environment autoload lock.' >&2
            return 1
        fi
    fi
    root_quoted="${workspace_root//\'/\'\\\'\'}"
    marker="# >>> dxcommandterminal .env.local autoload >>>"
    end_marker="# <<< dxcommandterminal .env.local autoload <<<"
    block=$(cat <<EOF
${marker}
DXT_WORKSPACE_ROOT='${root_quoted}'
if [ -f '${root_quoted}/.devcontainer/env-autoload.sh' ]; then
    . '${root_quoted}/.devcontainer/env-autoload.sh'
fi
${end_marker}
EOF
)

    for rc_file in "$HOME/.bashrc" "$HOME/.profile"; do
        [[ -f "${rc_file}" ]] || continue

        # The rewrite below drops every line between a start marker and its end
        # marker, so only proceed on a well-formed file: markers balanced in
        # order and not nested. Otherwise refuse and let a human repair it.
        if ! awk -v start="${marker}" -v end="${end_marker}" '
            $0 == start { depth++; next }
            $0 == end { depth--; if (depth < 0) exit 1; next }
            END { exit (depth == 0) ? 0 : 1 }
        ' "${rc_file}"; then
            printf 'Malformed autoload block in %s; fix it by hand.\n' "${rc_file}" >&2
            return 1
        fi

        if grep -Fqx "${marker}" "${rc_file}" \
            && grep -Fqx "${end_marker}" "${rc_file}" \
            && [[ "$(grep -Fxc "${marker}" "${rc_file}")" -eq 1 ]] \
            && grep -Fqx "DXT_WORKSPACE_ROOT='${root_quoted}'" "${rc_file}" \
            && grep -Fqx "if [ -f '${root_quoted}/.devcontainer/env-autoload.sh' ]; then" "${rc_file}"; then
            continue
        fi

        temporary="$(mktemp "${rc_file}.dxt-autoload.XXXXXX")"
        rewritten="$(mktemp "${rc_file}.dxt-autoload.XXXXXX")"
        trap 'rm -f "${temporary}" "${rewritten}"' EXIT
        if ! awk -v start="${marker}" -v end="${end_marker}" '
            $0 == start { skip = 1; next }
            $0 == end { skip = 0; next }
            !skip { print }
        ' "${rc_file}" >"${temporary}"; then
            return 1
        fi
        # Build the whole file first: a failed write must not truncate the rc.
        {
            printf '\n%s\n' "${block}"
            cat "${temporary}"
        } >"${rewritten}" || return 1
        cat "${rewritten}" >"${rc_file}" || return 1
        rm -f "${temporary}" "${rewritten}"
        trap - EXIT
    done
)
