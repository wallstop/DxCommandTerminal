"""Line-limit linter for the authored LLM context files (issue #220 stage 1).

The typed-python port of tooling~/scripts/lint-skill-sizes.ps1. Checks every
authored file under .llm/ (context.md, skills, references, code samples - any
text file) against the size thresholds the repo's rule 1 enforcement defines:

- >300 lines: ERROR (must split/reduce - hard limit).
- 270-300 lines: CRITICAL warning, always shown; exit 0 unless fail_on_critical.
- <=269 lines: OK, reported under verbose output.

The generated .llm/skills/index.md is exempt: it is machine-written by
generate-skills-index.ps1, not an authored file.

Stated limit: python splits a few unicode characters as line breaks that
.NET's ReadAllLines does not (\v, \f, \x1c-\x1e, \x85, \u2028, \u2029), so
a file carrying one of those bytes inside a line counts one line more per
separator here - failing toward the stricter limit - and this walk
includes dot-prefixed files Get-ChildItem skipped, so a hidden file under
.llm/ is checked, not silently exempt.

Entry points (all keep their command names; this file is the implementation):
- pre-commit hook `llm-skill-sizes`
- the LLM Instructions Lint workflow, both OS legs
- the contract test suite next to it (test_lint_skill_sizes.py)
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

MAX_LINES = 300
CRITICAL_LINES = 270

PREFIX = "[skill-sizes]"

EXEMPT_RELATIVE_PATHS = frozenset({"skills/index.md"})


class FileSize:
    """One authored file's verdict: its repo-relative path and line count."""

    __slots__ = ("relative_path", "line_count")

    def __init__(self, relative_path: str, line_count: int) -> None:
        self.relative_path = relative_path
        self.line_count = line_count


def count_lines(path: Path) -> int:
    """Counts lines the way ReadAllLines does: any line ending splits, and a
    file without trailing content after its last newline has no phantom line.
    An empty file is zero lines."""
    return len(path.read_text(encoding="utf-8-sig").splitlines())


def authored_files(llm_dir: Path) -> list[FileSize]:
    """Every file under .llm/ except the machine-written index, sorted by
    repo-relative path for a deterministic report."""
    files = [
        FileSize(
            file.relative_to(llm_dir).as_posix(),
            count_lines(file),
        )
        for file in sorted(llm_dir.rglob("*"))
        if file.is_file()
    ]
    return [entry for entry in files if entry.relative_path not in EXEMPT_RELATIVE_PATHS]


def lint_skill_sizes(
    repo_root: Path,
    *,
    verbose_output: bool = False,
    fail_on_critical: bool = False,
    stream: object = sys.stdout,
) -> int:
    """Runs the lint and returns the exit code: 0 clean, 1 on any error, or on
    any critical band file when fail_on_critical is set. Writes the same report
    the PowerShell linter wrote, through `stream` (tests capture it)."""
    llm_dir = repo_root / ".llm"
    write = getattr(stream, "write")

    def emit(message: str) -> None:
        write(f"{message}\n")

    if not llm_dir.is_dir():
        emit(f"{PREFIX} ERROR: LLM directory not found at: {llm_dir}")
        return 1

    files = authored_files(llm_dir)
    error_count = 0
    critical_count = 0
    ok_count = 0
    exit_code = 0

    for entry in files:
        if entry.line_count > MAX_LINES:
            emit(
                f"{PREFIX} ERROR: {entry.relative_path}: {entry.line_count} lines "
                f"(max: {MAX_LINES}) - MUST split/reduce"
            )
            error_count += 1
            exit_code = 1
        elif entry.line_count >= CRITICAL_LINES:
            remaining = MAX_LINES - entry.line_count
            if remaining == 0:
                emit(
                    f"{PREFIX} CRITICAL: {entry.relative_path}: {entry.line_count} lines "
                    f"- AT the {MAX_LINES} line limit! Must split/reduce"
                )
            else:
                emit(
                    f"{PREFIX} CRITICAL: {entry.relative_path}: {entry.line_count} lines "
                    f"- only {remaining} lines from the {MAX_LINES} limit! Consider splitting now"
                )
            critical_count += 1
            if fail_on_critical:
                exit_code = 1
        else:
            if verbose_output:
                emit(f"{PREFIX} OK: {entry.relative_path}: {entry.line_count} lines")
            ok_count += 1

    emit("")
    emit("=" * 60)
    emit(
        f"{PREFIX} Summary: {len(files)} authored files checked "
        f"(limits: error >{MAX_LINES}, critical >={CRITICAL_LINES})"
    )
    if verbose_output or critical_count > 0 or error_count > 0:
        emit(f"{PREFIX}   OK: {ok_count}, Critical: {critical_count}, Errors: {error_count}")

    if exit_code == 0:
        emit("All files within size limits")
    else:
        emit("Some files exceed size limits - see errors above")
    emit("=" * 60)
    return exit_code


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validates LLM file sizes against the repo's line limits.",
    )
    parser.add_argument(
        "-v",
        "--verbose-output",
        action="store_true",
        help="emit per-file OK status",
    )
    parser.add_argument(
        "--fail-on-critical",
        action="store_true",
        help="exit non-zero when a file is in the 270-300 critical band",
    )
    parser.add_argument(
        "--repo-root",
        default=str(REPO_ROOT),
        help="repository root (default: the checkout this script lives in)",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    return lint_skill_sizes(
        Path(args.repo_root),
        verbose_output=args.verbose_output,
        fail_on_critical=args.fail_on_critical,
    )


if __name__ == "__main__":
    sys.exit(main())
