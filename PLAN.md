# Terminal UI Refactor Follow-Up Plan

## Outstanding Bugs

1. **Launcher history over-expands vertically** — *In Progress*
   - *Symptoms*: Each additional entry in launcher history increases the window height more than a single row, leaving excess blank space before the `historyHeight` cap is reached.
   - *Suspected Causes*: Combined padding/margin accumulation, duplicated height estimates (measured content vs. row count), and fallback spacing when the ListView geometry is not yet resolved.
   - *Next Steps*: instrument the launcher height calculation to capture individual terms (input height, reserved suggestions, measured content, estimated rows) and verify in-editor with known history sizes. Clamp to `historyHeight` after padding is applied, not before. Begin by extracting a reusable calculator method that returns the individual metrics for logging.
   - *Progress*: Added `CalculateLauncherLayoutSnapshot` to centralise the sizing math, verified with a telemetry-focused regression test, clamped the row estimate to `HistoryVisibleEntryCount`, raised editor instrumentation via `LauncherLayoutDiagnostics`, and started sampling actual row heights through geometry callbacks to keep the estimate in sync with rendered content. Next, capture sample telemetry and compare measured vs. estimated heights to tune the fallback path.

2. **Launcher spacing inconsistencies when suggestions toggle**
   - *Symptoms*: Small residual gaps persist when the autocomplete pill row is removed, depending on the previous state.
   - *Next Steps*: ensure we zero out margins on both the pill container and history container when suggestions disappear, and add dedicated regression coverage.

3. **Terminal visibility race on close**
   - *Symptoms*: A brief flash of history text can appear when closing the terminal via toggle.
   - *Next Steps*: move visibility toggling earlier in `RefreshUI` or gate rendering with state transitions, then add a regression test that simulates closing while mid-animation.

## Improvements

1. **Centralise launcher height calculation** — *Done*
   - Extract the computation into a dedicated method that accepts content metrics (rows, measured height, padding) and returns the clamped values. This reduces repeated logic and simplifies testing. Implemented the calculator and snapshot struct inside `TerminalUI`.

2. **Telemetry helper for layout diagnostics** — *Done*
   - Add a temporary debug utility (editor-only) to log the computed terms when launcher size changes. This will speed up validation while adjusting the height formula. Added a toggleable editor diagnostic hook via `LauncherLayoutDiagnostics`.

3. **Expanded regression coverage**
   - Add play mode tests that simulate adding/removing history entries and autocomplete suggestions in sequence, verifying window size, content height, and scroll behaviour remain stable.

4. **Document launcher layout expectations**
   - Update README or a developer note with the intended launcher behaviour (input flush, history cap, suggestion spacing) to guide future contributors.
