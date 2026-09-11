# LLM Agent Instructions

Procedural skills live in the [skills/](./skills/) directory. See the generated
[Skills Index](./skills/index.md) for the full catalog with trigger conditions.

Every skill is a `SKILL.md` folder following the [Agent Skills](https://agentskills.io) format
(`name` + `description` frontmatter, metadata for grouping). Front-end pointer files
(`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, `.github/copilot-instructions.md`) are thin wrappers
that delegate here; this file is the single source of truth.

---

## Repository Overview

**Package**: `com.wallstop-studios.dxcommandterminal`
**Version**: `1.0.0-rc25.0` (see `package.json`)
**Repository**: <https://github.com/wallstop/DxCommandTerminal>
**Root Namespace**: `WallstopStudios.DxCommandTerminal`
**Minimum Unity**: `2021.3`
**License**: MIT

**Design Principles**: Performance-first in-game console (O(1) buffer wrap, pooled allocations),
defensive input validation on all public APIs, immutable-by-default collections, zero warnings,
CSharpier-formatted code, self-documenting names over comments.

---

## Project Structure

```text
Runtime/                              # Runtime C# library (asmdef: WallstopStudios.DxCommandTerminal)
  Attributes/                         # RegisterCommandAttribute, DxShowIfAttribute
  CommandTerminal/
    Backend/                          # Terminal (static facade), CommandShell, CommandArg, CommandInfo,
                                      # CommandAutoComplete, CommandHistory, CommandLog, BuiltinCommands
    Input/                            # ITerminalInput, IInputHandler, keyboard + PlayerInput controllers
    Persistence/                      # TerminalThemeConfiguration(s), TerminalThemePersister
    Themes/                           # TerminalThemePack, TerminalFontPack, ThemeNameHelper
    UI/                               # TerminalUI, TerminalState (UI Toolkit)
  DataStructures/                     # Generic data structures used by the terminal
  Extensions/                         # Extension methods
  Helper/                             # Internal helpers
  AssemblyInfo.cs                     # InternalsVisibleTo: Editor + Tests.Runtime

Editor/                               # Editor-only tooling (asmdef: ...Editor)
  CustomEditors/                      # TerminalUIEditor, TerminalFontPackEditor, TerminalThemePackEditor
  Helper/                             # TerminalThemeStyleSheetHelper
  TerminalAssetPackPostProcessor.cs   # Asset post-processing for font/theme packs
  DxShowIfPropertyDrawer.cs           # Drawer for DxShowIfAttribute

Packs/                                # Built-in asset packs
  Fonts/                              # TerminalFontPack assets (Fira Mono, Courier Prime, ...)
  Themes/                             # TerminalThemePack assets

Styles/                               # USS/TSS stylesheets consumed by TerminalUI
Tests/Runtime/                        # PlayMode tests (asmdef: ...Tests.Runtime)
  Components/                         # Test harness components (TestCommands, TerminalInputHandler)
Media/                                # Screenshots and demo GIFs
tooling~/                             # Unity-hidden tooling (tilde-suffixed; not shipped in the UPM artifact)
  package.json                        # npm manifest (devDependencies; root package.json delegates via --prefix)
  scripts/                            # Repo tooling (PowerShell + Node)
    mcp/                              # unity-mcp.mjs: Unity MCP bridge/probe/configure/capture + tests
    tests/                            # Script regression suites (pwsh + bash)
.devcontainer/                        # VS Code devcontainer (Dockerfile, lifecycle, Z.AI launchers)
```

### Dev tooling (tooling~/scripts/, .devcontainer/)

`npm run unity:mcp:probe|configure|bridge|capture` drives the host Unity editor
over the authenticated MCP bridge in `tooling~/scripts/mcp/unity-mcp.mjs` (see the
[unity-mcp](./skills/unity-mcp/SKILL.md) and
[capture-unity-state](./skills/capture-unity-state/SKILL.md) skills). Credentials
live in gitignored `.env.local` (see `.env.example`); agent MCP configs are
generated, never hand-edited; `npm test` runs the Node tooling suite.

---

## Skills Reference

See the generated [Skills Index](./skills/index.md). Regenerate it after adding or editing any
skill: `pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1` (validated by
`tooling~/scripts/lint-llm-instructions.ps1`).

### SKILL.md Contract

Each skill is a directory `.llm/skills/<skill-name>/SKILL.md` where:

1. Frontmatter `name` is required, `a-z0-9-` only, 1-64 chars, no leading/trailing/double hyphens,
   and MUST match the parent directory name.
2. Frontmatter `description` is required, single-line, ASCII-only, 1-1024 chars, and describes
   both what the skill does and when to use it (keyword-rich for discovery).
3. `metadata.category` is one of `Core`, `Performance`, `Feature` (default `Feature`).
4. Body is the skill instructions; keep the whole file under the line limit below.

Adding a skill: create the directory + `SKILL.md`, then run the generator. The linter enforces
frontmatter validity, index freshness, and pointer-file delegation; see
[manage-skills](./skills/manage-skills/SKILL.md).

---

## Critical Rules Summary

### C# Code Rules

1. `using` directives go INSIDE the namespace block (see any file in `Runtime/`);
   `#if` blocks INSIDE namespace; `#define` at file top.
2. All code is formatted via [CSharpier](https://csharpier.com/):
   `dotnet tool run csharpier format` (tools pinned in `.config/dotnet-tools.json`, enforced by
   pre-commit). Do not hand-format.
3. Explicit types over `var`; explicit access modifiers on every member.
4. Classes/structs are `sealed`/`readonly` where possible; collections exposed immutably by
   default - mutable access only when necessary.
5. Zero warnings. No dead code, no commented-out code.
6. Prefer `TryXxx` patterns over exceptions for expected failures; validate inputs on all public
   methods (see `CommandArg.TryGet<T>` for the canonical example).
7. String comparisons are `OrdinalIgnoreCase` / ordinal - never culture-sensitive defaults.
8. Use `nameof()` instead of magic strings.
9. Internal APIs over reflection on our own code; `InternalsVisibleTo` is already granted to the
   Editor and Tests.Runtime assemblies (`Runtime/AssemblyInfo.cs`).
10. Annotate format-string methods with `[StringFormatMethod("...")]` (JetBrains) so callers get
    format checking.
11. `foreach` over collections with value-typed enumerables (`List<T>`, arrays, structs).
    Counting `for` only when the index is used, the collection is `IReadonlyList`, or the
    count direction/skip matters. Convert last-element separator logic to a first/last flag.
12. One top-level type (class/struct/enum/delegate) per file. Nested helper types are fine.
13. Assign `out` parameters immediately before each `return`, per path. Never blanket-assign
    them at method entry; that defeats the compiler's definite-assignment bugcheck.
14. Flags enums hold single-bit members only. Named composites (`Gameplay`, `All`) live in a
    static presets class; never compose members inside the enum.
15. No raw bitwise flag math at call sites; use the allocation-free
    `EnumExtensions.HasFlagNoAlloc` helper (adapted from unity-helpers, MIT).
16. No sentinel/magic values where the type system can express optionality: use nullable
    types (`int?`, nullable structs). `CommandInfo.maxArgCount` and `AddCommand` use `int?`
    (legacy negative values normalize to `null`). `RegisterCommandAttribute.MaxArgCount`
    keeps `int` because attribute properties cannot be nullable; `CommandInfo` normalizes.
17. No `params` on frequently-called APIs; provide fixed-arity overloads (`params` allocates).
    One-time configuration APIs may use `params`.
18. Hot-path collection access avoids interface dispatch: specialize arrays and `List<T>`
    (Unity does not de-virtualize `IReadOnlyList` indexers); arrays are preferred (bound-check
    elision). Copy with `Array.Copy` / `CopyTo`, not element loops; reserve `Clone()` for
    cases where its `object` return is acceptable.
19. Comparison operators read left-to-right in ascending order: only `<`, `<=` and `==`. Never
    `>` or `>=` -- write `0 <= index` and `b < a`, not `index >= 0` or `a > b` (issue #51).
    Enforced by `npm --prefix tooling~ run lint:comparison-direction` (pre-commit + CI; `:fix`
    swaps operands, refusing rewrites where both sides can have side effects).
20. One member ordering across every C# type: const, events, delegates, static properties,
    static fields, properties, fields, constructors, static methods, methods; each tier
    public > protected > internal > private, and nested types go at the END of their containing
    type (issue #50, per the unity-helpers rule). Static properties come before static fields
    and properties before fields by design; const takes the accessibility ordering too.
    Enforced by `npm --prefix tooling~ run lint:member-ordering` (pre-commit + CI; `:fix` is a
    permutation-only reorder that never crosses `#if` boundaries or type-load-initializer
    dependencies).
21. Multi-line comments are block comments: two or more consecutive comment-only `//` lines
    must be one `/*` ... `*/` block instead. Single `//` lines and `///` doc comments stay
    legal. Enforced by `npm --prefix tooling~ run lint:multiline-comments` (pre-commit + CI;
    `:fix` converts runs, refusing content that contains the block-comment close).

### Unity Package Rules

1. Every visible file/folder needs a `.meta` file (see existing `*.meta`). Dot-prefixed paths
   (`.llm/`, `.github/`, `.config/`, `.git/`) are ignored by Unity - never add `.meta` for them.
2. Assembly layout is fixed: `Runtime` (no editor refs), `Editor` (references Runtime + testable
   internals), `Tests.Runtime` (PlayMode). Adding an assembly requires updating
   `InternalsVisibleTo` usages too.
3. UI is [UI Toolkit](https://docs.unity3d.com/Manual/UIElements.html) (`TerminalUI` +
   `Styles/*.uss`/`*.tss`); theming flows through `TerminalThemePack` assets and
   `TerminalThemeStyleSheetHelper`, not per-code style mutations.
4. Runtime code must stay WebGL/IL2CPP-safe: no managed reflection-dependent APIs outside the
   command registration path (see [webgl-command-registration](./skills/webgl-command-registration/SKILL.md)).

### Command Registration (Quick Reference)

- `[RegisterCommand(Help = "...", MinArgCount = 2, MaxArgCount = 2)]` on a static method;
  command name is inferred from the method name (`COMMAND` infix/suffix/prefix stripped),
  overridable via `Name = "..."`.
- Non-static commands register manually: `Terminal.Shell.AddCommand(name, handler, min, max, help)`.
- Details: [register-terminal-command](./skills/register-terminal-command/SKILL.md).

### User-Facing Copy (STE)

All text a human reads (PR titles, PR descriptions, commit messages, review comments,
code comments, issues) uses Simplified Technical English: short sentences, active voice,
present tense, common words, ASCII only. PRs and commits follow a "Why" / "What changed" /
"How we know" order; omit sections that add nothing. Length budgets are hard limits:
PR descriptions stay at or under ~20 lines, commit bodies at or under ~12, one line per
bullet, no nested bullets. Code comments stay minimal - only what the code cannot say.
Details: [simple-writing](./skills/simple-writing/SKILL.md).

### CHANGELOG (user-facing only)

`CHANGELOG.md` records only changes a package consumer can observe: public API and
serialized-data changes, behavior changes, fixes, install-size or console-output changes.
Internal work (refactors, tooling, style enforcement, linters, measurement, CI lanes)
stays out, and so do internal numbers (timings, allocation counts, test tallies) - those
live in PR descriptions, issues, and `progress/` logs. If a user cannot observe the
difference, it does not belong in the changelog. New `Unreleased` entries follow the
policy stated in the file header; a PR that touches `CHANGELOG.md` keeps entries in the
existing `Added/Changed/Fixed/Removed` buckets.

### LLM Attribution (GitHub)

LLM-generated comments, issues, PR descriptions, and reviews start with
`DISCLOSURE: LLM-GENERATED TEXT` as the first line. Never auto-respond to outside
contributors; summarize and wait for wallstop.
Details: [llm-attribution](./skills/llm-attribution/SKILL.md).

### Argument Parsing (Quick Reference)

- `args[i].TryGet<T>(out T value)` returns `false` on failure instead of silently defaulting.
- Built-in support covers primitives, `DateTime`/`Guid`, enums, Unity math types (`Vector2`,
  `Color`, `Quaternion`, ...), plus named-constant matching (`"red"`, `"MaxValue"`).
- Custom parsers: `CommandArgParser.RegisterParser<T>(parser, force)`; per-call overload
  `TryGet<T>(out T, parser)`. Details: [custom-argument-parsing](./skills/custom-argument-parsing/SKILL.md).

---

## Testing

- Tests are PlayMode tests under `Tests/Runtime/` (Unity Test Runner); harness components live in
  `Tests/Runtime/Components/`.
- Command-behavior tests are data-driven over `CommandArg`/`CommandShell`/`Terminal` static
  facades; see `Tests/Runtime/CommandArgTests.cs` and `CommandShellTests.cs` for the house style.
- Run via Unity Test Runner (Window > General > Test Runner) or Unity CLI `-runTests`.
- See [run-terminal-tests](./skills/run-terminal-tests/SKILL.md) before writing or debugging tests.

---

## Enforcement (LLM Context Hygiene)

1. **Line limits**: every authored file under `.llm/` MUST stay at or below 300 lines; 270+ gets
   a critical warning. The generated `.llm/skills/index.md` is exempt (machine-written).
   Enforced by `tooling~/scripts/lint-skill-sizes.ps1` (pre-commit + CI + tests).
2. **SKILL.md validity + index freshness + pointer delegation**: enforced by
   `tooling~/scripts/lint-llm-instructions.ps1` (pre-commit + CI + tests).
3. **Generated files are byte-stable**: UTF-8 without BOM, LF line endings, ordinal sorting, no
   timestamps. Never hand-edit `.llm/skills/index.md`.
4. **Encoding overrides**: `.editorconfig` forces UTF-8 (no BOM) + LF for `.llm/**` and
   `tooling~/**` regardless of the repo defaults for C# assets.
5. `.editorconfig` charset/line-ending defaults for C# assets remain BOM/CRLF per repo
   convention; only the LLM-context paths above are overridden.

## Front-End Pointer Files

| File | Consumer | Contract |
| --- | --- | --- |
| `AGENTS.md` | Codex, OpenCode, nanocoder, Goose, most agents | Delegates to `./.llm/context.md` |
| `CLAUDE.md` | Claude Code | Delegates to `./.llm/context.md` |
| `.cursorrules` | Cursor | Delegates to `./.llm/context.md` |
| `.github/copilot-instructions.md` | GitHub Copilot | Delegates to `../.llm/context.md` |

These files stay minimal (a few lines). Skills are NOT copied into frontend-specific
directories; agents discover them via `.llm/skills/*/SKILL.md` (the standard Agent Skills
layout) referenced from this file and the skills index.
