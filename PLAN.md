# Terminal UI Refactor Follow-Up Plan

## Outstanding Bugs

1. **Launcher history over-expands vertically** — *In Progress*
   - *Symptoms*: Each additional entry in launcher history increases the window height more than a single row, leaving excess blank space before the `historyHeight` cap is reached.
   - *Suspected Causes*: Combined padding/margin accumulation, duplicated height estimates (measured content vs. row count), and fallback spacing when the ListView geometry is not yet resolved.
   - *Next Steps*: instrument the launcher height calculation to capture individual terms (input height, reserved suggestions, measured content, estimated rows) and verify in-editor with known history sizes. Clamp to `historyHeight` after padding is applied, not before. Begin by extracting a reusable calculator method that returns the individual metrics for logging.
   - *Progress*: Added `CalculateLauncherLayoutSnapshot` to centralise the sizing math, verified with a telemetry-focused regression test, clamped the row estimate to `HistoryVisibleEntryCount`, raised editor instrumentation via `LauncherLayoutDiagnostics`, and now derive row heights on the fly from resolved element sizes while preserving launcher fading/scroll behaviour and eliminating height oscillations. The terminal log scroll view is bottom-aligned via scheduled scroll clamping, and launcher fades now recompute on every scroll and layout tick so the lowest visible row is fully tinted without over-scrolling. Next, capture sample telemetry and compare measured vs. estimated heights to tune the fallback path.

2. **Launcher spacing inconsistencies when suggestions toggle**
   - *Symptoms*: Small residual gaps persist when the autocomplete pill row is removed, depending on the previous state.
   - *Next Steps*: ensure we zero out margins on both the pill container and history container when suggestions disappear, and add dedicated regression coverage.
   - *Progress*: Added regression coverage (`LauncherSpacingResetsWhenSuggestionsDisappear`) to verify history margins clear when suggestions disappear.

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

5. **Launcher view controller extraction** — *Done*
   - Isolate scroll clamping and fade logic into a helper so `TerminalUI` focuses on orchestration. Introduced `LauncherViewController` and `HistoryListAdapter` to own scroll/fade orchestration and ListView plumbing, with regression coverage verifying justification and fade curves.

6. **Unify geometry measurement utilities** — *In Progress*
   - Factor shared height/row calculations into a utility used by both launcher layouts and future standard terminal sizing. Added `LayoutMeasurementUtility`, updated launcher reserved suggestion and height clamps to reuse it, and introduced focused unit tests (including new standard-container coverage). Standard layout now uses the shared helper for container height calculations. Next: audit animation entry points and any remaining ad-hoc geometry math.

7. **Minimise ad-hoc singletons** — *In Progress*
   - Introduced `ITerminalProvider`/`TerminalRegistry` for terminal lookup and `ITerminalRuntimeConfigurator` with a default proxy wrapping `TerminalRuntimeConfig`. `TerminalUI` now registers providers/configurators and is covered by new `TerminalRuntimeConfiguratorTests`. Next: document the provider/configurator APIs and evaluate replacing the remaining static lookups (`Terminal.ActiveRuntime`, `DefaultTerminalInput.Instance`).

## Architectural Assessment

- **Structure Overview**
  - `TerminalUI` spans three partial files totalling ~3.9k LOC and coordinates state, layout, input focus, scroll behaviour, theme application, history fading, and telemetry. Supporting types (`TerminalLauncherSettings`, `TerminalHistoryFadeTargets`, profiles) remain small and descriptive, but virtually all runtime behaviour funnels through the monolithic partial class.
- **SOLID Analysis**
  - *Single Responsibility*: `TerminalUI` violates SRP by owning state transitions, layout logic, data synchronisation, scroll/fade orchestration, diagnostics, and user interaction. The partial split eases navigation but no clear seams exist for independent testing or replacement.
  - *Open/Closed & Liskov*: Most behaviour is hard-coded inside `TerminalUI`; extending layouts or fading requires touching core logic. Injectable collaborators (e.g., data sources, view presenters) are absent, making substitution difficult yet not currently broken (LSP is effectively satisfied because there are no subclasses).
  - *Interface Segregation*: External APIs expose wide surface (`TerminalUI` methods used by tests) rather than small focused interfaces; consumers (tests, editor tooling) rely on internals via `InternalsVisibleTo` to reach the necessary hooks.
  - *Dependency Inversion*: UI depends directly on Unity APIs and embedded state; no abstractions exist for command history, theme packs, or scroll controllers inside the UI, increasing coupling.
- **DRY / Reuse**
  - Launchers and standard terminal share similar scroll+fade logic implemented twice (launcher-specific blocks vs. standard). Numerous constants (spacing, fade exponent) live in `TerminalUI` making reuse by other components difficult. Geometry probing and scroll scheduling patterns repeat across methods.
- **Maintainability & Ease of Understanding**
  - High cognitive load: layout code interleaves measurement, state transitions, and animation triggers. Extensive private fields with similar names (`_launcherHistoryContentHeight`, `_launcherHistoryEntries`, etc.) require cross-file searching. Lack of comments around non-obvious math (fade curves, padding heuristics) increases onboarding time.
  - Tests compensate via `InternalsVisibleTo` but rely on mutable state injection, signalling missing seams for proper presenters or services.
- **Performance Considerations**
  - Frequent per-frame scheduling (`schedule.Execute`) and iteration over child collections occurs in `UpdateLauncherLayoutMetrics`. Without virtualisation or caching of resolved heights, large histories may trigger redundant recalculations. Geometry reads (`worldBound`) for every entry on each refresh can be costly under heavy use, though Unity UI Toolkit usually handles moderate lists.
  - Scroll clamping uses `Mathf.Clamp` each frame; negligible cost compared to repeated tree walks.
- **Correctness & Usability**
  - Logic relies on measured heights racing against Unity's layout pass; scheduling fixes mitigate flicker but risk regression. Fading now depends on real viewport overlap, improving UX but increasing code complexity. Overscroll clamping should prevent blank regions, but no guard exists against layout jitter if scroller reports stale bounds.
  - Normal terminal now bottom-aligns logs, matching user expectation; launcher provides live fade and scroll clamp, aligning UX across modes.
- **Recommendations (prioritised against current work)**
  1. Short-term: isolate launcher/scroll interactions into a helper (`LauncherViewController`) to contain fade/clamp logic and provide unit-testable seams without derailing current bug work.
  2. Mid-term: extract geometry measurement and height calculation into a dedicated service or data struct shared by standard + launcher modes to remove duplication and clarify responsibilities.
  3. Long-term: refactor `TerminalUI` into smaller presenters (input, history, launcher, diagnostics) with explicit contracts, enabling dependency injection and reducing reliance on `InternalsVisibleTo` for testing.
