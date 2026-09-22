---
name: capture-unity-state
description: Automatically capture Unity editor and game state for agentic verification - hierarchy, console logs, terminal buffer, game view screenshots, and passive editor-state snapshots via npm run unity:capture and DxTerminalStateCapture. Use when verifying visual changes in Unity, debugging play mode behavior, red-green testing against a live editor, checking compile status, or capturing evidence for a bug report.
metadata:
  category: Feature
---

# Agentic Unity State Capture

`npm run unity:capture` drives the host editor through the [unity-mcp](../unity-mcp/SKILL.md)
bridge, invokes `DxTerminalStateCapture.CaptureAll(outputDirectory)`, and waits for the
manifest to complete. Artifacts land inside this package's `.artifacts/unity-state/`
(dot-prefixed: Unity never imports them) so they are visible in the devcontainer.

## Prerequisites

1. Bridge running on the host (`npm run unity:mcp`) and `npm run unity:mcp:probe`
   passing from where you run capture.
2. Capture script compiled in the editor. Install it host-side with
   `npm run unity:mcp:install-capture -- --project <host-project>` (it copies
   `tooling~/scripts/mcp/DxTerminalStateCapture.cs.txt` to `<project>/Assets/Editor/`,
   backing up any previous copy under `.artifacts/unity-state/backup/`). `capture`
   performs this install itself when the project directory is reachable locally
   (host runs, or the container's `/unity-project` bind mount).

## What one capture produces

| Artifact | Content |
| --- | --- |
| `manifest.json` | Timestamps, editor flags (compiling/updating/playing/paused), counts, artifact list, errors; `complete:true` when finished |
| `hierarchy.json` | Every loaded scene with isDirty flags and the full transform tree (name, tag, layer, active flags, component type names, rounded local positions) |
| `console-log.json` | Ring buffer (last 500 entries since editor start) plus per-type counts |
| `editor-state.json` | Passive 1 Hz snapshot (also written continuously by the installed script) |
| `terminal-log.txt` | This package's in-game terminal buffer via `Terminal.Buffer` reflection (best effort) |
| `game-view.png` | Game View screenshot (play mode only; `skipped` otherwise) |

## Red-green harness loop

1. **Baseline (red):** capture before your change; read `manifest.json` and the
   artifacts; record console error/warning counts and relevant hierarchy facts.
2. Apply the change; let the editor recompile.
3. **Verify (green):** never refresh or invoke while
   `isCompiling || isUpdating` is true - capture does this automatically via the
   eval'd idle check, and the npm command polls until the editor is idle, then
   captures, then polls `manifest.json` until `complete:true` (default 120 s).
4. Compare against the baseline. Accept only when the new manifest has
   `complete:true`, no new errors, and the expected hierarchy/visual delta.
5. Cite artifact paths (repo-relative, under `.artifacts/unity-state/<stamp>/`) in
   the task summary; they are gitignored but persist locally.

## Rules

- Capture is read-only observation: it must not mutate scenes or assets. If a
  capture reports unexpected `errors` entries, fix the capture environment before
  trusting (or re-running) anything else.
- Game View captures require play mode; if `gameView` is `skipped`, enter play
  mode first (via the editor or MCP tools) and capture again. A
  `gameViewError` fails the npm command - do not ignore it.
- Console history starts at editor launch (ring buffer, not the full console
  window). For evidence of an older error, reproduce it, then capture.
- Freshness: check `capturedUtc` in the manifest against the host clock before
  comparing artifacts from different stamps.
- Manual trigger without npm: menu `Tools > Dx Terminal State > Capture Now` in
  the editor; `DxTerminalStateCapture.CaptureStatus()` returns the latest manifest.

## Fixture capture (T04, `npm run t4:capture`)

Pixel fixtures of the real package surfaces (terminal small/full, completion
hints, command palette) are PlayMode tests under `Tests/Runtime/Capture/`, run
through the bridge:

```
npm run t4:capture                      # runs the suite + validates manifests
npm run t4:capture -- --scenarios CapturesTerminalSmallSurface
```

The command runs the `TerminalSurfaceCapture` tests, then validates every
`.manifest.json` written under `.artifacts/t4/` since the run started: schema,
positive dimensions, non-empty PNG, bounds-clean pixels for the four surface
scenarios, and a deliberately failing (blank) negative control. It exits
non-zero on any missing, incomplete, or schema-invalid manifest - blank or
broken renders can never pass silently. Each capture writes `<scenario>.png` +
`<scenario>.manifest.json` (resolution, logical scale, color space, Unity
version, graphics API, theme/font, git revision, pixel metrics, bounds,
violations, tree diagnostics).

Rules the harness enforces on itself:

- Read-only for the surfaces: it redirects `PanelSettings.targetTexture`,
  forces one synchronous panel render per capture, and restores render state.
- Deterministic: the terminal caret is frozen via
  `TerminalUI.SetCursorBlinkPaused(true)`; ease times are zeroed; the palette's
  native caret is frozen through its `cursorColor` (blink-invariance is proven
  by a byte-identical repeat readback).
- Leak-checked: RenderTexture counts are asserted back to baseline in teardown
  (guarded when setup skipped, so -nographics ignores cannot fail teardown).

## Golden baselines (T11, `npm run t11:check` / `npm run t11:update`)

`t4:capture` also compares every capture against the committed baseline store
under `Tests/Runtime/Capture/Baselines~/` (one directory per environment,
e.g. `6000.4.6f1-metal-linear-397x489-scale1/`, holding the scenario PNGs plus
an `index.json` with provenance). Gate: per-channel tolerance of 1 byte,
failing when more than 0.1% of pixels exceed it; provenance (Unity version,
graphics API, color space, resolution, theme, font) must match exactly.
Missing baselines are reported as pending, not failed; a mismatch fails the
command and writes diff/overlay/report artifacts under `.artifacts/t11/`.

```
npm run t11:check                       # Unity-free store integrity gate (runs in CI)
npm run t11:update -- --run .artifacts/t4   # promote a capture run into the store
```

Rules:

- `t11:update` is the only regeneration path. It refuses incomplete or
  schema-invalid captures and cross-environment promotions; replacing a
  baseline whose pixels differ prints the verdict plus a gate PASS/FAIL and
  writes review artifacts - the operator reviews and commits.
- Never edit baselines by hand; never loosen tolerances to pass a compare.
- Capture before visual changes (run `t4:capture`, review, then `t11:update`).

Panel-scale constraints learned the hard way (PR #126 science runs) - any new
capture scenario must respect them:

- A runtime `CreateInstance<PanelSettings>()` has no theme style sheet and
  renders nothing (layout still works - silently wrong pixels). Assign the
  package's `Styles/TerminalThemeSettings-Base.tss`.
- The panel freezes its scale at creation and the game view's zoom x Retina
  backing decides how many capture pixels one panel point is (2.656x on the
  pinned host). Screen-pixel designs wider/taller than the panel's point
  extent are stretched-and-clipped in the offscreen render; the terminal in
  the small state (161.5 points tall) fits, the full state does not.
  Post-creation `referenceResolution` changes are ignored; only a respawn or a
  lowered game-view zoom floor (`ZoomableArea` hScaleMin/vScaleMin, min is the
  backing scale on Retina) changes the ratio.
- The game view must be open for the zoom pin to find it; harness reflection
  on Unity's GameView/ZoomableArea fails explicitly when Unity's internal
  shape changes.

Baseline updates (replacing golden images) stay out of this command; they land
with T11's comparator as an explicit, reviewed step.
