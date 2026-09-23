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

1. Local preflight (fastest full gate, no Unity): `npm run preflight` - runs the
   node suite, the nine C#/asset linters, the T11 baseline gate, the package
   gate, docs guides build, and the compat compile in parallel (~3-4s wall vs
   ~30s serial). `--skip=a,b` narrows a run.
2. Unity Test Runner: Window > General > Test Runner -> PlayMode tab -> Run All.
3. Unity CLI (CI-style):
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

## Host editor hygiene (eval drills)

A modal dialog blocks the editor's main thread; while it is up, every bridge
request times out and the outage reads as a hang (session-048: a scratch scene
was deleted while it was the open scene, and the next play-mode entry raised
the "save modified scene?" prompt). The techniques here are adapted from
IshoBoy's shared-editor etiquette (`Ambiguous-Interactive/IshoBoy`,
`.llm/references/unity-mcp-shared-editor-etiquette.md` and the capture
runner's refusal gate).

- **Open with a refusal preflight, not a state guess.** Before anything that
  swaps, saves, reimports, compiles, or enters play mode, run one read-only
  eval: `EditorApplication.isPlaying(OrWillChangePlaymode)`, `isCompiling`,
  and every open scene's `path` + `isDirty`. Dirty or playing -> **refuse and
  say so** (name the scenes) instead of stepping into a prompt only the human
  can dismiss. Editor status endpoints cannot see scene dirtiness.
- **Never write or `DeleteAsset` anything under `Assets/` the editor has
  open** - reimporting a loaded file raises "Reload/Ignore" and deleting the
  file under the open scene dirties it. Re-read the open-scene list
  immediately before the write rather than trusting an earlier probe.
- **Single-mode scene swaps are the modal source** (`NewScene`/`OpenScene`
  with `NewSceneMode.Single` prompt over a dirty scene). Prefer no swap; for
  isolated edit-mode work use `EditorSceneManager.NewPreviewScene()` /
  `ClosePreviewScene(scene)`, which touch no open scene.
- **`new GameObject` lands in the active scene and dirties it**; destroying
  it does not clear the flag. In eval probes use
  `EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave, ...)`
  - it belongs to no scene. (Its `scene.IsValid()` is false, so bodies added
  with it never register with a physics world.)
- **Restore order**: record the open scenes first; open the originals back
  with `OpenScene(path)` BEFORE deleting drill assets; end with zero dirty
  scenes, no drill asset open, not playing. Unity 6 has no
  `ClearSceneDirtiness`; once a prompt is dismissed, opening a clean scene is
  the reset. Verify via eval after every drill.
- **Play mode is a one-way door over MCP** (entering it stops the bridge
  answering; only a human at the machine can leave). Only start
  play-mode runs with the human present, and never as the session's last
  operation.

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
- Historical palette/token caret flakes (QuotedTokensAcceptUnquotedInsertions,
  TabAppliesArgumentCompletionWithQuoting, NavigateAutoLoads, the re-clamp pair)
  were root-caused to tests asserting the live cursorIndex without the queued
  marker; the polls now accept both surfaces and the failures stopped
  recurring. A failure there today means the marker itself moved or drained -
  a real regression, not a poll budget problem.
- UITK clamps `cursorIndex` writes to the last LAID-OUT text length, not
  the value length. The cap converges with layout and its convergence is
  nondeterministic under session sequences: a fresh field can sit capped
  below the value length for a whole poll budget, and re-focusing does
  not force it. Worse, the panel can RE-CLAMP a caret write after it
  already landed (observed session-023: a mid-line park held, then lost
  frames later). UI tests must not assume a caret park sticks once; retry
  the write and readiness-poll for it to HOLD (see
  `TerminalUITokenCompletionTests.SetInput`'s retry loop), or probe for a
  holdable position and pin position-independent rules against it (see
  `CommandPaletteTests.PendingCaretConsumesOnlyAfterStablePasses`).
  Synchronous caret reads one frame after a value write are also clamped
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

- Assert post-mutation state through the live reference: after a respawn, reset,
  destroy, or disable, read `TerminalUI.Instance` (or re-query the facade) at the
  assert itself, not a local captured before the mutation. A stale capture makes
  the null-check vacuous and lets the following `AreNotSame` pass against a dead
  object (PR #101 Bugbot). Capture-then-assert is only sound when nothing mutates
  between the capture and the assert.

- Teardown pairs with setup: when `[UnitySetUp]` can exit early (`Assert.Ignore`
  for -nographics or missing assets), any teardown assert that reads setup state
  must guard on that state existing (nullable field + `HasValue`, or the same
  skip condition). NUnit still runs `[UnityTearDown]` after a SetUp ignore, so an
  unguarded baseline (for example a RenderTexture count) turns an intended skip
  into a failure on machines with pre-existing objects (PR #126 Bugbot).
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
