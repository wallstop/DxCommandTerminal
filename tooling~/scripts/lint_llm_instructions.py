"""Validate skill frontmatter, generated index, and agent pointer contracts."""

from __future__ import annotations

import argparse
import io
import re
import stat
import sys
from pathlib import Path
from tempfile import TemporaryDirectory
from typing import TextIO

from generate_skills_index import REPO_ROOT, WHITESPACE, generate_skills_index, trim

PREFIX = "[llm-lint]"
FRONTMATTER = re.compile(r"\A---\r?\n(.*?\r?\n)---\r?\n", re.DOTALL)
NAME = re.compile(r"^[a-z0-9]+(?:-[a-z0-9]+)*$")
CATEGORIES = ("Core", "Performance", "Feature")
ENTRYPOINTS = (
    ("AGENTS.md", "](./.llm/context.md)", "AGENTS.md (Codex/OpenCode/nanocoder) entrypoint"),
    ("CLAUDE.md", "](./.llm/context.md)", "Claude Code entrypoint"),
    (".cursorrules", "](./.llm/context.md)", "Cursor agent entrypoint"),
    (".github/copilot-instructions.md", "](../.llm/context.md)", "Copilot agent entrypoint"),
)


def read_text(path: Path) -> str:
    """ReadAllText detects BOMs and preserves line endings."""
    data = path.read_bytes()
    encoding = "utf-8-sig"
    if data.startswith((b"\xff\xfe\x00\x00", b"\x00\x00\xfe\xff")):
        encoding = "utf-32"
    elif data.startswith((b"\xff\xfe", b"\xfe\xff")):
        encoding = "utf-16"
    return data.decode(encoding, errors="replace")


