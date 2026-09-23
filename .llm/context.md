# LLM Agent Instructions

Procedural skills live in the [skills/](./skills/) directory. See the generated
[Skills Index](./skills/index.md) for the full catalog with trigger conditions.

Every skill is a `SKILL.md` folder following the [Agent Skills](https://agentskills.io) format
(`name` + `description` frontmatter, metadata for grouping). Front-end pointer files
(`AGENTS.md`, `CLAUDE.md`, `.cursorrules`, `.github/copilot-instructions.md`) are thin wrappers
that delegate here; this file is the single source of truth.

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
generated, never hand-edited; `npm test` runs the Node suite; `npm run preflight` = all gates parallel (~3-4s).

## Skills Reference

See the generated [Skills Index](./skills/index.md). Regenerate it after adding or editing any
skill: `pwsh -NoProfile -File tooling~/scripts/generate-skills-index.ps1` (validated by
`tooling~/scripts/lint-llm-instructions.ps1`).

### SKILL.md Contract

Each skill is a directory `.llm/skills/<skill-name>/SKILL.md` where: frontmatter `name` is
required (`a-z0-9-`, 1-64 chars, no leading/trailing/double hyphens, MUST match the parent
directory name), `description` is required (single-line, ASCII-only, 1-1024 chars, keyword-rich
for discovery: what it does + when to use it), `metadata.category` is one of `Core`,
`Performance`, `Feature` (default `Feature`), and the body stays under the line limit below.

Adding a skill: create the directory + `SKILL.md`, then run the generator. The linter enforces
frontmatter validity, index freshness, and pointer-file delegation; see
[manage-skills](./skills/manage-skills/SKILL.md).

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
7. String comparisons state their rule: `string.Equals(a, b, StringComparison.Ordinal|OrdinalIgnoreCase)`;
   string-typed `IndexOf`/`StartsWith`/`EndsWith`/`Contains`/`Replace` take an explicit `StringComparison` - never
   culture-sensitive defaults, never `==`/`!=` on string values (`== null`, enums, and Unity references stay legal).
   Name-shaped identifiers (font/theme/command names) are `OrdinalIgnoreCase`. Enforced by
   `npm --prefix tooling~ run lint:string-equality` (pre-commit + CI; identifier-vs-identifier equality is a review convention).
8. Use `nameof()` instead of magic strings.
9. Internal APIs over reflection on our own code; `InternalsVisibleTo` is already granted to the
   Editor and Tests.Runtime assemblies (`Runtime/AssemblyInfo.cs`).
10. Annotate format-string methods with `[StringFormatMethod("...")]` (JetBrains) so callers get
    format checking.
11. `foreach` over collections with value-typed enumerables (`List<T>`, arrays, structs).
    Counting `for` only when the index is used, the collection is `IReadonlyList`, or the
    count direction/skip matters. Convert last-element separator logic to a first/last flag.
12. One top-level type (class/struct/enum/delegate) per file. Nested helper types are fine.
13. Assign `out` parameters immediately before each `return`, per path; never blanket-assign at method entry - that
    defeats the compiler's definite-assignment bugcheck. Enforced by `npm --prefix tooling~ run lint:out-param-discipline` (pre-commit + CI, #119).
14. Flags enums hold single-bit members only. Named composites (`Gameplay`, `All`) live in a
    static presets class; never compose members inside the enum.
15. No raw bitwise flag math at call sites; use the allocation-free
    `EnumExtensions.HasFlagNoAlloc` helper (adapted from unity-helpers, MIT).
16. No sentinel/magic values where the type system can express optionality: use nullable
    types (`int?`, nullable structs). `CommandInfo.maxArgCount`/`AddCommand` use `int?`
    (legacy negatives normalize); `RegisterCommandAttribute.MaxArgCount` stays `int`
    (attribute properties cannot be nullable). Optional fields take nullable types, never
    `-1` sentinels (caret markers, inspector selection/popup state, PR #76). Normalize
    IndexOf/FindIndex results to `null` at the field boundary; guard with `HasValue`/`is
    not int x` (lifted `null < 0` is `false`); IMGUI `Popup` takes/returns `int` (`-1` =
    none) - coalesce there via `GetValueOrDefault(-1)`. Null helper returns banned too
    (`TryXxx`/`out`/`Array.Empty`, PR #67); prefer `Math.Max`/`Math.Clamp` over `?:` clamps.
17. No `params` on frequently-called APIs; provide fixed-arity overloads (`params` allocates).
    One-time configuration APIs may use `params`.
18. Hot-path collection access avoids interface dispatch: specialize arrays and `List<T>`
    (Unity does not de-virtualize `IReadOnlyList` indexers); arrays are preferred (bound-check
    elision). Copy with `Array.Copy` / `CopyTo`, not element loops; reserve `Clone()` for
    cases where its `object` return is acceptable. Counting loops hoist `List<T>.Count`,
    interface `Count`, and UIToolkit `childCount` reads out of the condition (per-iteration
    property/interface dispatch, PR #73 review); keep `array.Length`/`string.Length` inline
    (bounds-check elision / inlined read); never hoist when the body mutates the collection.
19. Comparison operators read left-to-right in ascending order: only `<`, `<=` and `==`. Never
    `>` or `>=` -- write `0 <= index` and `b < a`, not `index >= 0` or `a > b` (issue #51).
    Enforced by `npm --prefix tooling~ run lint:comparison-direction` (pre-commit + CI; `:fix`
    swaps operands, refusing rewrites where both sides can have side effects).
20. One member ordering across every C# type: const, events, delegates, static properties,
    static fields, properties, fields, constructors, static methods, methods; each tier
    public > protected > internal > private; nested types go at the END of their containing
    type (issue #50). Enforced by `npm --prefix tooling~ run lint:member-ordering` (pre-commit
    + CI; `:fix` is a permutation-only reorder that never crosses `#if` boundaries).
21. Multi-line comments are block comments: two or more consecutive comment-only `//` lines
    must be one `/* ... */` block; single `//` lines and `///` doc comments stay legal.
    Enforced by `npm --prefix tooling~ run lint:multiline-comments` (pre-commit + CI; `:fix`
    converts runs, refusing content that contains the block-comment close).
22. No LINQ in production code (`Runtime/`, `Editor/`) - every operator allocates
    enumerators/closures and some copy whole sequences: no `using System.Linq`, no qualified
    `System.Linq.` calls, no static `Enumerable.` calls. Plain loops over the concrete
    collection type; reuse caller-owned buffers (`List<T>` fill/`Clear`, `CopyTo`) instead of
    intermediate sequences; `List<T>.ToArray()`/`CopyTo` instance methods stay legal. Tests
    and `Generator~` tooling are exempt. Enforced by `npm --prefix tooling~ run
    lint:linq-production` (pre-commit + CI; no `:fix` by design).
23. Replacing LINQ is not enough - the loop must not re-introduce the allocation (PR #60):
    - String assembly on repeated paths rents `CachedStringBuilder.Rent(capacity)` /
      `Return(builder)` (`Runtime/Helper/`, ThreadStatic); never `new StringBuilder()` per
      call; multi-part messages interpolate (`$"..."`), no 3+ operand `+` chains (PR #76).
    - Derived data drawn every `OnGUI`/editor tick is cached against its source (reference +
      count stamp; e.g. the `TerminalUIEditor` popup/font-key arrays), rebuilt only when the
      source changes - a fresh array/list per frame is a regression even when allocation-free.
    - Snapshot-then-mutate (clear all variables while iterating a dictionary) lives on the
      owning type with a cached buffer field (`CommandShell.ClearVariables`), not in
      command handlers that build throwaway lists.
    - When converting LINQ, `foreach` over the concrete type (struct enumerator,
      bounds-check elision); counting loops only where the index is genuinely used (rule 11).
24. Enum state checks whitelist the valid states (`state is TerminalState.OpenSmall or
    TerminalState.OpenFull` via a helper like `TerminalUI.IsOpenState`), never blacklist
    (`!= Closed`) - `TerminalState`/`HintDisplayMode` carry an obsolete `Unknown = 0`
    sentinel, and a blacklist silently treats invalid/serialized values (and any future
    enum member) as the non-sentinel branch (PR #82 review). Acronyms in identifiers
    stay all-caps: `TeardownUI`, not `TeardownUi`.
25. UnityEngine.Object null checks use the Unity `==`/`!=` operators explicitly. Banned on Unity
    objects: `?.`, `??`/`??=`, `is null`/`is not null`/`ReferenceEquals` (all bypass the fake-null
    operator), `Assert.IsNull`/`Assert.IsNotNull` in tests, and implicit truthiness - write
    `component != null` / `Assert.That(x == null)`. `lint:unity-null-patterns` enforces the Assert
    half (`:fix` converts); the repo-internal `UnityObjectNullPatternAnalyzer` DxCmd0001-0005
    type-checks the rest, warnaserror fails the compile; it never ships and no-ops outside the
    repo's assemblies - keep both layers (issues #98/#100). Delegate `?.`/plain C# `??` stay legal.

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
5. Files on the generator-tests CI path filter (`Runtime/Attributes/RegisterCommandAttribute.cs`,
   `CommandArg.cs`, `CommandCatalogEntry.cs`, `Runtime/Analyzers/**`) get compiled under
   `EnableNETAnalyzers` on netstandard2.0 in CI: no `string.Contains(char)` (CA1307), no
   `Contains(string)` for single chars (CA1847), and no `Contains(char, StringComparison)`
   (missing on netstandard2.0). Use `0 <= s.IndexOf(' ', StringComparison.Ordinal)` and
   `string.Replace(" ", ..., StringComparison.Ordinal)`; explicit null checks before
   dereference in public methods (CA1062). When converting a `?.` chain to an IndexOf
   guard, carry the null check over - dropping it turns a typed definition error into a
   NullReferenceException (Bugbot finding on PR #67; pinned by a null-name test).
   Reproduce with `dotnet test Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators.Tests`.

### Command Registration (Quick Reference)

- `[RegisterCommand(Help = "...", MinArgCount = 2, MaxArgCount = 2)]` on a static method;
  command name is inferred from the method name (`COMMAND` infix/suffix/prefix stripped),
  overridable via `Name = "..."`.
- Non-static commands register manually: `Terminal.Shell.AddCommand(name, handler, min, max, help)`.
- Builder commands: `CommandBuilder.Create(...)` with `.Arg<T>`, `.Subcommand`, and
  disposable handles. Definition-time misconfiguration throws
  `CommandConfigurationException` (an `InvalidOperationException` subclass carrying
  `CommandName`, a `CommandConfigurationFailure` kind, and the argument/subcommand/type
  scope; wrong-type `Get<T>` reads throw `CommandArgumentTypeMismatchException`) -
  tests must assert that exact type and branch on `Failure`, never on message text.
- Completion providers always receive a context scoped to the command's own arguments:
  `ActiveArgumentIndex`/`PrecedingArguments` are relative to that command, and the router shifts them per
  routing level (`CommandCompletionContext.ForSubcommand`). Never hand a provider a parent-shifted context.
- Details: [register-terminal-command](./skills/register-terminal-command/SKILL.md).

### User-Facing Copy (STE)

All text a human reads (PR titles, PR descriptions, commit messages, review comments,
code comments, issues) uses Simplified Technical English: extremely short, simple,
direct. A few sentences is the ceiling, not the target - cut before adding. PRs cover
how (plus why/what): `Why` 1-2 sentences, `What` 3-6 one-line bullets, optional 1-3
evidence lines, ~12 lines total. Commit bodies ~8 lines. No per-file tours, no process
narration, no restated context. Code comments state only what the code cannot say.
Enforced for PRs by `npm --prefix tooling~ run lint:pr-copy` and the pr-copy CI job
(`tooling~/scripts/lint-pr-copy.mjs`: disclosure first line, section structure, line and
bullet budgets; the Cursor Bugbot summary block is stripped before checking).
Check before opening or editing a PR.
Details: [simple-writing](./skills/simple-writing/SKILL.md).

### CHANGELOG (user-facing only)

`CHANGELOG.md` records only changes a package consumer can observe: public API and
serialized-data changes, behavior changes, fixes, install-size or console-output changes.
Internal work (refactors, tooling, style enforcement, linters, measurement, CI lanes)
stays out, and so do internal numbers (timings, allocation counts, test tallies) - those
live in PR descriptions, issues, and `progress/` logs. If a user cannot observe the
difference, it does not belong in the changelog; new `Unreleased` entries keep the
existing `Added/Changed/Fixed/Removed` buckets per the policy in the file header.

### LLM Attribution (GitHub)

LLM-generated comments, issues, PR descriptions, and reviews start with `DISCLOSURE: LLM-GENERATED TEXT` as the first line.
Never auto-respond to outside contributors; summarize and wait for wallstop.
Details: [llm-attribution](./skills/llm-attribution/SKILL.md).

### Argument Parsing (Quick Reference)

- `args[i].TryGet<T>(out T value)` returns `false` on failure instead of silently defaulting.
- Built-in support covers primitives, `DateTime`/`Guid`, enums, Unity math types (`Vector2`,
  `Color`, `Quaternion`, ...), plus named-constant matching (`"red"`, `"MaxValue"`).
- Custom parsers: `CommandArgParser.RegisterParser<T>(parser, force)`; per-call overload
  `TryGet<T>(out T, parser)`. Details: [custom-argument-parsing](./skills/custom-argument-parsing/SKILL.md).

## Testing

- Tests are PlayMode tests under `Tests/Runtime/` (Unity Test Runner); harness components live in
  `Tests/Runtime/Components/`. Run via Unity Test Runner or Unity CLI `-runTests`.
- Command-behavior tests are data-driven over the static facades; see
  `Tests/Runtime/CommandArgTests.cs` and `CommandShellTests.cs` for the house style.
- See [run-terminal-tests](./skills/run-terminal-tests/SKILL.md) before writing or debugging tests.

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
