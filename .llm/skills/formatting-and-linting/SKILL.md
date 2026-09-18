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

## Enum and Unity object review

- Give every enum member an explicit value. Reserve `Unknown = 0` or `None = 0`
  with `[Obsolete("Use a valid value")]`; use named valid defaults for optional APIs.
- Preserve shipped nonzero numeric identities. `TerminalLogType` keeps Unity's
  serialized `LogType` mapping, including `Error = 0`; never renumber it silently.
- Flags can still use typed `default` for an empty mask. Obsolete marks source
  references; it does not reject runtime values. Validate state at its boundary.
- Whitelist valid execution environments before checking flag membership:
  `HasFlag(0)` is true and cannot establish a valid execution context.
- When moving a former valid zero, audit optional builder defaults, serialized
  fields, and struct defaults. An invalid allocation verdict must not report zero.
- Prefer `obj == null` / `obj != null` for Unity objects. These include destroyed
  native objects; `is null` and `ReferenceEquals` only test managed identity.
- Do not add redundant null guards to fresh synchronous Find results. Keep guards
  on public inputs and retained objects that may have been destroyed.
- Verify APIs against each version gate's actual documentation or references;
  a shim compilation cannot prove an overload exists in older Unity versions.

## Pre-commit hooks

`.pre-commit-config.yaml` currently runs:

1. `dotnet-tool-restore` (pre-commit, pre-push, post-checkout, post-rewrite)
2. `csharpier` on staged `*.cs` files
3. C# style linters (Node, whole-tree scans; each has an `npm --prefix tooling~ run lint:<name>`
   script, a contract-test suite under `tooling~/scripts/tests/`, and a CI step):
   - `comparison-direction` (only `<`, `<=`, `==`; `:fix` swaps operands)
    - `member-ordering` (member order, nested types last, explicit enum values and obsolete zero sentinels;
      `:fix` only reorders members, never renumbers enums)
   - `multiline-comments` (stacked `//` become one `/* */` block; `:fix` converts)
   - `unity-null-patterns` (bans `Assert.IsNull`/`Assert.IsNotNull`; `:fix` converts to
     `Assert.That(x == null / x != null)`; issue #100)
   - `linq-production` (no LINQ in `Runtime/`, `Editor/`; no `:fix`)
   - `string-equality` (no `==`/`!=` on string literals or `string.Empty` in shipped code; use
     `string.Equals` with an explicit `StringComparison`; no `:fix`)
   - `theme-palette-tokens` (USS theme tokens + palette fallbacks)
4. LLM-context linters: `lint-llm-instructions.ps1`, `lint-skill-sizes.ps1` (see
   [manage-skills](../manage-skills/SKILL.md))

Fixer rule: every `:fix` rewriter splices at offsets its own scan recorded (token
`.start`/`.end`, scan-walk indices), never at offsets recomputed from token text widths.
Trivia between tokens (spaces, comments, line breaks) is legal C# the tokenizer skips
silently, so a width-derived cut lands inside a token and the rewrite emits invalid C#
(Bugbot on PR #102; pinned by the trivia-splice contract test in
`lint-unity-null-patterns.test.mjs`). When adding a fixer, contract-test at least one
shape with trivia between every token the fixer consumes.

Run everything manually with `pre-commit run --all-files`. If a hook rewrites files, re-stage and
retry the commit.
