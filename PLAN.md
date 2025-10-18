# Launcher Scrollbar & Fade Remediation Plan

## Goals

- Surface a reliable vertical scrollbar in launcher mode only when history content exceeds the visible viewport.
- Restore launcher history fading so entries further from the input bar render with progressively lower opacity.
- Keep the solution maintainable even if that means trading away UI Toolkit virtualization for correctness.

## Current Behaviour

- Launcher mode clamps history height via `ApplyLauncherLayout` in `Runtime/CommandTerminal/UI/TerminalUI.LayoutView.cs:195`, yet the vertical scroller element inside the list view never appears despite mouse-wheel scrolling working.
- `LauncherViewController.UpdateFade` (`Runtime/CommandTerminal/UI/TerminalUI.LauncherViewController.cs:49`) runs, but rendered entries keep full opacity; neither the entry container nor nested label reflects the expected gradient.

## Root-Cause Hypotheses

### Scrollbar Absence

1. The scroller VisualElement (`unity-scroller--vertical`) might be forced to `display:none` or zero width by USS overrides in `Styles/BaseStyles.uss:32` (e.g., the tracker/dragger classes) once launcher classes are applied.
2. `InitializeScrollView` (`Runtime/CommandTerminal/UI/TerminalUI.cs:1426`) wires callbacks but never forces `scrollView.showVerticalScroller = true`; relying on `ScrollerVisibility.Auto` may fail because the computed `highValue` stays at `lowValue` while content still scrolls via pointer events.
3. Dynamic-height virtualization on `ListView` (`Runtime/CommandTerminal/UI/TerminalUI.HistoryListAdapter.cs:60`) could suppress the scroller element when realized child count stays below the configured viewport height, leaving no trigger for the scrollbar to become visible.

### Fade Breakdown

1. The fade pipeline is gated on `_launcherMetricsInitialized` (`Runtime/CommandTerminal/UI/TerminalUI.LayoutView.cs:386`) and may exit early whenever launcher layout updates run before the scroll view resolves geometry, leaving `UpdateFade` with an empty viewport rectangle.
2. Virtualization rebinding calls `BindLogListItem` (`Runtime/CommandTerminal/UI/TerminalUI.LogView.cs:29`), which resets `element.style.opacity = 1f` for launcher mode; if `HandleScrollValueChanged` is not invoked afterwards (e.g., touchpad inertial scroll), entries keep the reset opacity.
3. Project setups can exclude `TerminalHistoryFadeTargets.Launcher` from the appearance profile (`Runtime/CommandTerminal/UI/TerminalAppearanceProfile.cs:26`), so we need to confirm configuration is not simply disabling the effect.

## Investigation Tasks

1. Reproduce in-editor with UI Toolkit Debugger attached; inspect `unity-scroller--vertical` during overflow to confirm visibility, size, and USS state.
2. Instrument `LauncherLayoutSnapshot` outputs (already piped through diagnostics) to log `Scroller.highValue`, `lowValue`, `contentContainer.resolvedStyle.height`, and `verticalScrollerVisibility` when history entries exceed the cap.
3. Temporarily toggle `ListView.virtualizationMethod` to `CollectionVirtualizationMethod.None` to see if scrollbar/fade behaviour immediately returns, confirming a virtualization interaction.
4. Trace `HandleScrollValueChanged` invocations (subscribe to `_logScrollView.verticalScroller.valueChanged`) to ensure fade recalculations fire after `BindLogListItem` resets opacity and after momentum scrolling completes.
5. Verify the active `TerminalAppearanceProfile` in scenes/prefabs retains the `Launcher` flag for `_historyFadeTargets`; capture asset GUID if a custom profile overrides defaults.

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

- [x] Prevent launcher snap logic from running while closing so the exit tween respects the configured curves.\n- [x] Keep the container visible while height animation plays and reset launcher timing flags once the tween completes.
- [x] Expand overflow heuristics and defer scroller recalculation to eliminate the transient gap at the bottom of launcher history when commands stream in.\n- [ ] Disable virtualization on the launcher/terminal history list to keep entries realized during close/overflow transitions (pending 2021.3 check).
- [ ] Stress-test launcher with rapid command bursts to confirm the bottom padding no longer flashes before the first manual scroll.

### Shared Cleanup

- Centralise scroll/fade state checks into a tiny helper so both `BindLogListItem` and `LauncherViewController` consult the same readiness predicate (active state, metrics initialised, scroller present).
- Add optional editor gizmo or log entry showing the normalized distance used for opacity to aid future tuning.

## Testing & Validation

- Manual: With 2, 5, and 10 history entries, verify the scrollbar appears only once the viewport height cap is exceeded and that dragging the thumb scrolls correctly.
- Manual: Check opacity gradient before and after scrolling with mouse wheel, touchpad momentum, and keyboard navigation.
- Automated: Extend `Tests/Runtime/TerminalTests.cs` with a launcher-focused test that feeds > `historyVisibleEntryCount` entries, asserts `_logScrollView.verticalScrollerVisibility == ScrollerVisibility.Visible`, and inspects the realized child opacities.
- Regression: Run existing runtime test suite to ensure non-launcher behaviours remain intact.

## Deliverables

- Updated runtime/UI code implementing the fixes and helper abstractions.
- USS adjustments (if needed) with before/after screenshots for documentation.
- New or updated tests proving scrollbar toggling and fade logic.
- Short developer note explaining the decision around virtualization for launcher history.
