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

## Debugging failures

- Errors are queued on the terminal (not only the last one) - assert on the full error set where
  relevant.
- Static state leaks between tests (`Terminal.Shell`, registered parsers, control sets on
  `CommandArg`) - reset or isolate when a test mutates global state, see
  [custom-argument-parsing](../custom-argument-parsing/SKILL.md) for the mutable statics list.
