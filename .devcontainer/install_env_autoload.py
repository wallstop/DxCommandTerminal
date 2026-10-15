"""Install the guarded shell loader without interpreting workspace paths or rc bytes."""
from __future__ import annotations

import io
import os
from pathlib import Path
import stat
import sys
import tempfile

START = b"# >>> dxcommandterminal .env.local autoload >>>"
END = b"# <<< dxcommandterminal .env.local autoload <<<"


def replacement(source: bytes, workspace: str, rc: Path) -> bytes:
    quoted = os.fsencode(workspace).replace(b"'", b"'\\''")
    block = (START + b"\nDXT_WORKSPACE_ROOT='" + quoted + b"'\nif [ -f '" + quoted
             + b"/.devcontainer/env-autoload.sh' ]; then\n    . '" + quoted
             + b"/.devcontainer/env-autoload.sh'\nfi\n" + END + b"\n")
    kept: list[bytes] = []
    inside = False
    blocks = 0
    for line in io.BytesIO(source):
        # Match the shell's LF-delimited markers, without decoding unrelated bytes.
        marker = line.removesuffix(b"\n")
        if marker == START:
            if inside:
                raise ValueError(f"Malformed autoload block in {rc}; fix it by hand.")
            inside = True
            blocks += 1
        elif marker == END:
            if not inside:
                raise ValueError(f"Malformed autoload block in {rc}; fix it by hand.")
            inside = False
        elif not inside:
            kept.append(line)
    if inside:
        raise ValueError(f"Malformed autoload block in {rc}; fix it by hand.")
    if source.endswith(block) and blocks == 1:
        return source
    prefix = b"".join(kept)
    if prefix and not prefix.endswith(b"\n"):
        prefix += b"\n"
    return prefix + b"\n" + block


def write_rc(rc: Path, contents: bytes) -> None:
    # Replace the symlink's target, retaining the link and the target's permissions.
    target = rc.resolve(strict=True)
    descriptor, name = tempfile.mkstemp(prefix=target.name + ".dxt-autoload.", dir=target.parent)
    temporary = Path(name)
    try:
        with os.fdopen(descriptor, "wb") as output:
            output.write(contents)
            output.flush()
            os.fchmod(output.fileno(), stat.S_IMODE(target.stat().st_mode))
        os.replace(temporary, target)
    finally:
        temporary.unlink(missing_ok=True)


def install(workspace: str, home: Path) -> None:
    if "\n" in workspace or "\r" in workspace:
        raise ValueError("Workspace path must not contain line breaks.")
    changes: list[tuple[Path, bytes]] = []
    for rc in (home / ".bashrc", home / ".profile"):
        if not rc.is_file():
            continue
        source = rc.read_bytes()
        updated = replacement(source, workspace, rc)
        if updated != source:
            changes.append((rc, updated))
    for rc, updated in changes:
        write_rc(rc, updated)


def main(arguments: list[str]) -> int:
    if len(arguments) != 1:
        print("Usage: install_env_autoload.py WORKSPACE", file=sys.stderr)
        return 1
    try:
        install(arguments[0], Path.home())
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
