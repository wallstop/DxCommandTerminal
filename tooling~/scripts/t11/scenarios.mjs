/*
    The T04/T11 capture scenario registries (issue #137): the pinned canon, the
    expected-incomplete negative controls, and the env-variant extensions. Pure
    data, stdlib-free, so Unity-free consumers (t11 gate, docs catalog lint)
    import them without pulling the MCP SDK; unity-mcp.mjs re-exports them for
    the bridge.

    The negative control proves the bounds can fail, and the closed-terminal
    capture proves the closed state actually produces that blank (a visible
    closed frame would mean close leaks pixels). Both are expected incomplete;
    neither is ever baselined because the store refuses blank pixels by design.
*/
export const T4_DEFAULT_SCENARIOS = Object.freeze([
  "CapturesTerminalSmallSurface",
  "CapturesTerminalFullSurfaceWithErrors",
  "CapturesCompletionHintsSurface",
  "CapturesScrollingLogSurface",
  "CapturesLongNameCompletionSurface",
  "CapturesCommandPaletteSurface",
  "CapturesPaletteEmptyResultsSurface",
  "CapturesPaletteLongDescriptionSurface",
  "CapturesLightThemeSurface",
  "CapturesDarkThemeSurface",
  "CapturesTerminalUIInspectorSurface",
  "CapturesThemePackInspectorSurface",
  "CapturesFontPackInspectorSurface"
]);
export const T4_EXPECTED_INCOMPLETE = Object.freeze([
  "BlankRenderFailsBounds",
  "CapturesClosedTerminalRendersNothing"
]);
/*
    Env-variant scenarios (T11): each pins one surface under a non-pinned
    environment (resolution/scale/font). They are captured and validated by
    every t4:capture run and baseline-compared like the pinned canon, but they
    never gate a pinned environment's coverage: resolution/scale variants live
    in their own environment directories, and font variants ride the pinned
    environment as an extra scenario (font is per-entry provenance, not part
    of the environment key).
*/
export const T4_VARIANT_SCENARIOS = Object.freeze([
  "CapturesNarrowScreenTerminalSmall",
  "CapturesWideScreenPaletteLongHelp",
  "CapturesScaleTwoTerminalSmall",
  "CapturesAlternateFontTerminalSmall",
  "CapturesTallScreenTerminalFull",
  "CapturesResizedViewportTerminalSmall"
]);
/** Every scenario a capture may baseline: the pinned canon plus variants. */
export const T4_ALL_SCENARIOS = Object.freeze([
  ...T4_DEFAULT_SCENARIOS,
  ...T4_VARIANT_SCENARIOS
]);
export const T4_TEST_FILTER = "TerminalSurfaceCapture";

/** Catalog group names, in docs display order (catalog schema + screenshots page). */
export const SCREENSHOT_GROUPS = Object.freeze([
  "terminal",
  "palette",
  "theme",
  "inspector",
  "environment-variant",
  "negative"
]);
