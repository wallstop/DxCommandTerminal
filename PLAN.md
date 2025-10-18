# Launcher Scrollbar & Fade Remediation Plan

## Goals

- Surface a reliable vertical scrollbar in launcher mode only when history content exceeds the visible viewport.
- Restore launcher history fading so entries further from the input bar render with progressively lower opacity.
- Keep the solution maintainable even if that means trading away UI Toolkit virtualization for correctness.

## Current Behaviour

- Launcher mode clamps history height via `ApplyLauncherLayout` in `Runtime/CommandTerminal/UI/TerminalUI.LayoutView.cs:195`, yet the vertical scroller element inside the list view never appears despite mouse-wheel scrolling working.
- `LauncherViewController.UpdateFade` (`Runtime/CommandTerminal/UI/TerminalUI.LauncherViewController.cs:49`) runs, but rendered entries keep full opacity; neither the entry container nor nested label reflects the expected gradient.
- Standard terminal currently restores alignment but still loses scroll position after large log updates or terminal toggles.
- A white highlight appears on history rows when hovered/selected due to default UI Toolkit classes.

## Root-Cause Hypotheses

### Scrollbar Absence

1. The scroller VisualElement (`unity-scroller--vertical`) might be forced to `display:none` or zero width by USS overrides in `Styles/BaseStyles.uss:32` (e.g., the tracker/dragger classes) once launcher classes are applied.
2. `InitializeScrollView` (`Runtime/CommandTerminal/UI/TerminalUI.cs:1426`) wires callbacks but never forces `scrollView.showVerticalScroller = true`; relying on `ScrollerVisibility.Auto` may fail because the computed `highValue` stays at `lowValue` while content still scrolls via pointer events.
3. Dynamic-height virtualization on `ListView` (`Runtime/CommandTerminal/UI/TerminalUI.HistoryListAdapter.cs:60`) could suppress the scroller element when realized child count stays below the configured viewport height, leaving no trigger for the scrollbar to become visible.

### Fade Breakdown

1. The fade pipeline is gated on `_launcherMetricsInitialized` (`Runtime/CommandTerminal/UI/TerminalUI.LayoutView.cs:386`) and may exit early whenever launcher layout updates run before the scroll view resolves geometry, leaving `UpdateFade` with an empty viewport rectangle.
2. Virtualization rebinding calls `BindLogListItem` (`Runtime/CommandTerminal/UI/TerminalUI.LogView.cs:29`), which resets `element.style.opacity = 1f` for launcher mode; if `HandleScrollValueChanged` is not invoked afterwards (e.g., touchpad inertial scroll), entries keep the reset opacity.
3. Project setups can exclude `TerminalHistoryFadeTargets.Launcher` from the appearance profile (`Runtime/CommandTerminal/UI/TerminalAppearanceProfile.cs:26`), so we need to confirm configuration is not simply disabling the effect.

### Standard Terminal Scroll Persistence

- ScrollView `highValue` is undefined until geometry settles; caching scroller value on close must wait for `highValue` before restoration.
- Log buffer mutations reset the non-virtualized listview, invalidating cached offsets.
- Auto-scroll-on-open fights manual cached position.

### History Hover Styling

- USS defaults for `.unity-list-view__item--hovered` and `.unity-list-view__item--selected` introduce light backgrounds in dark themes.

## Investigation Tasks

1. Reproduce in-editor with UI Toolkit Debugger attached; inspect `unity-scroller--vertical` during overflow to confirm visibility, size, and USS state.
2. Instrument `LauncherLayoutSnapshot` outputs (already piped through diagnostics) to log `Scroller.highValue`, `lowValue`, `contentContainer.resolvedStyle.height`, and `verticalScrollerVisibility` when history entries exceed the cap.
3. Temporarily toggle `ListView.virtualizationMethod` to `CollectionVirtualizationMethod.None` to see if scrollbar/fade behaviour immediately returns, confirming a virtualization interaction.
4. Trace `HandleScrollValueChanged` invocations (subscribe to `_logScrollView.verticalScroller.valueChanged`) to ensure fade recalculations fire after `BindLogListItem` resets opacity and after momentum scrolling completes.
5. Verify the active `TerminalAppearanceProfile` in scenes/prefabs retains the `Launcher` flag for `_historyFadeTargets`; capture asset GUID if a custom profile overrides defaults.
6. Add temporary logging around `_cachedStandardScrollValue`, `scroller.highValue`, and `_restoreStandardScrollPending` transitions to confirm restoration timing.
7. Identify all USS selectors contributing hover/highlight styling and override them at the base theme.