def hidden_directory(path: Path) -> bool:
    if sys.platform == "win32":
        return bool(getattr(path.stat(), "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_HIDDEN)
    return path.name.startswith(".")


def utf16_length(value: str) -> int:
    return len(value.encode("utf-16-le", errors="surrogatepass")) // 2


def frontmatter_failures(path: Path) -> list[str]:
    content = read_text(path)
    label = f"{path.parent.name}/SKILL.md"
    match = FRONTMATTER.match(content)
    if match is None:
        return [f"{label}: missing '---' delimited YAML frontmatter"]
    values: dict[str, str] = {}
    for line in re.split(r"\r?\n", match[1]):
        pair = re.match(rf"^(name|description):{WHITESPACE}*(.*)$", line, re.IGNORECASE)
        category = re.match(rf"^{WHITESPACE}{{2,}}category:{WHITESPACE}*(.*)$", line, re.IGNORECASE)
        if pair:
            values[pair[1].lower()] = trim(pair[2]).strip("\"'")
        elif category:
            values["category"] = trim(category[1]).strip("\"'")
    failures: list[str] = []
    name = values.get("name", "")
    if not name:
        failures.append("frontmatter is missing required 'name'")
    elif utf16_length(name) > 64:
        failures.append(f"name is {utf16_length(name)} chars (max 64)")
    elif not NAME.fullmatch(name):
        failures.append(f"name '{name}' must be lowercase a-z/0-9 with single interior hyphens")
    elif name.lower() != path.parent.name.lower():
        failures.append(f"name '{name}' must match parent directory name '{path.parent.name}'")
    description = values.get("description", "")
    if not trim(description):
        failures.append("frontmatter is missing required 'description'")
    elif description.startswith(("|", ">")):
        failures.append("description must be a single inline line (block scalers '|'/'>' are not supported)")
    elif utf16_length(description) > 1024:
        failures.append(f"description is {utf16_length(description)} chars (max 1024)")
    else:
        # .NET regex matches UTF-16 units; isolated surrogates print as replacements.
        units = description.encode("utf-16-le", errors="surrogatepass")
        bad_units = dict.fromkeys(int.from_bytes(units[i:i + 2], "little")
                                  for i in range(0, len(units), 2)
                                  if int.from_bytes(units[i:i + 2], "little") > 127)
        bad = ["\ufffd" if 0xD800 <= unit <= 0xDFFF else chr(unit) for unit in bad_units]
        if bad:
            failures.append(f"description contains non-ASCII character(s) [{' '.join(bad)}] (use ASCII: '-' not em-dash, straight quotes)")
    category = values.get("category")
    if category is not None and category not in CATEGORIES:
        failures.append(f"unknown metadata.category '{category}' (expected: {', '.join(CATEGORIES)})")
    if not trim(content[match.end():]):
        failures.append("skill body is empty")
    return [f"{label}: {failure}" for failure in failures]


def lint_llm_instructions(repo_root: Path = REPO_ROOT, *, fix: bool = False,
                          verbose_output: bool = False, stream: TextIO = sys.stdout) -> int:
    def write(message: str = "") -> None:
        stream.write(message + "\n")

    def info(message: str) -> None:
        if verbose_output:
            write(f"{PREFIX} {message}")

    def error(message: str) -> None:
        write(f"{PREFIX} ERROR: {message}")

    def success(message: str) -> None:
        write(f"{PREFIX} {message}")

    skills = repo_root / ".llm/skills"
    context = repo_root / ".llm/context.md"
    index = skills / "index.md"
    generator = Path(__file__).with_name("generate-skills-index.ps1")
    required = [(skills, "Skills directory"), (context, "context.md"),
                (generator, "generate-skills-index.ps1")]
    required.extend((repo_root / path, label) for path, _, label in ENTRYPOINTS)
    info("Checking required paths...")
    for path, label in required:
        if not path.exists():
            error(f"{label} not found at: {path}")
            return 1
    exit_code = 0
    entries: list[Path] = []
    # Authored names are ASCII; preserve PowerShell's case-insensitive directory order.
    for directory in sorted(skills.iterdir(), key=lambda item: item.name.lower()):
        if not directory.is_dir() or hidden_directory(directory):
            continue
        path = directory / "SKILL.md"
        if path.exists():
            entries.append(path)
        else:
            error(f"Skill directory '{directory.name}' has no SKILL.md.")
            exit_code = 1
    if not entries:
        error(f"No skills found under {skills} (expected .llm/skills/<name>/SKILL.md).")
        return 1
    write("\nValidating SKILL.md frontmatter...")
    failures = [failure for path in entries for failure in frontmatter_failures(path)]
    if failures:
        for failure in failures:
            error(failure)
        exit_code = 1
    else:
        success(f"All {len(entries)} SKILL.md files have valid frontmatter")
    write("\nValidating skills index (index.md)...")

    def generate(destination: Path | None, label: str) -> bool:
        result = generate_skills_index(repo_root, output_path=destination,
                                       verbose_output=verbose_output, stream=stream)
        if result:
            error(f"{label} (generator exit {result}).")
        return result == 0

    with TemporaryDirectory() as temporary:
        first, second = Path(temporary) / "first.md", Path(temporary) / "second.md"
        if not generate(first, "Generator failed writing expected index"):
            return 1
        if not generate(second, "Generator failed on determinism re-run"):
            return 1
        if first.read_bytes() != second.read_bytes():
            error("Generator is NON-deterministic: two runs produced different bytes.")
            exit_code = 1
        else:
            info("Generator is deterministic (identical bytes across two runs).")
        expected = read_text(first).replace("\r\n", "\n")
        if not index.exists():
            if fix:
                if not generate(None, "Generator failed while creating missing index.md"):
                    return 1
                success("Generated missing index.md")
            else:
                error("index.md does not exist. Run: pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1")
                return 1
        current = read_text(index).replace("\r\n", "\n")
        if current != expected:
            if fix:
                if not generate(None, "Generator failed during -Fix"):
                    return 1
                success("Regenerated index.md")
            else:
                error("Skills index index.md is out of date!")
                expected_lines, current_lines = expected.split("\n"), current.split("\n")
                shown = 0
                for position in range(max(len(expected_lines), len(current_lines))):
                    exp = expected_lines[position] if position < len(expected_lines) else "(missing)"
                    cur = current_lines[position] if position < len(current_lines) else "(missing)"
                    if exp.lower() != cur.lower():
                        write(f"  Line {position + 1}:\n    Expected: {exp}\n    Current:  {cur}")
                        shown += 1
                        if shown == 5:
                            break
                write("Run: pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1 (or this lint with -Fix)")
                exit_code = 1
        else:
            success("Skills index is up to date")
    data = index.read_bytes()
    has_bom, has_cr = data.startswith(b"\xef\xbb\xbf"), b"\r" in data
    if has_bom:
        error("index.md has a UTF-8 BOM; it must be written without a BOM (cross-OS stability).")
        exit_code = 1
    if has_cr:
        error("index.md contains CR (CRLF); it must use LF line endings.")
        exit_code = 1
    if not has_bom and not has_cr:
        info("index.md encoding OK (UTF-8 no BOM, LF).")
    write("\nValidating context.md...")
    content = read_text(context)
    for marker in ("<!-- BEGIN GENERATED SKILLS INDEX -->", "<!-- END GENERATED SKILLS INDEX -->"):
        if marker in content:
            error(f"context.md still contains a stale embedded-index marker: {marker}")
            write("The index lives in .llm/skills/index.md; remove the embedded block.")
            exit_code = 1
    if not re.search(r"\]\(\./skills/index\.md\)", content, re.IGNORECASE):
        error("context.md must link to the generated index (a link to ./skills/index.md).")
        exit_code = 1
    h1_count = len(re.findall(r"^# (?!#)", content, re.MULTILINE))
    if h1_count != 1:
        error(f"context.md must have exactly one H1 (found {h1_count}).")
        exit_code = 1
    for path, link, label in ENTRYPOINTS:
        if link not in read_text(repo_root / path):
            error(f"{label} must delegate to .llm/context.md via {link}.")
            exit_code = 1
    write("\n" + "=" * 60)
    if exit_code:
        error("LLM instructions validation failed!")
    else:
        success("LLM instructions validation passed!")
    write("=" * 60)
    return exit_code


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=REPO_ROOT)
    parser.add_argument("--fix", action="store_true")
    parser.add_argument("--verbose-output", action="store_true")
    args = parser.parse_args(argv)
    return lint_llm_instructions(args.repo_root, fix=args.fix, verbose_output=args.verbose_output)


if __name__ == "__main__":
    if isinstance(sys.stdout, io.TextIOWrapper):
        sys.stdout.reconfigure(encoding="utf-8", newline="\n")
    sys.exit(main())
