---
name: run-terminal-tests
description: Write and run DxCommandTerminal PlayMode tests (Unity Test Runner, Tests/Runtime/, NUnit) following the repo's test style for CommandArg, CommandShell, and Terminal behavior. Use when adding tests, running the test suite, or debugging failing terminal tests.
metadata:
  category: Feature
---

# Run Terminal Tests

## Layout

- `Tests/Runtime/` - PlayMode tests, asmdef `WallstopStudios.DxCommandTerminal.Tests.Runtime`
  (references Runtime; `InternalsVisibleTo` grants internal access).
- `Tests/Editor/` - EditMode tests, asmdef `WallstopStudios.DxCommandTerminal.Tests.Editor`.
  EditMode-safe screens: no `[UnityTest]`/scene machinery; no default-context dispatch
  without pinning `CommandExecutionContext.AmbientContextProvider` to a Play Mode context
  in `[SetUp]` (`[UnityTest]` absence alone does not prove safety); no PlayMode-only
  test-infra dependency; no borrowed fixtures from the PlayMode assembly.
- `Tests/Runtime/Components/` - harness pieces: `TestCommands.cs` (attribute-registered test
  commands), `TerminalInputHandler.cs` (input simulation), `StartTracker.cs`.
- Stays in PlayMode because it is assembly-coupled: `CommandDiscoveryTests` (pins the
  allocation-free classification memo through the PlayMode allocation instrument) and
  `CommandCompatibilityBakeTests` (drives the bake over those fixtures and
  `Components/BakePartialFixtureCommands.cs`).
- Suites that pin assembly-level discovery contracts - the generated catalog, the
  TypeCache claim, catalog-less provider exclusion - need attributed `[RegisterCommand]`
  fixtures in their own assembly, which is what `Tests/Editor/DiscoveryFixtureCommands.cs`
  is for Tests.Editor.

## Running

1. Local preflight (fastest full gate, no Unity): `npm run preflight` - runs the
   node suite, the nine C#/asset linters, the T11 baseline gate, the package
   gate, docs guides build, and the compat compile in parallel (~3-4s wall vs
   ~30s serial). `--skip=a,b` narrows a run. Local success results are cached by
   their full input fingerprint. Use `--no-cache` for a clean run. CI and the
   compat compile do not use the cache.
2. One-command entry point per suite category (through the bridge; host editor
   up with the bridge running; exit 1 on any failure):

   | Suite category | Command |
   | --- | --- |
   | Functional (all EditMode + PlayMode) | `npm run unity:tests` |
   | Allocation | `npm run unity:tests -- --filter Allocation` |
   | Performance (standard-op benchmarks) | `npm run unity:tests -- --filter Benchmark` |
   | Graphics (T04 captures + T11 baselines) | `npm run t4:capture` |
   | Tooling (node) | `npm test` |

   `unity:tests` wraps `unity-mcp.mjs tests`: `--mode all|editmode|playmode`,
   `--filter` (the bridge's test-name filter; case-insensitive partial match
   on the pinned backend), `--run-timeout MS` (minimum 30000, per leg). `all`
   runs the EditMode suite then the PlayMode suite and prints one result block
   per leg: the counters, then one indented line per failed test the editor
   named. The list is capped, so a block that shows fewer names than failures
   ends with a line saying how many were not listed. Names arrive percent-encoded
   and are escaped before printing, per
   [text-io-boundaries](../text-io-boundaries/SKILL.md).

   Four rules make a result trustworthy. Two live in `awaitRunResult` (the
   bridge-polling fallback) and two in `runUnityTests`; `awaitRunClaim` holds the
   claim-file equivalents used whenever the run reporter is installed:

   - A leg is reported only when its result is finished, not in flight, and
     either seen in flight (from a poll, never from the `run_tests` answer
     itself) or carrying a run key (counters plus duration) that differs from
     the pre-request one. The bridge answers `run_tests` with the previous run's
     result when it starts nothing, and that echo must never read as a green
     gate. Duration is in the key so a legitimate identical re-run is not
     mistaken for the previous one.
   - The session signal is sized for every leg (`runTimeout * legs`), or it ends
     the command before the per-leg deadlines can mean anything.
   - Inspect a payload before re-testing the deadline. A loop shaped
     `while (now < deadline) { inspect; sleep; fetch }` drops the payload its
     last poll fetched, so a run that finished during that poll reads as
     missing.
   - A leg that matched no test is reported, and only a zero total across legs
     fails, so a mode-specific `--filter` is not an error.

   The claim path replaces the status poll, never the `run_tests` request: the
   command writes an owner token to `test-run-request.txt`, the editor echoes it
   on every line of `test-run.txt`, and only a claim carrying that token can end
   the wait. The request is removed once acknowledged, so a later run nobody
   asked for cannot adopt it. `did-not-run` fails with the editor's reason and
   the request path. Until a claim with our token arrives the wait is bounded by
   a 120 s start grace, not `--run-timeout`; after it, the full deadline, so a
   long capture leg is never cut off. A run matching no test never starts and
   reports nothing, so it takes the bridge's zero-total answer and the leg is
   reported, not failed. The editor's own report is what a red leg is read
   from: a reporter that predates the name field, or the bridge fallback, names
   nothing and the leg says so.
