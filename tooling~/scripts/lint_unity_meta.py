r"""Unity .meta hygiene linter (issue #220).

Enforces three rules over `git ls-files`, so the check reflects what a fresh
clone (the remote) contains rather than local working-tree state:

1. ORPHAN META: every tracked `*.meta` must have its target tracked too. A
   target is present in the remote when it is a tracked file, or a directory
   that still contains tracked files (git does not track empty directories).
   Metas checked in without their data file produce missing-asset warnings
   for consumers and depend on locally generated files that are absent from
   the remote (e.g. gitignored build outputs).
2. MISSING META: every tracked file or directory visible to Unity (no
   dot-prefixed path segment, no `~`-suffixed segment) must have a tracked
   sibling `.meta`, so Unity never auto-generates a stub GUID.
3. GUIDS: every tracked `.meta` must declare exactly one 32-hex `guid:`, and
   GUIDs must be unique across the package.

Messages use the [meta-lint] prefix.

Stated limits: git output is read NUL-delimited (`ls-files -z`), so a
non-ASCII path round-trips without core.quotepath and backslash guards
become a no-op normalization; the line patterns match case-insensitively
(re.IGNORECASE; python's default is case-sensitive) with two exotic-byte
leniencies over the .NET text reader this contract preserves - python's
case-insensitivity Unicode-casefolds (a "guıd:" key would parse where
.NET's invariant culture would not) and python's \s folds \x1c-\x1f, which
.NET's \s does not - neither reachable from a meta Unity writes;
meta files read with utf-8-sig and split on CR/LF/CRLF only, the .NET
ReadAllLines set (str.splitlines would also split \x0b-\x1e and the
U+2028/2029 family, reading such lines shorter), while
bytes that are not UTF-8 decode with U+FFFD replacement the way .NET's
default decoder did - .NET's reader also BOM-sniffed UTF-16/32, which this
linter does not, so a UTF-16 meta fails closed as an unparsable guid;
traversal is sorted, so the report order is deterministic.

Entry points:
- pre-commit hook `unity-meta-lint` (uv run --project tooling~ python ...)
- the Unity Meta Lint workflow
- the contract test suite next to it (test_lint_unity_meta.py)
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path
from typing import Protocol

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

META_SUFFIX = ".meta"

# PowerShell's -match is case-insensitive; the port keeps that (Unity writes
# the keys lowercase, and a hand-written case drift stays a parseable line).
GUID_LINE = re.compile(r"guid:\s*([0-9a-fA-F]{32})\s*", re.IGNORECASE)
FOLDER_ASSET_LINE = re.compile(r"folderAsset:\s*yes\s*", re.IGNORECASE)

# .NET's ReadAllLines split on exactly these three; str.splitlines() also
# splits \x0b-\x1e and the U+2028/2029 family, which would read a line the
# ps1 saw whole as several.
LINE_SEPARATORS = re.compile(r"\r\n|\r|\n")

PREFIX = "[meta-lint]"


class Stream(Protocol):
    """What the report writes through: anything with a text-mode write()."""

    def write(self, text: str, /) -> object: ...


def git_tracked_paths(repo_root: Path) -> list[str] | None:
    """The repo's tracked paths, NUL-delimited so no path can split; None on
    any git failure (non-zero exit, missing git, missing repo root)."""
    try:
        result = subprocess.run(
            ["git", "ls-files", "-z"],
            cwd=repo_root,
            capture_output=True,
            check=False,
        )
    except OSError:
        return None
    if result.returncode != 0:
        return None
    return [
        path.replace("\\", "/")
        for path in result.stdout.decode("utf-8", "surrogateescape").split("\0")
        if path
    ]


def ancestor_directories(path: str) -> list[str]:
    """Every ancestor directory of a tracked file: a directory "exists in the
    remote" iff it contains tracked files (git does not track empty
    directories)."""
    segments = path.split("/")
    return ["/".join(segments[:index]) for index in range(1, len(segments))]


def is_unity_ignored(path: str) -> bool:
    """Unity never imports a path with a dot-prefixed or ~-suffixed segment."""
    return any(
        segment.startswith(".") or segment.endswith("~") for segment in path.split("/")
    )


def lint_unity_meta(
    repo_root: Path,
    *,
    tracked_paths: list[str] | None = None,
    verbose_output: bool = False,
    stream: Stream = sys.stdout,
) -> int:
    """Runs the lint and returns the exit code: 0 clean, 1 on any error.
    Writes the same report the PowerShell linter wrote, through `stream`
    (tests capture it). tracked_paths is the `git ls-files` seam: None runs
    git, tests inject a synthetic listing so the rules run without spawning.
    """
    write = stream.write

    def emit(message: str) -> None:
        write(f"{message}\n")

    repo_root = Path(repo_root)
    if tracked_paths is None:
        tracked_paths = git_tracked_paths(repo_root)
        if tracked_paths is None:
            emit(f"{PREFIX} ERROR: git ls-files failed; is {repo_root} a git repository?")
            return 1

    tracked = sorted(tracked_paths)
    tracked_set = set(tracked)
    tracked_directories = sorted({parent for path in tracked for parent in ancestor_directories(path)})

    orphan_errors = 0
    missing_errors = 0
    guid_errors = 0
    guids_by_path: dict[str, str] = {}

    for file in tracked:
        if not file.endswith(META_SUFFIX):
            continue

        meta_path = repo_root / file
        try:
            # errors="replace" mirrors .NET's default ReadAllLines decode:
            # bytes that are not UTF-8 become U+FFFD instead of a read error.
            lines = LINE_SEPARATORS.split(meta_path.read_text(encoding="utf-8-sig", errors="replace"))
        except OSError as error:
            emit(f"{PREFIX} ERROR: '{file}' could not be read: {error}")
            guid_errors += 1
            continue

        guid_matches = [match.group(1).lower() for line in lines if (match := GUID_LINE.fullmatch(line))]

        if not guid_matches:
            emit(f"{PREFIX} ERROR: '{file}' has no parsable 32-hex 'guid:' line")
            guid_errors += 1
            continue

        if len(guid_matches) > 1:
            emit(f"{PREFIX} ERROR: '{file}' declares multiple guid: lines")
            guid_errors += 1
            continue

        guid = guid_matches[0]
        if guid in guids_by_path:
            emit(f"{PREFIX} ERROR: duplicate guid '{guid}' in '{file}' and '{guids_by_path[guid]}'")
            guid_errors += 1
        else:
            guids_by_path[guid] = file
            if verbose_output:
                emit(f"{PREFIX} OK: {file}: guid unique")

        # Legacy folder metas (historical Unity output) may omit
        # 'folderAsset: yes' and still work, so the validity rule is simply
        # that the target exists in the remote: a tracked file, or a tracked
        # directory (git does not track empty directories).
        is_folder_asset = any(FOLDER_ASSET_LINE.fullmatch(line) for line in lines)
        target = file[: -len(META_SUFFIX)]
        if target not in tracked_set and target not in tracked_directories:
            emit(f"{PREFIX} ERROR: orphan meta: '{file}' has no tracked target '{target}'")
            orphan_errors += 1
        elif is_folder_asset and target in tracked_set:
            emit(f"{PREFIX} ERROR: folder meta '{file}' targets a tracked file, not a directory")
            orphan_errors += 1

    for file in tracked:
        if file.endswith(META_SUFFIX) or is_unity_ignored(file):
            continue

        if file + META_SUFFIX not in tracked_set:
            emit(f"{PREFIX} ERROR: missing meta: '{file}' is visible to Unity but '{file}.meta' is not tracked")
            missing_errors += 1
        elif verbose_output:
            emit(f"{PREFIX} OK: {file}: meta present")

    for directory in tracked_directories:
        if is_unity_ignored(directory):
            continue

        if directory + META_SUFFIX not in tracked_set:
            emit(
                f"{PREFIX} ERROR: missing meta: directory '{directory}' is visible to Unity "
                f"but '{directory}.meta' is not tracked"
            )
            missing_errors += 1
        elif verbose_output:
            emit(f"{PREFIX} OK: {directory}: meta present")

    total_checked = len(tracked) + len(tracked_directories)
    emit("")
    emit("=" * 60)
    emit(
        f"{PREFIX} Summary: {total_checked} tracked files/directories checked "
        f"(orphan metas: {orphan_errors}, missing metas: {missing_errors}, guid problems: {guid_errors})"
    )

    if orphan_errors == 0 and missing_errors == 0 and guid_errors == 0:
        emit("Unity meta hygiene passed")
        return 0

    emit("Unity meta hygiene failed - see errors above")
    return 1


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validates Unity .meta file hygiene for every tracked file in the repo.",
    )
    parser.add_argument(
        "-v",
        "--verbose-output",
        action="store_true",
        help="emit per-file OK status",
    )
    parser.add_argument(
        "--repo-root",
        default=str(REPO_ROOT),
        help="repository root (default: the checkout this script lives in)",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    # A tracked path whose bytes are not UTF-8 carries surrogates after the
    # decode; printing it must not crash the report the ps1 printed lossily.
    for console in (sys.stdout, sys.stderr):
        reconfigure = getattr(console, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(errors="backslashreplace")
    return lint_unity_meta(Path(args.repo_root), verbose_output=args.verbose_output)


if __name__ == "__main__":
    sys.exit(main())
