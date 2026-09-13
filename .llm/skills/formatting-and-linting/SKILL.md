---
name: formatting-and-linting
description: Run CSharpier and the repo's pre-commit enforcement for DxCommandTerminal (dotnet tools, formatting C# files, style linters, fixing format-check or lint failures). Use when formatting code, adding pre-commit hooks, or when a commit is rejected by the csharpier, dotnet-tool-restore, or any lint-* hook.
metadata:
  category: Core
---

# Formatting And Linting

## Toolchain

| Tool | Source | Command |
| --- | --- | --- |
| CSharpier 1.1.2 | `.config/dotnet-tools.json` | `dotnet tool run csharpier format` |
| pre-commit | `.pre-commit-config.yaml` | `pre-commit run --all-files` |

Restore tools first (`dotnet tool restore`) - the `dotnet-tool-restore` pre-commit hook runs it
on every commit and fails fast when tools are missing.

## Formatting

1. Format only the files you touched:
   `dotnet tool run csharpier format -- <file1> <file2>`
2. Whole repo check: `dotnet tool run csharpier format --check .` (or run pre-commit).
3. NEVER hand-adjust CSharpier output (spacing, braces, wrapping). If output looks wrong, the fix
   is upstream CSharpier config, not manual edits.

## Style contract (from `.editorconfig` + README)

- `using` directives inside the namespace block; `#if` blocks inside namespace.
- Explicit types over `var`; explicit access modifiers everywhere.
- Braces always (`csharp_prefer_braces`).
- Modifier order: `public private protected internal file new static abstract virtual sealed
  readonly override extern unsafe volatile async required`.
- Files: UTF-8 BOM + CRLF for C# assets (repo default). Do not "fix" line endings on untouched
  files - that pollutes diffs. Exceptions: `.llm/**` and `tooling~/scripts/**` are UTF-8 no BOM + LF
  (enforced by `.editorconfig` overrides and the LLM linters).

## Pre-commit hooks

`.pre-commit-config.yaml` currently runs:

1. `dotnet-tool-restore` (pre-commit, pre-push, post-checkout, post-rewrite)
2. `csharpier` on staged `*.cs` files
3. C# style linters (Node, whole-tree scans; each has an `npm --prefix tooling~ run lint:<name>`
   script, a contract-test suite under `tooling~/scripts/tests/`, and a CI step):
   - `comparison-direction` (only `<`, `<=`, `==`; `:fix` swaps operands)
   - `member-ordering` (one member order; nested types last; `:fix` reorders)
   - `multiline-comments` (stacked `//` become one `/* */` block; `:fix` converts)
   - `linq-production` (no LINQ in `Runtime/`, `Editor/`; no `:fix`)
   - `string-equality` (no `==`/`!=` on string literals or `string.Empty` in shipped code; use
     `string.Equals` with an explicit `StringComparison`; no `:fix`)
   - `theme-palette-tokens` (USS theme tokens + palette fallbacks)
4. LLM-context linters: `lint-llm-instructions.ps1`, `lint-skill-sizes.ps1` (see
   [manage-skills](../manage-skills/SKILL.md))

Run everything manually with `pre-commit run --all-files`. If a hook rewrites files, re-stage and
retry the commit.