## Implementation Strategy

### Scrollbar Visibility _(In Progress)_

- [x] Added overflow detection and explicit vertical scroller toggling in `Runtime/CommandTerminal/UI/TerminalUI.LayoutView.cs` to gate visibility.
- [x] Reworked scroller API usage to remain compatible with Unity 2021.3 (no `showVerticalScroller`, preferring `ScrollerVisibility` and style toggles).
- [x] Restored standard terminal alignment switching so flex justification snaps to `FlexStart` whenever the user scrolls away from the bottom, eliminating white-space overrun while preserving bottom alignment by default.
- [ ] Validate virtualization interactions and adjust scroller range calculations if Unity still suppresses the dragger.

### History Fade _(In Progress)_

- [x] Ensure launcher bindings default to computed opacity and immediately request fade recomputation to avoid resets.
- [ ] Observe geometry-driven fade output under momentum scrolling and update diagnostics if needed.

### Animation & Launcher Layout _(In Progress)_

- [x] Prevent launcher snap logic from running while closing so the exit tween respects the configured curves.
- [x] Keep the container visible while height animation plays and reset launcher timing flags once the tween completes.
- [x] Defer clearing launcher metrics while closing so shortcut-based closes reuse launcher layout/scrolling until the tween ends.
- [x] Expand overflow heuristics and defer scroller recalculation to eliminate the transient gap at the bottom of launcher history when commands stream in.
- [ ] Ensure standard closing tween keeps the log content anchored beneath the input (In Progress – scroller now forced to follow the current bottom offset whenever `_isClosingStandard` is true; needs in-editor validation).
- [ ] Disable virtualization on the launcher/terminal history list to keep entries realized during close/overflow transitions (pending 2021.3 check).
- [ ] Stress-test launcher with rapid command bursts to confirm the bottom padding no longer flashes before the first manual scroll.

### Standard Terminal Scroll Persistence _(In Progress)_

- [x] Cache scroller value and log version on close; suppress auto-scroll when restoring.
- [x] Delay restoration until `RestoreStandardScrollBounds` runs with valid `highValue`.
- [x] Clamp cached value and honor bottom-alignment when `highValue` is zero.
- [x] Detect when the user closed while pinned to the latest entry and fall back to `ScrollToEnd` instead of replaying a normalized offset.
- [x] Retry `ScrollToEnd` after layout settles and compute fallback targets from content/viewport height so reopening at the bottom no longer depends on `highValue`.
- [ ] Validate cached scroll replay after close/reopen (In Progress – normalized caching wired to restore offsets; needs runtime confirmation after the closing-time anchoring settles the scroll view).
- [ ] Add conditional diagnostics around `ScrollToEnd`/`TryApplyScrollToEnd` capturing `highValue`, fallback targets, and cached metadata whenever restoration fails, to assist future debugging.
- [ ] Add diagnostics and possibly a fallback auto-scroll once geometry stabilizes if cached state becomes stale (e.g., buffer cleared).
- [ ] Investigate caching per terminal state (small/full) to avoid cross-state interference.

### History Hover Styling _(Done)_

- [x] Override all `.unity-list-view__item` hover/focus/selected classes to remain transparent in dark themes.

### Shared Cleanup

- Centralise scroll/fade state checks into a tiny helper so both `BindLogListItem` and `LauncherViewController` consult the same readiness predicate (active state, metrics initialised, scroller present).
- Add optional editor gizmo or log entry showing the normalized distance used for opacity to aid future tuning.

## Testing & Validation

- Manual: With 2, 5, and 10 history entries, verify the scrollbar appears only once the viewport height cap is exceeded and that dragging the thumb scrolls correctly.
- Manual: Check opacity gradient before and after scrolling with mouse wheel, touchpad momentum, and keyboard navigation.
- Manual: Toggle standard terminal (small/full) with history scrolled, confirming scroll restoration matches cached position.
- Automated: Extend `Tests/Runtime/TerminalTests.cs` with a launcher-focused test that feeds > `historyVisibleEntryCount` entries, asserts `_logScrollView.verticalScrollerVisibility == ScrollerVisibility.Visible`, and inspects the realized child opacities.
- Regression: Run existing runtime test suite to ensure non-launcher behaviours remain intact.

## Deliverables

- Updated runtime/UI code implementing the fixes and helper abstractions.
- USS adjustments (if needed) with before/after screenshots for documentation.
- New or updated tests proving scrollbar toggling and fade logic.
- Short developer note explaining the decision around virtualization for launcher history.
