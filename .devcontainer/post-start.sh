#!/usr/bin/env bash
# shellcheck shell=bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec python3 "${SCRIPT_DIR}/post_start.py" "$@"
