"""Require OpenCode v2 and matching optional aliases before container readiness."""
from __future__ import annotations

import re
import shutil
import subprocess
import sys

PREFIX = "[verify-opencode]"


def version(command: str) -> str:
    try:
        result = subprocess.run([command, "--version"], stdout=subprocess.PIPE, check=True)
    except (OSError, subprocess.CalledProcessError) as exception:
        raise ValueError(f"{command} --version failed") from exception
    # Bash command substitution removes trailing newlines, not other whitespace.
    return result.stdout.decode("utf-8", errors="replace").rstrip("\n")


def main() -> int:
    try:
        if shutil.which("opencode") is None:
            raise ValueError("opencode is not installed")
        primary = version("opencode")
        if re.search(r"(^|[ \t\r\n\v\f])v?2\.", primary, flags=re.ASCII) is None:
            raise ValueError(f"OpenCode v2 is required; found {primary or 'empty output'}")
        alias = shutil.which("opencode2")
        if alias is not None:
            secondary = version("opencode2")
            if primary != secondary:
                raise ValueError(f"opencode2 ({secondary or 'empty output'}) does not match opencode ({primary})")
        suffix = " (opencode and opencode2 agree)" if alias is not None else ""
        print(f"{PREFIX} OpenCode {primary}{suffix}")
        return 0
    except ValueError as exception:
        print(f"{PREFIX} ERROR: {exception}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