3. Unity Test Runner: Window > General > Test Runner -> PlayMode tab -> Run All.
4. Unity CLI (CI-style):
   `Unity -batchmode -projectPath <proj> -runTests -testPlatform PlayMode -testResults results.xml -quit`
   (requires a valid Unity license; exit code reflects test success).

## House style (from existing suites)

- File per type under test (`CommandArgTests.cs`, `CommandShellTests.cs`,
  `TerminalTests.cs`, ...), plain NUnit (`[Test]`) over the static facades
  (`Terminal`, `Terminal.Shell`); no scene setup for command/parsing behavior.
- Data-driven cases use `[TestCase]` rows instead of loops; failure messages assert
  on values, not just booleans. Naming and comment rules come from `context.md`.

## Adding tests

1. New command behavior -> extend the matching suite; fixtures go where registration
   scanning finds them: `Components/TestCommands.cs` for PlayMode,
   `Tests/Editor/DiscoveryFixtureCommands.cs` for EditMode-assembly discovery.
2. Input behavior -> simulate through `Components/TerminalInputHandler.cs`, not raw
   key events. Terminal lifecycle (open/close, resize, buffer wrap) -> `TerminalTests.cs`,
   reusing its setup helpers.
3. Keep runtime allocations in assertions minimal; suites run in PlayMode on every change.

## Driving tests from agents

After editing files outside Unity, confirm the editor compiled the intended
content before trusting a run: the host sync can lag, and a stale assembly
produces a green report on the previous code.

