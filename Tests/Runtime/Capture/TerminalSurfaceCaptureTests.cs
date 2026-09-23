namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /*
        T04 fixture captures of the real package surfaces (terminal small and
        full states, completion hints, the command palette, the closed state)
        plus the blank negative control that proves the bounds can fail - the
        closed-state capture shares that expected-incomplete contract - and
        the light and dark theme surfaces, and the IMGUI inspector surfaces
        of the package's custom editors. Each capture writes a PNG and a
        manifest under .artifacts/t4/ and asserts the pixel bounds; teardown
        asserts zero RenderTexture leaks. Requires a graphics device, so a
        -nographics editor skips the suite.

        Scenarios open the terminal in the small state wherever possible:
        the host game view's zoom and Retina backing decide how many panel
        points one capture pixel gets, and small-state layouts stay inside
        the capture frame at every stretch factor (the full-state capture is
        kept for its log/error surface; golden baselines that pin the game
        view exactly arrive with T11).
     */
    public sealed class TerminalSurfaceCaptureTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const string ThemePackPath = "Packs/Themes/Medium.asset";
        private const string FontPackPath = "Packs/Fonts/Medium.asset";
        private const string LightThemeName = "light";
        private const string DarkThemeName = "dark";

        /*
            Readiness-poll headroom, not a fixed expectation: panels under
            editor throttling need more frames to land layout, caret state,
            and completion refreshes. Polls exit the frame they succeed.
         */
        private const int FrameBudget = 600;
        private const int SettleFrames = 3;

        /*
            The palette caret freeze must outlast the native UITK blink
            interval (~0.5 s) so the repeat capture cannot land inside the
            same blink phase as the first; wall-clock, not frames, because
            an uncapped editor frame rate would make a frame count vacuous.
         */
        private const float BlinkSettleSeconds = 0.7f;
        private const float InspectorWidth = 520f;
        private const float InspectorHeight = 780f;
        private const int InspectorRepaintFloor = 3;

        /*
            Repaint-probe headroom: a visible Game View repaints within a few
            frames; anything longer means the game view is missing or
            occluded, which the inspector capture needs.
         */
        private const int InspectorRepaintProbeFrames = 30;

        /*
            Keep in sync with T4_DEFAULT_SCENARIOS in
            tooling~/scripts/t11/scenarios.mjs (re-exported by unity-mcp.mjs),
            which fails t4:capture when any of these manifests is missing.
         */
        private const string LightScenarioName = "CapturesLightThemeSurface";
        private const string DarkScenarioName = "CapturesDarkThemeSurface";

        private const string CompletionQuery = "capture-a";
        private const string LongNameQuery = "capture-long-command";
        private const string PaletteQuery = "capture";
        private const string PaletteEmptyQuery = "zzz-matches-nothing";
        private const string LongHelpCommandName = "capture-described";
        private const string PaletteLongHelpQuery = LongHelpCommandName;
        private const int ScrollLineCount = 40;

        /*
            Env-variant capture environments (T11): deliberate extensions of
            the pinned host - a narrow screen, a wide screen that exercises
            the palette window width clamp and the help label's first-line
            wrap point, a 2x panel scale (Retina-style pixel density over the
            pinned point layout, 794x978 = 2x 397x489), a representative
            alternate font pack, a tall phone-shaped screen, and a
            mid-session viewport resize. Resolution and scale derive the
            environment key; theme and font ride per-scenario in each entry's
            provenance, so a font change fails provenance in place instead of
            minting a new environment.
         */
        private const string AltFontPackPath = "Packs/Fonts/Large.asset";
        private const float VariantPanelScale = 2f;
        private const int NarrowCaptureWidth = 300;
        private const int NarrowCaptureHeight = 489;
        private const int WideCaptureWidth = 640;
        private const int WideCaptureHeight = 360;
        private const int ScaleTwoCaptureWidth = 794;
        private const int ScaleTwoCaptureHeight = 978;
        private const int TallCaptureWidth = 397;
        private const int TallCaptureHeight = 852;
        private const int ResizedCaptureWidth = 560;
        private const int ResizedCaptureHeight = 489;
        private const int PinnedCaptureWidth = 397;
        private const int PinnedCaptureHeight = 489;
        private const string WrappedLine =
            "capture-wrap long line that must wrap across several visual rows: "
            + "0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ the quick brown fox jumps over the lazy dog "
            + "and keeps going so the log view has to break it into multiple rendered rows";
        private const string LongHelpLine =
            LongHelpCommandName
            + " help: a deliberately long description that must wrap across several rendered "
            + "rows inside the palette result list, exercising row growth, help-label wrapping, "
            + "and the scrolled result layout without truncation or clipping at the pinned host "
            + "resolution";

        private static readonly string[] CaptureCommandNames = { "capture-alpha", "capture-bravo" };

        private static readonly string[] LongNameCommands =
        {
            "capture-long-command-name-alpha-with-many-readable-segments",
            "capture-long-command-name-bravo-with-many-readable-segments",
            "capture-long-command-name-charlie-with-many-readable-segments",
        };

        private PanelSettings _panelSettings;
        private GameObject _surfaceObject;
        private TerminalUI _terminal;
        private CommandPaletteUI _palette;
        private StartTracker _tracker;
        private string _runDirectory;
        private int? _renderTexturesBefore;
        private CaptureOutcome _lastOutcome;
        private string _diagnosticsSuffix;
        private int _captureWidth;
        private int _captureHeight;

        private static string FormatBound(VisualElement element)
        {
            return element != null
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "({0:0.#},{1:0.#} {2:0.#}x{3:0.#})",
                    element.worldBound.x,
                    element.worldBound.y,
                    element.worldBound.width,
                    element.worldBound.height
                )
                : "null";
        }

        private static void AssertAcceptable(CaptureOutcome outcome)
        {
            Assert.That(
                outcome.Violations,
                Is.Empty,
                "Capture met the pixel bounds: " + string.Join("; ", outcome.Violations)
            );
            Assert.That(
                outcome.Manifest != null,
                "Every capture records a manifest next to its PNG"
            );
            Assert.That(0 < outcome.Metrics.PngBytes, "The PNG encoder produced a non-empty file");
        }

        private static void RegisterFixtureCommands(string[] names)
        {
            for (int index = 0; index < names.Length; ++index)
            {
                string commandName = names[index];
                Terminal.Shell.AddCommand(
                    commandName,
                    _ => { },
                    help: $"Capture fixture command {commandName}"
                );
            }
        }

        private static T LoadPack<T>(string relativePath)
            where T : ScriptableObject
        {
#if UNITY_EDITOR
            T pack = UnityEditor.AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.That(pack != null, $"Expected the asset pack at {PackageRoot}/{relativePath}");
            return pack;
#else
            return null;
#endif
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!TerminalSurfaceCapture.IsSupported)
            {
                Assert.Ignore(TerminalSurfaceCapture.UnsupportedReason);
            }

            _runDirectory = TerminalSurfaceCapture.CreateRunDirectory();
            _renderTexturesBefore = TerminalSurfaceCapture.CountRenderTextures();
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_panelSettings != null)
            {
                _panelSettings.targetTexture = null;
            }

            if (_surfaceObject != null)
            {
                Object.Destroy(_surfaceObject);
            }

            if (_panelSettings != null)
            {
                Object.Destroy(_panelSettings);
            }

            yield return null;

            /*
                The baseline only exists when SetUp ran past its skip check;
                an ignored (-nographics) run must not fail teardown.
             */
            if (_renderTexturesBefore.HasValue)
            {
                Assert.AreEqual(
                    _renderTexturesBefore.Value,
                    TerminalSurfaceCapture.CountRenderTextures(),
                    "Capture left RenderTexture leaks behind"
                );
            }
        }

        [UnityTest]
        public IEnumerator CapturesTerminalSmallSurface()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall);
            Terminal.Log("capture-small ready");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesTerminalSmallSurface),
                CaptureBounds.Default()
            );
            AssertAcceptable(_lastOutcome);
        }

        [UnityTest]
        public IEnumerator CapturesTerminalFullSurfaceWithErrors()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenFull);
            Terminal.Log("capture-full ready");
            Terminal.Log(TerminalLogType.Warning, "capture-full warning line");
            Terminal.Log(TerminalLogType.Error, "capture-full error line");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesTerminalFullSurfaceWithErrors),
                CaptureBounds.Default()
            );
            AssertAcceptable(_lastOutcome);
        }

        [UnityTest]
        public IEnumerator CapturesCompletionHintsSurface()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall);
            RegisterFixtureCommands(CaptureCommandNames);

            /*
                Tab completion is the surface players see: CompleteCommand
                fills the suggestion buffer for the typed prefix and the hint
                popup renders it with the selected row highlighted.
             */
            _terminal._commandInput.value = CompletionQuery;
            yield return null;
            _terminal.CompleteCommand();
            yield return WaitForCompletionHints(CompletionQuery);
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesCompletionHintsSurface),
                CaptureBounds.Default()
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            Scrolling surface: enough log volume to overflow the small-state
            log view, so the scrollbar engages and the auto scroll-to-end
            pins the visible window to the newest lines (the wrapped line
            plus an error line ride along for content variety).
         */
        [UnityTest]
        public IEnumerator CapturesScrollingLogSurface()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall);
            for (int line = 1; line <= ScrollLineCount; ++line)
            {
                Terminal.Log(
                    "capture-scroll line "
                        + line.ToString(CultureInfo.InvariantCulture)
                        + " of "
                        + ScrollLineCount.ToString(CultureInfo.InvariantCulture)
                );
            }

            Terminal.Log(TerminalLogType.Error, "capture-scroll error line");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);
            yield return WaitForLogScrollSettled();
            yield return CaptureSurface(
                nameof(CapturesScrollingLogSurface),
                CaptureBounds.Default()
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            Long command names through the legacy completion path: the hint
            popup rows must lay out names far wider than the short fixture
            names, pinning the popup width behavior at the host resolution.
         */
        [UnityTest]
        public IEnumerator CapturesLongNameCompletionSurface()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall);
            RegisterFixtureCommands(LongNameCommands);
            _terminal._commandInput.value = LongNameQuery;
            yield return null;
            _terminal.CompleteCommand();
            yield return WaitForCompletionHints(LongNameQuery);
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesLongNameCompletionSurface),
                CaptureBounds.Default()
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            Palette empty results: a query that matches nothing leaves the
            palette open with the input rendered and zero result rows - the
            no-match feedback surface players see on a typo.
         */
        [UnityTest]
        public IEnumerator CapturesPaletteEmptyResultsSurface()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall, withPalette: true);
            RegisterFixtureCommands(CaptureCommandNames);
            _palette.Open();
            yield return null;
            _palette._input.value = PaletteEmptyQuery;
            yield return SettleRenders();
            Assert.That(
                _palette._matchNames,
                Is.Empty,
                $"The palette matched nothing for query '{PaletteEmptyQuery}'"
            );
            yield return CapturePaletteSurface(nameof(CapturesPaletteEmptyResultsSurface));
            AssertAcceptable(_lastOutcome);
        }

        /*
            Palette long help: a result row whose name and description far
            exceed the short fixture commands, pinning row, selection, and
            window layout under a wrapped multi-line help label. The pinned
            host frame cuts the row at the name column, so glyph-level help
            wrapping becomes visible only with resolution-variant
            environments; this capture still fails any regression that
            shifts row or window layout.
         */
        [UnityTest]
        public IEnumerator CapturesPaletteLongDescriptionSurface()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall, withPalette: true);
            Terminal.Shell.AddCommand(LongHelpCommandName, _ => { }, help: LongHelpLine);
            _palette.Open();
            yield return null;
            _palette._input.value = PaletteLongHelpQuery;
            yield return WaitForPaletteRows(PaletteLongHelpQuery);
            yield return CapturePaletteSurface(nameof(CapturesPaletteLongDescriptionSurface));
            AssertAcceptable(_lastOutcome);
        }

        /*
            Env-variant captures (T11): each pins a real package surface under
            a deliberate environment extension of the pinned host - narrow and
            wide screens, 2x panel pixel density, an alternate representative
            font. Layout decisions tuned to the pinned resolution can regress
            without moving the pinned baselines, so these variants pin them
            separately; the manifest's resolution/scale/font provenance
            derives each environment key, and baselines never compare across
            environments.
         */
        [UnityTest]
        public IEnumerator CapturesNarrowScreenTerminalSmall()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall);
            Terminal.Log("capture-narrow ready");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesNarrowScreenTerminalSmall),
                CaptureBounds.Default(),
                NarrowCaptureWidth,
                NarrowCaptureHeight
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            Wide screen with the long-help palette row: the palette window is
            a serialized point width clamped by the frame, so the pinned
            397pt frame hides the clamp while this one exercises it (640pt
            window in a 640pt frame), and the wider row gives the help label
            enough space to expose its first-line wrap point. Multi-line row
            growth stays invisible at any width: rows pin a fixed serialized
            rowHeight, so wrapping clips inside the row by design.
         */
        [UnityTest]
        public IEnumerator CapturesWideScreenPaletteLongHelp()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall, withPalette: true);
            Terminal.Shell.AddCommand(LongHelpCommandName, _ => { }, help: LongHelpLine);
            _palette.Open();
            yield return null;
            _palette._input.value = PaletteLongHelpQuery;
            yield return WaitForPaletteRows(PaletteLongHelpQuery);
            yield return CapturePaletteSurface(
                nameof(CapturesWideScreenPaletteLongHelp),
                WideCaptureWidth,
                WideCaptureHeight
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            2x panel scale over the doubled pinned frame (794x978 = 2x
            397x489): the point layout matches the pinned host exactly, so
            any pixel regression here is a density-handling bug, not a
            re-layout.
         */
        [UnityTest]
        public IEnumerator CapturesScaleTwoTerminalSmall()
        {
            yield return SpawnCalibratedTerminal(
                TerminalState.OpenSmall,
                panelScale: VariantPanelScale
            );
            Terminal.Log("capture-scale-two ready");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesScaleTwoTerminalSmall),
                CaptureBounds.Default(),
                ScaleTwoCaptureWidth,
                ScaleTwoCaptureHeight
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            A representative alternate font pack at the pinned frame: pins
            that the shipped packs render into the terminal correctly and
            gives the font provenance a second, distinct value to gate on.
         */
        [UnityTest]
        public IEnumerator CapturesAlternateFontTerminalSmall()
        {
            yield return SpawnCalibratedTerminal(
                TerminalState.OpenSmall,
                fontPackPath: AltFontPackPath
            );
            Terminal.Log("capture-font ready");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesAlternateFontTerminalSmall),
                CaptureBounds.Default(),
                PinnedCaptureWidth,
                PinnedCaptureHeight
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            Tall screen with the full state (397x852, phone-shaped): the
            full-state window height derives from the game view (Screen), so
            the band keeps the game-view-derived height on a taller viewport
            instead of stretching. Pins that derivation and the blank
            remainder it leaves below the top-anchored band.
         */
        [UnityTest]
        public IEnumerator CapturesTallScreenTerminalFull()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenFull);
            Terminal.Log("capture-tall ready");
            Terminal.Log(TerminalLogType.Warning, "capture-tall warning line");
            Terminal.Log(TerminalLogType.Error, "capture-tall error line");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesTallScreenTerminalFull),
                CaptureBounds.Default(),
                TallCaptureWidth,
                TallCaptureHeight
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            Mid-session viewport widening: the panel lays out at the pinned
            frame first, then the render target swaps to a wider frame and
            the capture runs after the re-layout settles. The terminal sizes
            from Screen, so the resize must re-layout the panel viewport
            without stretching or repositioning the terminal band. The
            re-layout poll, the settle, and the committed-baseline pixel
            compare together keep a half-migrated frame out of the store.
         */
        [UnityTest]
        public IEnumerator CapturesResizedViewportTerminalSmall()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall);
            Terminal.Log("capture-resize ready");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);

            RenderTexture preResize = AttachRenderTarget(
                nameof(CapturesResizedViewportTerminalSmall),
                PinnedCaptureWidth,
                PinnedCaptureHeight
            );
            yield return SettleRenders();
            VisualElement uiRoot = _terminal._uiDocument.rootVisualElement;
            float pinnedLayoutWidth = uiRoot.layout.width;
            DetachRenderTarget(preResize);

            RenderTexture resized = AttachRenderTarget(
                nameof(CapturesResizedViewportTerminalSmall),
                ResizedCaptureWidth,
                ResizedCaptureHeight
            );
            try
            {
                int frameBudget = FrameBudget;
                while (0 < frameBudget-- && uiRoot.layout.width <= pinnedLayoutWidth)
                {
                    yield return null;
                }

                Assert.Greater(
                    uiRoot.layout.width,
                    pinnedLayoutWidth,
                    "The panel re-laid out for the widened viewport before the capture"
                );
                yield return SettleRenders();
                _lastOutcome = FinishCapture(
                    nameof(CapturesResizedViewportTerminalSmall),
                    resized,
                    CaptureBounds.Default(),
                    uiRoot
                );
            }
            finally
            {
                DetachRenderTarget(resized);
            }

            AssertAcceptable(_lastOutcome);
        }

        /*
            Closed state: after a close the terminal paints nothing - the
            shared document root collapses to zero height with the input
            hidden. The capture must fail the bounds exactly like the blank
            negative control (a visible frame would mean close leaks
            pixels), so it is expected-incomplete in t4:capture and never
            baselined: the store refuses blank pixels by design.
         */
        [UnityTest]
        public IEnumerator CapturesClosedTerminalRendersNothing()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall);
            Terminal.Log("capture-close ready");
            _terminal.SetState(TerminalState.Closed);
            yield return WaitForTerminalClosed();
            yield return CaptureSurface(
                nameof(CapturesClosedTerminalRendersNothing),
                CaptureBounds.Default()
            );
            Assert.That(
                _lastOutcome.Violations,
                Is.Not.Empty,
                "A closed terminal must fail the capture bounds like the blank control"
            );
            Assert.IsFalse(
                _lastOutcome.Manifest.Complete,
                "A closed terminal must record an incomplete manifest"
            );
        }

        /*
            The palette's native TextField caret is frozen through its
            cursorColor for the capture; a repeat capture past the native
            blink interval must be byte-identical, proving the pixels no
            longer move with the blink phase. Hosts where the freeze cannot
            engage skip the invariance proof instead of asserting against an
            unfrozen caret.
         */
        [UnityTest]
        public IEnumerator CapturesCommandPaletteSurface()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall, withPalette: true);
            RegisterFixtureCommands(CaptureCommandNames);
            _palette.Open();
            yield return null;
            _palette._input.value = PaletteQuery;
            yield return WaitForPaletteRows(PaletteQuery);
            _terminal.SetCursorBlinkPaused(true);

            string scenario = nameof(CapturesCommandPaletteSurface);
            RenderTexture target = AttachRenderTarget(scenario);
            TerminalSurfaceCapture.CursorFreezeScope cursorFreeze =
                TerminalSurfaceCapture.FreezeCursor(_palette._input);
            try
            {
                _diagnosticsSuffix = "cursorFreeze=" + cursorFreeze.Describe();
                yield return SettleRenders();
                _lastOutcome = FinishCapture(
                    scenario,
                    target,
                    CaptureBounds.Default(),
                    _terminal._uiDocument.rootVisualElement
                );
                AssertAcceptable(_lastOutcome);
                if (!cursorFreeze.Engaged)
                {
                    Assert.Ignore(
                        "The caret freeze could not engage; skipping the blink-invariance "
                            + $"proof ({cursorFreeze.Describe()})"
                    );
                }

                yield return WaitSeconds(BlinkSettleSeconds);
                CaptureOutcome repeat = FinishCapture(
                    scenario + "-repeat",
                    target,
                    CaptureBounds.Default(),
                    _terminal._uiDocument.rootVisualElement
                );
                AssertAcceptable(repeat);
                byte[] expected = File.ReadAllBytes(_lastOutcome.PngPath);
                byte[] actual = File.ReadAllBytes(repeat.PngPath);
                Assert.That(
                    actual,
                    Is.EqualTo(expected),
                    "The palette render must be blink-invariant with the caret frozen"
                );
            }
            finally
            {
                try
                {
                    cursorFreeze.Dispose();
                }
                finally
                {
                    DetachRenderTarget(target);
                }
            }
        }

        /*
            Light/dark scenario coverage: one run per theme from the shipped
            Medium pack, and both captures must render distinct backgrounds so
            a theme sheet that silently fails to apply cannot pass.
         */
        [UnityTest]
        public IEnumerator CapturesLightAndDarkThemeSurfaces()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall);
            Terminal.Log("capture-themes ready");
            Terminal.Log(WrappedLine);
            _terminal.SetCursorBlinkPaused(true);
            List<CaptureOutcome> themed = new List<CaptureOutcome>(2);
            yield return CaptureThemedSurface(LightThemeName, LightScenarioName, themed);
            yield return CaptureThemedSurface(DarkThemeName, DarkScenarioName, themed);
            AssertAcceptable(themed[0]);
            AssertAcceptable(themed[1]);

            /*
                The distinct-background assert reads the modal color of the
                whole frame and is host-robust because the terminal panel's
                root paints the active theme's background across the frame
                (backgroundFraction ~0.66 on the pinned host with the
                content sharing the rest), not just the small-state band.
             */
            CapturePixelMetrics light = themed[0].Metrics;
            CapturePixelMetrics dark = themed[1].Metrics;
            bool distinctBackground =
                light.BackgroundRed != dark.BackgroundRed
                || light.BackgroundGreen != dark.BackgroundGreen
                || light.BackgroundBlue != dark.BackgroundBlue;
            Assert.IsTrue(
                distinctBackground,
                "The light and dark themes must render distinct backgrounds: "
                    + $"light #{light.BackgroundRed:X2}{light.BackgroundGreen:X2}{light.BackgroundBlue:X2} vs "
                    + $"dark #{dark.BackgroundRed:X2}{dark.BackgroundGreen:X2}{dark.BackgroundBlue:X2}"
            );
        }

        /*
            IMGUI inspector surfaces of the package's custom editors, drawn
            through an InspectorDrawSurface that redirects the game view's
            IMGUI pass into the capture target. The closed terminal
            contributes no pixels, so each capture shows only the inspector
            surface under test.
         */
        [UnityTest]
        public IEnumerator CapturesTerminalUIInspectorSurface()
        {
            if (!EditorInspectorCapture.IsSupported)
            {
                Assert.Ignore(EditorInspectorCapture.UnsupportedReason);
            }

            yield return SpawnTerminal();
            yield return CaptureInspectorSurface(
                nameof(CapturesTerminalUIInspectorSurface),
                _terminal
            );
            AssertAcceptable(_lastOutcome);
        }

        [UnityTest]
        public IEnumerator CapturesThemePackInspectorSurface()
        {
            if (!EditorInspectorCapture.IsSupported)
            {
                Assert.Ignore(EditorInspectorCapture.UnsupportedReason);
            }

            yield return SpawnTerminal();
            yield return CaptureInspectorSurface(
                nameof(CapturesThemePackInspectorSurface),
                LoadPack<TerminalThemePack>(ThemePackPath)
            );
            AssertAcceptable(_lastOutcome);
        }

        [UnityTest]
        public IEnumerator CapturesFontPackInspectorSurface()
        {
            if (!EditorInspectorCapture.IsSupported)
            {
                Assert.Ignore(EditorInspectorCapture.UnsupportedReason);
            }

            yield return SpawnTerminal();
            yield return CaptureInspectorSurface(
                nameof(CapturesFontPackInspectorSurface),
                LoadPack<TerminalFontPack>(FontPackPath)
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            Negative control: a render target cleared to transparent and fed
            by no panel content must fail the bounds. This pins that the
            harness can reject blank output instead of approving every
            capture.
         */
        [UnityTest]
        public IEnumerator BlankRenderFailsBounds()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();

            /*
                The blank control renders no panel content at all, so the
                cleared target must stay blank through the settle and fail
                the bounds. It shares the standard target factory so the
                manifest records the same resolution provenance as every
                other capture.
             */
            RenderTexture target = CreateRenderTarget(nameof(BlankRenderFailsBounds));
            _panelSettings.targetTexture = target;

            try
            {
                yield return SettleRenders();
                _lastOutcome = FinishCapture(
                    nameof(BlankRenderFailsBounds),
                    target,
                    CaptureBounds.Default(),
                    null
                );
            }
            finally
            {
                DetachRenderTarget(target);
            }

            Assert.That(
                _lastOutcome.Violations,
                Is.Not.Empty,
                "A blank render must violate the capture bounds"
            );
            Assert.IsFalse(
                _lastOutcome.Manifest.Complete,
                "A blank render must record an incomplete manifest"
            );
        }

        private IEnumerator SpawnTerminal(
            float? panelScale = null,
            string fontPackPath = FontPackPath
        )
        {
            _diagnosticsSuffix = string.Empty;
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();

            /*
                A runtime-created PanelSettings carries no theme style sheet;
                without one the panel lays out but never renders pixels ("No
                Theme Style Sheet set" warning). The package's own base sheet
                gives the fixture the real shipped look.
             */
            _panelSettings.themeStyleSheet = LoadPack<ThemeStyleSheet>(
                "Styles/TerminalThemeSettings-Base.tss"
            );

            /*
                The panel freezes its scale at creation, so a variant scale
                must land before the document goes live.
             */
            if (panelScale.HasValue)
            {
                _panelSettings.scale = panelScale.Value;
            }

            _surfaceObject = new GameObject("T4CaptureTerminal");
            _surfaceObject.SetActive(false);
            UIDocument document = _surfaceObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _surfaceObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadPack<TerminalThemePack>(ThemePackPath);
            _terminal._fontPack = LoadPack<TerminalFontPack>(fontPackPath);
            _tracker = _surfaceObject.AddComponent<StartTracker>();
            _surfaceObject.SetActive(true);
            yield return new WaitUntil(() => _tracker.Started);
        }

        private IEnumerator SpawnCalibratedTerminal(
            TerminalState state,
            bool withPalette = false,
            float? panelScale = null,
            string fontPackPath = FontPackPath
        )
        {
            /*
                The host game view's zoom and Retina backing decide how many
                capture pixels one panel point gets. Measure the live panel
                against the frame it renders into and record the verdict in
                the manifest; scenario layouts that stay inside the small
                state capture cleanly at any stretch factor.
             */
            yield return SpawnTerminal(panelScale, fontPackPath);
            if (withPalette)
            {
                AttachPalette();
            }

            _terminal.SetState(state);
            yield return WaitForTerminalInputVisible();
        }

        private void AttachPalette()
        {
            _palette = _surfaceObject.AddComponent<CommandPaletteUI>();
            _palette._uiDocument = _terminal._uiDocument;
            _palette._themePack = _terminal._themePack;
        }

        private IEnumerator WaitForTerminalInputVisible()
        {
            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && (
                    _terminal._commandInput == null
                    || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex
                )
            )
            {
                yield return null;
            }

            Assert.That(
                _terminal._commandInput != null,
                "The terminal input field exists after the terminal opens"
            );
            Assert.AreEqual(
                DisplayStyle.Flex,
                _terminal._commandInput.resolvedStyle.display,
                "The terminal input is visible after the terminal opens"
            );
        }

        private IEnumerator WaitForCompletionHints(string query)
        {
            /*
                Hints render into the AutoCompletePopup container; the popup
                itself is private, so readiness reads the public visual tree
                plus the internal completion buffer.
             */
            VisualElement hintPopup()
            {
                return _terminal._uiDocument.rootVisualElement.Q("AutoCompletePopup");
            }

            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && (
                    _terminal._lastCompletionBuffer.Count == 0
                    || hintPopup() == null
                    || hintPopup().childCount == 0
                    || DisplayStyle.Flex != hintPopup().resolvedStyle.display
                )
            )
            {
                yield return null;
            }

            Assert.That(
                0 < _terminal._lastCompletionBuffer.Count,
                $"Completion candidates were computed for '{query}'"
            );
            VisualElement popup = hintPopup();
            Assert.That(popup != null, "Completion hint popup exists for the query");
            Assert.That(0 < popup.childCount, "Completion hint rows were built for the query");
            Assert.AreEqual(
                DisplayStyle.Flex,
                popup.resolvedStyle.display,
                "Completion hints are visible for the query"
            );
        }

        private IEnumerator WaitForLogScrollSettled()
        {
            ScrollView LogView()
            {
                return _terminal._uiDocument.rootVisualElement.Q("LogScrollView") as ScrollView;
            }

            /*
                The runtime scroll-to-end pin is one-shot, so require two
                consecutive stable frames: a late re-layout that grows the
                scroll extent must not slip through between pin and capture.
             */
            int frameBudget = FrameBudget;
            bool settledOnce = false;
            while (0 < frameBudget--)
            {
                Scroller scroller = LogView()?.verticalScroller;
                bool settled =
                    scroller != null
                    && 0f < scroller.highValue
                    && scroller.highValue - 0.5f <= scroller.value;
                if (settled && settledOnce)
                {
                    break;
                }

                settledOnce = settled;
                yield return null;
            }

            Scroller verticalScroller = LogView()?.verticalScroller;
            Assert.That(
                verticalScroller != null,
                "The log scroll view exposes a vertical scroller"
            );
            Assert.Greater(
                verticalScroller.highValue,
                0f,
                "The log content overflows the log view so the scrollbar engages"
            );
            Assert.Less(
                verticalScroller.highValue - verticalScroller.value,
                0.5f,
                "The log view settled at the scrolled-to-end position"
            );
        }

        private IEnumerator WaitForPaletteRows(string query)
        {
            int frameBudget = FrameBudget;
            while (
                0 < frameBudget-- && (_palette._matchNames.Count == 0 || _palette._rows.Count == 0)
            )
            {
                yield return null;
            }

            Assert.That(
                0 < _palette._matchNames.Count,
                $"Palette matched commands for query '{query}'"
            );
            Assert.That(0 < _palette._rows.Count, "Palette built result rows for the query");
        }

        /*
            Closed readiness: IsClosed also requires the window height to
            converge, so polling it (plus the hidden input container) lands
            the capture after the final closed layout, not the transition
            frame.
         */
        private IEnumerator WaitForTerminalClosed()
        {
            VisualElement inputContainer()
            {
                return _terminal._uiDocument.rootVisualElement.Q("InputContainer");
            }

            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && (
                    !_terminal.IsClosed
                    || inputContainer() == null
                    || inputContainer().resolvedStyle.display != DisplayStyle.None
                )
            )
            {
                yield return null;
            }

            Assert.IsTrue(_terminal.IsClosed, "The terminal reached the closed state");
            VisualElement closedInputContainer = inputContainer();
            Assert.That(
                closedInputContainer != null,
                "The input container exists after the terminal closes"
            );
            Assert.AreEqual(
                DisplayStyle.None,
                closedInputContainer.resolvedStyle.display,
                "The terminal input is hidden after the terminal closes"
            );
        }

        private IEnumerator CaptureSurface(string scenario, CaptureBounds bounds)
        {
            yield return CaptureSurface(scenario, bounds, Screen.width, Screen.height);
        }

        private IEnumerator CaptureSurface(
            string scenario,
            CaptureBounds bounds,
            int width,
            int height
        )
        {
            RenderTexture target = AttachRenderTarget(scenario, width, height);
            try
            {
                yield return SettleRenders();
                _lastOutcome = FinishCapture(
                    scenario,
                    target,
                    bounds,
                    _terminal._uiDocument.rootVisualElement
                );
            }
            finally
            {
                DetachRenderTarget(target);
            }
        }

        /*
            Single-capture variant of the palette freeze sequence: both the
            palette caret freeze and the terminal caret pause apply before
            the settle; whether the freeze engaged is recorded in the
            manifest diagnostics, and the baseline gate only runs on the
            pinned host where it engages.
         */
        private IEnumerator CapturePaletteSurface(string scenario)
        {
            yield return CapturePaletteSurface(scenario, Screen.width, Screen.height);
        }

        private IEnumerator CapturePaletteSurface(string scenario, int width, int height)
        {
            RenderTexture target = AttachRenderTarget(scenario, width, height);
            TerminalSurfaceCapture.CursorFreezeScope cursorFreeze =
                TerminalSurfaceCapture.FreezeCursor(_palette._input);
            try
            {
                _terminal.SetCursorBlinkPaused(true);
                _diagnosticsSuffix = "cursorFreeze=" + cursorFreeze.Describe();
                yield return SettleRenders();
                _lastOutcome = FinishCapture(
                    scenario,
                    target,
                    CaptureBounds.Default(),
                    _terminal._uiDocument.rootVisualElement
                );
            }
            finally
            {
                try
                {
                    cursorFreeze.Dispose();
                }
                finally
                {
                    DetachRenderTarget(target);
                }
            }
        }

        /*
            Inspector captures bypass the panel entirely: the draw surface
            paints the inspector into a standalone target during game view
            repaints, so the wait floors on real repaint passes instead of
            frame counts, and a missing or occluded Game View fails fast
            with its own diagnostic.
         */
        private IEnumerator CaptureInspectorSurface(string scenario, UnityEngine.Object target)
        {
            RenderTexture renderTarget = CreateRenderTarget(scenario);
            EditorInspectorCapture.InspectorScope inspector = EditorInspectorCapture.Attach(
                _surfaceObject,
                target,
                renderTarget,
                InspectorWidth,
                InspectorHeight
            );
            try
            {
                InspectorDrawSurface surface = _surfaceObject.GetComponent<InspectorDrawSurface>();
                Assert.That(surface != null, "Sanity: the inspector draw surface attached");
                yield return WaitFrames(InspectorRepaintProbeFrames);
                Assert.GreaterOrEqual(
                    surface.RepaintCount,
                    1,
                    "The Game View repainted the inspector; inspector capture needs a "
                        + "visible Game View"
                );
                int frameBudget = FrameBudget;
                while (0 < frameBudget-- && surface.RepaintCount < InspectorRepaintFloor)
                {
                    yield return null;
                }

                Assert.GreaterOrEqual(
                    surface.RepaintCount,
                    InspectorRepaintFloor,
                    "The game view painted the inspector into the capture target"
                );
                yield return WaitFrames(SettleFrames);
                _lastOutcome = FinishCapture(scenario, renderTarget, CaptureBounds.Default(), null);
            }
            finally
            {
                try
                {
                    inspector.Dispose();
                }
                finally
                {
                    DetachRenderTarget(renderTarget);
                }
            }
        }

        private IEnumerator SettleRenders()
        {
            yield return WaitFrames(SettleFrames);
        }

        private IEnumerator WaitFrames(int frameCount)
        {
            for (int frame = 0; frame < frameCount; ++frame)
            {
                yield return null;
            }
        }

        private IEnumerator WaitSeconds(float seconds)
        {
            float start = Time.unscaledTime;
            while (Time.unscaledTime - start < seconds)
            {
                yield return null;
            }
        }

        private IEnumerator CaptureThemedSurface(
            string friendlyTheme,
            string scenario,
            List<CaptureOutcome> outcomes
        )
        {
            _terminal.SetTheme(friendlyTheme);
            yield return WaitForTheme(friendlyTheme);
            yield return CaptureSurface(scenario, CaptureBounds.Default());
            outcomes.Add(_lastOutcome);
        }

        private IEnumerator WaitForTheme(string friendlyTheme)
        {
            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && !string.Equals(
                    _terminal.CurrentFriendlyTheme,
                    friendlyTheme,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                yield return null;
            }

            Assert.AreEqual(
                friendlyTheme,
                _terminal.CurrentFriendlyTheme,
                $"Theme '{friendlyTheme}' applied before capture"
            );
        }

        private CaptureOutcome FinishCapture(
            string scenario,
            RenderTexture target,
            CaptureBounds bounds,
            VisualElement contentRoot
        )
        {
            string pngPath = Path.Combine(_runDirectory, scenario + ".png");
            CapturePixelMetrics metrics = TerminalSurfaceCapture.CaptureToPng(
                target,
                contentRoot,
                pngPath
            );
            CaptureOutcome outcome = new CaptureOutcome
            {
                Metrics = metrics,
                Violations = TerminalSurfaceCapture.Evaluate(metrics, bounds),
                PngPath = pngPath,
            };
            outcome.Manifest = BuildManifest(scenario, bounds, outcome);
            TerminalSurfaceCapture.WriteManifest(
                outcome.Manifest,
                Path.Combine(_runDirectory, scenario + ".manifest.json")
            );
            return outcome;
        }

        private RenderTexture AttachRenderTarget(string scenario)
        {
            return AttachRenderTarget(scenario, Screen.width, Screen.height);
        }

        private RenderTexture AttachRenderTarget(string scenario, int width, int height)
        {
            RenderTexture target = CreateRenderTarget(scenario, width, height);
            _panelSettings.targetTexture = target;
            return target;
        }

        /*
            Creates the capture target and clears it, so an inspector capture
            paints onto a known background instead of stale target contents.
         */
        private RenderTexture CreateRenderTarget(string scenario)
        {
            return CreateRenderTarget(scenario, Screen.width, Screen.height);
        }

        private RenderTexture CreateRenderTarget(string scenario, int width, int height)
        {
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = $"T4Capture-{scenario}",
            };
            Assert.IsTrue(target.Create(), $"Sanity: the {width}x{height} capture target created");
            _captureWidth = width;
            _captureHeight = height;
            RenderTexture previousTarget = RenderTexture.active;
            RenderTexture.active = target;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previousTarget;
            return target;
        }

        private void DetachRenderTarget(RenderTexture target)
        {
            if (_panelSettings != null)
            {
                _panelSettings.targetTexture = null;
            }

            target.Release();
            Object.DestroyImmediate(target);
        }

        private CaptureManifest BuildManifest(
            string scenario,
            CaptureBounds bounds,
            CaptureOutcome outcome
        )
        {
            Font currentFont = _terminal != null ? _terminal.CurrentFont : null;
            return new CaptureManifest
            {
                Scenario = scenario,
                CapturedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Complete = 0 == outcome.Violations.Count,
                ResolutionWidth = _captureWidth,
                ResolutionHeight = _captureHeight,
                LogicalScale = _panelSettings.scale,
                ColorSpace = QualitySettings.activeColorSpace.ToString(),
                UnityVersion = Application.unityVersion,
                GraphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                Theme = _terminal != null ? _terminal.CurrentTheme : null,
                Font = currentFont != null ? currentFont.name : null,
                Revision = TerminalSurfaceCapture.TryReadRevision(),
                PngFile = Path.GetFileName(outcome.PngPath),
                Metrics = outcome.Metrics,
                Bounds = bounds,
                Violations = outcome.Violations,
                Diagnostics = DescribeSurface() + _diagnosticsSuffix,
            };
        }

        /*
            Compact tree fingerprint recorded with every capture: when pixels
            and expectations disagree, the manifest says which side lied.
         */
        private string DescribeSurface()
        {
            if (_terminal == null)
            {
                return "terminal=null";
            }

            VisualElement uiRoot = _terminal._uiDocument.rootVisualElement;
            VisualElement hintPopup = uiRoot.Q("AutoCompletePopup");
            VisualElement terminalRoot = uiRoot.Q("TerminalRoot");
            VisualElement terminalContainer = uiRoot.Q("TerminalContainer");
            VisualElement logScrollView = uiRoot.Q("LogScrollView");
            VisualElement inputContainer = uiRoot.Q("InputContainer");
            return "terminalClosed="
                + _terminal.IsClosed
                + " screen="
                + Screen.width
                + "x"
                + Screen.height
                + " uiRootBound="
                + FormatBound(uiRoot)
                + " terminalRootBound="
                + FormatBound(terminalRoot)
                + " containerBound="
                + FormatBound(terminalContainer)
                + " logBound="
                + FormatBound(logScrollView)
                + " inputBound="
                + FormatBound(inputContainer)
                + " inputDisplay="
                + (
                    inputContainer != null
                        ? inputContainer.resolvedStyle.display.ToString()
                        : "null"
                )
                + " input="
                + (_terminal._commandInput != null ? _terminal._commandInput.value : "null")
                + " hintPopupChildren="
                + (hintPopup?.childCount.ToString(CultureInfo.InvariantCulture) ?? "null");
        }

        private sealed class CaptureOutcome
        {
            public CapturePixelMetrics Metrics { get; set; }
            public List<string> Violations { get; set; }
            public string PngPath { get; set; }
            public CaptureManifest Manifest { get; set; }
        }
    }
}
