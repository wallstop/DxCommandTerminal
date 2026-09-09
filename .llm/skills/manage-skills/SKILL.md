---
name: manage-skills
description: Create, edit, or remove agentic skills (.llm/skills/*/SKILL.md), regenerate the skills index, or fix lint failures around SKILL.md frontmatter, line limits, or pointer-file delegation. Use when adding new skills, changing skill descriptions/categories, touching .llm/**, or when scripts/lint-llm-instructions.ps1 or lint-skill-sizes.ps1 fail.
metadata:
  category: Core
---

# Manage Skills

## Layout

```text
.llm/
  context.md                     # Single source of truth; pointer files delegate here
  skills/
    index.md                     # GENERATED - never hand-edit
    <skill-name>/SKILL.md        # One directory per skill (agentskills.io format)
scripts/
  generate-skills-index.ps1      # Index generator (deterministic)
  lint-llm-instructions.ps1      # Contract linter (-Fix regenerates the index)
  lint-skill-sizes.ps1           # Line-limit linter
  tests/                         # Red-green tests for the linters and generator
```

## Creating a skill

1. Create `.llm/skills/<skill-name>/SKILL.md`. The directory name IS the skill name.
2. Frontmatter rules (enforced by `lint-llm-instructions.ps1`):
   - `name`: required, `a-z0-9-` only, 1-64 chars, no leading/trailing/double hyphens, and must
     equal the parent directory name.
   - `description`: required, SINGLE LINE, ASCII-only, 1-1024 chars. Describe what the skill does
     AND when to use it, with concrete keywords. Non-ASCII (em-dash, smart quotes) fails the lint:
     it is the known cross-OS index-drift vector.
   - `metadata.category`: `Core`, `Performance`, or `Feature` (default `Feature`).
3. Body: instructions, examples, edge cases. Reference sibling files with relative paths from the
   skill root. Keep the file at or below 300 lines (`lint-skill-sizes.ps1`; 270+ warns).
4. Regenerate the index:
   `pwsh -NoProfile -File scripts/generate-skills-index.ps1`
5. Run both linters; commit `.llm/skills/index.md` together with the skill.

```sh
pwsh -NoProfile -File scripts/lint-llm-instructions.ps1 -Fix
pwsh -NoProfile -File scripts/lint-skill-sizes.ps1 -VerboseOutput
```

## Editing a skill

Change the body freely; if you change `description` or `category`, regenerate the index. The
description is loaded by agents at startup for every skill - keep it under ~2 sentences but
keyword-rich.

## Removing a skill

Delete the skill directory and regenerate the index. Check `context.md` and sibling skills for
links to the removed skill.

## Index generation invariants

- Output is byte-identical across OSes: UTF-8 no BOM, LF endings, ordinal (culture-invariant)
  sorting, no timestamps. The linter runs the generator twice and byte-compares to catch drift.
- Sections are grouped by category: Core, then Performance, then Feature; each table is sorted
  ordinally by skill name.
- If the index is stale, `lint-llm-instructions.ps1 -Fix` repairs it; CI fails without `-Fix`.

## Pointer-file contract

`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, and `.github/copilot-instructions.md` must contain a
markdown link to `./.llm/context.md` (or `../.llm/context.md` for the `.github/` copy). They stay
thin wrappers; never inline real guidance there.

## Common lint failures

| Message | Cause | Fix |
| --- | --- | --- |
| `name ... must match parent directory` | Renamed dir without frontmatter | Align them |
| `description must be single line` | Folded/multiline YAML | Join into one ASCII line |
| `non-ASCII character(s)` | Smart quotes, em-dash | Replace with ASCII |
| `Skills index ... out of date` | Edited skills without regen | Run generator (or `-Fix`) |
| `has a UTF-8 BOM` / `contains CR` | Wrong editor wrote the index | Regenerate, do not hand-edit |
| `MUST split` | File >300 lines | Split body into a `references/` file in the skill dir |