`npm run unity:tests` and `npm run unity:capture` both ask the editor to import
and compile what changed on disk, then wait for it, so neither reads the
assembly it already had. The request is made in script because
`menu: Assets/Refresh` answers success without importing a changed script under
`Packages/` (#168). Driving the editor yourself over MCP means asking for the
compile, not just the import - a refresh that finds a change only schedules it,
so an idle check can land before it starts:

```js
"UnityEditor.AssetDatabase.Refresh();"
  + "UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();"
```

then wait for `isCompiling` to clear and check the `Library/ScriptAssemblies`
timestamp moved before believing a run. If the asset database must be forced,
add `ImportAssetOptions.ForceUpdate` and `CleanBuildCache`.

## Host editor hygiene (eval drills)

A modal dialog blocks the editor's main thread, so every bridge request times
out and the outage reads as a hang. The drills, the refusal preflight, and the
scene-restore order are in
[unity-mcp](../unity-mcp/SKILL.md#host-editor-hygiene-eval-drills); read them
before any eval that writes.

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
- Caret assertions are deterministic (session-045): the palette and
  token-completion polls accept the queued marker (`_pendingCaretIndex`) or the
  live cursor, and value polls accept the input abstraction or the field mirror.
  The live cursor is UITK's layout-coupled echo and can sit clamped below the
  value length for whole polls; never assert on it alone. A poll loop's
  condition must accept every surface its final assert does - a poll narrower
  than the assert burns the whole budget waiting on a surface that cannot
  converge (Bugbot catch on PR #114; sweep: compare each `while` budget loop
  against its trailing assert). A caret rule that needs the marker to drain is
  pinned synchronously by driving `ApplyPendingCaret` against a position the
  field holds (see `PendingCaretConsumesOnlyAfterStablePasses`).
- One failure mode survives the deterministic polls: long agent sessions can
  leave the editor in a state where panel events stop processing entirely
  (writes re-clamp or never land, for 30+ frames). See the first Debugging
  failures bullet for the verify-then-refresh recipe.
- Palette caret flakes: `CommandPaletteUI._logCaretPasses = true` (editor
  eval or a test) logs every pending-caret pass with frame, pending,
  cursor/select, and focus owner, so a #74-class re-clamp shows which pass
  moved the caret. The caret is parked only after it holds for two passes
  (`CaretStickPasses`), and a field change cancels the queued caret
  (`_pendingCaretIndex` is `int?`; null = none).
- A caret failure is the marker moving or draining - a real regression, not a poll budget
  problem. The four flakes this replaced (`QuotedTokensAcceptUnquotedInsertions`,
  `TabAppliesArgumentCompletionWithQuoting`, `NavigateAutoLoads`, the re-clamp pair) all
  asserted the live cursorIndex without the queued marker; the palette and token-completion
  polls now accept the queued marker (`_pendingCaretIndex`) or the live cursor, and value
  polls accept the input abstraction or the field mirror.
- UITK clamps `cursorIndex` writes to the last LAID-OUT text length, not the
  value length, and the panel can RE-CLAMP a write after it landed. A fresh
  field can sit capped below the value length for a whole poll budget, and
  re-focusing does not force it. Never assume a caret park sticks once: retry
  the write and poll for it to HOLD (see
  `TerminalUITokenCompletionTests.SetInput`'s retry loop), or pin
  position-independent rules against a probed holdable position (see
  `CommandPaletteTests.PendingCaretConsumesOnlyAfterStablePasses`).
  Synchronous caret reads one frame after a value write are clamped too
  (`PendingCaretWritesAndKeepsMarkerWhileUnfocused` readiness-polls its
  applied positions).

## Debugging failures

- Focus assertions must accept the TextField or anything it contains: the focus
  controller reports either the field or its inner `unity-text-input` element (the
  palette suite's `InputOwnsFocus` is the reference helper).
- `TerminalUI.IsClosed` is state-closed AND window height settled; the height settles
  in a LateUpdate after `Close`, so poll (`IsClosed` over a frame budget) instead of
  asserting synchronously right after a close/toggle.
- Keyboard-controller behavior is testable without real input: subclass it, override
  the virtual `Is*Pressed` checks (the base constructor's delegate table dispatches
  virtually), and drive the protected `Update` from a public method. The loop runs
  checks in `_controlOrder` and breaks after the first hit - that is the hotkey
  conflict contract.
- A hotkey test that must exercise the text-focus gate overrides `IsHotkeyDown(string)`
  instead: the gate lives in `IsHotkeyActive`, below the per-control checks, so a
  per-control override would bypass it. `TextInputOwnsFocus` is virtual as well - a
  `bool?` override pins focus without a panel, and leaving it null reads live focus
  and so covers the shipped wiring. A real poll is edge-triggered, so a simulated
  press is consumed by the first read (see `TerminalUITransitionTests.HotkeyController`).
- A component that polls on its own `Update` (the palette's toggle) can be driven
  for real with `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Backquote))`
  under `#if ENABLE_INPUT_SYSTEM` - no production seam, and one control half that
  proves the queue reached the poll. Wait fixed frames for a one-shot state event;
  polling for the expected state passes on the value it started with.
- A hotkey frame is observed twice: by the controller's own `Update`, and by the
  terminal's field-change handler through `IInputHandler.ShouldHandleInputThisFrame`.
  The controller must sit on the terminal's GameObject for the `GetComponents<IInputHandler>()`
  in `Awake` to see it.

- Assert post-mutation state through the live reference: after a respawn, reset,
  destroy, or disable, read `TerminalUI.Instance` (or re-query the facade) at the
  assert, not a local captured before the mutation. A stale capture makes the
  null-check vacuous and lets `AreNotSame` pass against a dead object.

- Teardown pairs with setup: when `[UnitySetUp]` can exit early (`Assert.Ignore` for
  -nographics or missing assets), a teardown assert that reads setup state must guard
  on that state existing (nullable field + `HasValue`, or the same skip condition).
  NUnit runs `[UnityTearDown]` after a SetUp ignore, so an unguarded baseline - a
  RenderTexture count, say - turns an intended skip into a failure on a machine with
  pre-existing objects.
- A test failing ISOLATED that passed isolated earlier in the same session
  is the poisoned-panel state below, not a code change - but verify with a
  real domain reload first: Assets > Refresh (or any script edit that
  recompiles). A no-op `recompile` ("up_to_date") does NOT reload the
  domain and does not clear it; Assets > Refresh does (session-023: the
  palette auto-load caret stuck at 9/1 for whole budgets, cleared after
  refresh).

- Errors are queued on the terminal (not only the last one) - assert on the full error set where
  relevant.
- Static state leaks between tests (`Terminal.Shell`, registered parsers, control sets on
  `CommandArg`) - reset or isolate when a test mutates global state, see
  [custom-argument-parsing](../custom-argument-parsing/SKILL.md) for the mutable statics list.

## Clean-project compatibility fixtures

- Root default matrix and fixture paths at `tooling~/`; test that an empty argument list
  loads them. Name per-leg editor flags directly from the leg id, such as `--unity-2021`.
- Pass positive boolean environment values such as `DX_T13_DOMAIN_RELOAD_ENABLED=1`, and
  test both boolean polarities against the Unity setting they describe.
