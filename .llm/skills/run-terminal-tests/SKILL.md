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

## Debugging failures

- Errors are queued on the terminal (not only the last one) - assert on the full error set where
  relevant.
- Static state leaks between tests (`Terminal.Shell`, registered parsers, control sets on
  `CommandArg`) - reset or isolate when a test mutates global state, see
  [custom-argument-parsing](../custom-argument-parsing/SKILL.md) for the mutable statics list.
