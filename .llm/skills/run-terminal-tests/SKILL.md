---
name: run-terminal-tests
description: Write and run DxCommandTerminal PlayMode tests (Unity Test Runner, Tests/Runtime/, NUnit) following the repo's test style for CommandArg, CommandShell, and Terminal behavior. Use when adding tests, running the test suite, or debugging failing terminal tests.
metadata:
  category: Feature
---

# Run Terminal Tests

## Layout

- `Tests/Runtime/` - PlayMode tests, asmdef `WallstopStudios.DxCommandTerminal.Tests.Runtime`
  (references the Runtime assembly; `InternalsVisibleTo` already grants internal access).
- `Tests/Runtime/Components/` - harness pieces: `TestCommands.cs` (attribute-registered test
  commands), `TerminalInputHandler.cs` (input simulation), `StartTracker.cs`.

## Running

1. Unity Test Runner: Window > General > Test Runner -> PlayMode tab -> Run All.
2. Unity CLI (CI-style):
   `Unity -batchmode -projectPath <proj> -runTests -testPlatform PlayMode -testResults results.xml -quit`
   (requires a valid Unity license; exit code reflects test success).

## House style (from existing suites)

- File per type under test: `CommandArgTests.cs`, `CommandShellTests.cs`, `TerminalTests.cs`,
  `CommandHistoryTests.cs`, `TerminalKeyboardControllerTests.cs`, `TryEatArgumentTests.cs`.
- Tests are plain NUnit (`[Test]`) over the static facades (`Terminal`, `Terminal.Shell`) - no
  scene setup needed for command/parsing behavior.
- Data-driven cases use `[TestCase]` rows instead of loops; failure messages assert on values,
  not just booleans.
- Names are PascalCase without underscores in test method bodies; mirrors the C# rules in
  `context.md`.

## Adding tests

1. New command behavior -> extend the matching suite; new command fixtures go in
   `Components/TestCommands.cs` so registration scanning picks them up.
2. Input behavior -> simulate through `Components/TerminalInputHandler.cs` rather than injecting
   raw key events.
3. Terminal lifecycle (open/close, resize, buffer wrap) -> `TerminalTests.cs` covers the
   component; reuse its setup helpers.
4. Keep runtime allocations in assertions minimal; suites run in PlayMode on every change.

## Driving tests from agents

After editing files outside Unity, confirm the editor compiled the intended
content before trusting a run: the host sync can lag, and stale assemblies
produce misleading failures. Check a canary (a log line, an assert message, or
a shifted line number in the failure stack) against the current file.

## UI test timing (frame-coupled reads)

`TerminalUI` applies programmatic value and caret writes through `RefreshUI` on
`LateUpdate`, and UI Toolkit applies them on its own schedule after that. In a
throttled or freshly initialized panel these passes can lag several frames, so
any assertion read one `yield return null` after `CompleteCommand` or a direct
field write is frame-coupled and flakes under session sequences
(issue #56). Rules:

- After `CompleteCommand` (or any code-driven field write), poll with the
  bounded helpers in `TerminalUITokenCompletionTests` - `WaitForInput` /
  `WaitForCaret` poll a few frames before asserting - instead of
  `yield return null` + immediate read.
- An exhausted poll that still finds the queued position pending is legitimate
  for unfocused panels; see the helper comments for what each fallback pins.
- Do not pin "consumed on frame N" behavior: the caret marker consumption
  (`ApplyPendingCaret`) depends on real focus landing, which synthetic panels
  may never do. Pin that logic synchronously by calling `ApplyPendingCaret`
  directly (see `PendingCaretWritesAndKeepsMarkerWhileUnfocused`).
- One failure mode survives everything: long agent sessions can leave the
  editor in a state where panel events stop processing entirely (writes
  re-clamp or never land, for 30+ frames). It clears with a domain reload
  (Assets > Refresh). If a previously green UI suite fails with stale values
  across several consecutive runs, refresh first, then re-run before hunting a
  code bug.
- Palette caret flakes: `CommandPaletteUI._logCaretPasses = true` (editor
  eval or a test) logs every pending-caret pass with frame, pending,
  cursor/select, and focus owner, so a #74-class re-clamp shows which pass
  moved the caret. The caret is parked only after it holds for two passes
  (`CaretStickPasses`), and a field change cancels the queued caret
  (`_pendingCaretIndex` is `int?`; null = none).
- Known order-dependent flakes on unmodified master (observed 2026-09-14):
  `TerminalUITokenCompletionTests.QuotedTokensAcceptUnquotedInsertions` and
  `CommandPaletteTests.TabAppliesArgumentCompletionWithQuoting` can fail inside full-suite
  runs (stale autocomplete candidate applied, or a stale caret read - e.g. `"pickup "pickaxe"`
  instead of `"pickup "torch"`, caret 13/9 vs 21) while passing in isolation both before and
  after a code change. Before hunting a regression, re-run the failing test isolated; an
  isolated PASS after a full-run FAIL is suite-order flake, and a domain reload clears the
  stale panel state between attempts. Root cause class: readiness polls too short for
  throttled panels (session-023 raised the token-completion and palette poll budgets to 600
  frames, and `TerminalUITokenCompletionTests.SetInput` now readiness-polls the caret park,
  which cleared this class from full-suite runs).
- UITK clamps `cursorIndex` writes to the last LAID-OUT text length, not
  the value length. The cap converges with layout and its convergence is
  nondeterministic under session sequences: a fresh field can sit capped
  below the value length for a whole poll budget, and re-focusing does
  not force it. UI tests must not assume a full-length caret park sticks;
  either readiness-poll the park or probe for a holdable position and pin
  position-independent rules against it (see
  `CommandPaletteTests.PendingCaretConsumesOnlyAfterStablePasses`).

## Debugging failures

- Errors are queued on the terminal (not only the last one) - assert on the full error set where
  relevant.
- Static state leaks between tests (`Terminal.Shell`, registered parsers, control sets on
  `CommandArg`) - reset or isolate when a test mutates global state, see
  [custom-argument-parsing](../custom-argument-parsing/SKILL.md) for the mutable statics list.
