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
        full states, completion hints, the command palette) plus the blank
        negative control that proves the bounds can fail, the light and dark
        theme surfaces, and the IMGUI inspector surfaces of the package's
        custom editors. Each capture writes a PNG and a manifest under
        .artifacts/t4/ and asserts the pixel bounds; teardown asserts zero
        RenderTexture leaks. Requires a graphics device, so a -nographics
        editor skips the suite.

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
            same blink phase as the first.
         */
        private const int BlinkSettleFrames = 40;
        private const float InspectorWidth = 520f;
        private const float InspectorHeight = 780f;
        private const int InspectorRepaintFloor = 3;

        private const string CompletionQuery = "capture-a";
        private const string PaletteQuery = "capture";
        private const string WrappedLine =
            "capture-wrap long line that must wrap across several visual rows: "
            + "0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ the quick brown fox jumps over the lazy dog "
            + "and keeps going so the log view has to break it into multiple rendered rows";

        private static readonly string[] CaptureCommandNames = { "capture-alpha", "capture-bravo" };

        private PanelSettings _panelSettings;
        private GameObject _surfaceObject;
        private TerminalUI _terminal;
        private CommandPaletteUI _palette;
        private StartTracker _tracker;
        private string _runDirectory;
        private int? _renderTexturesBefore;
        private CaptureOutcome _lastOutcome;
        private string _diagnosticsSuffix;

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

        private static void RegisterCaptureCommands()
        {
            for (int index = 0; index < CaptureCommandNames.Length; ++index)
            {
                string commandName = CaptureCommandNames[index];
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
            RegisterCaptureCommands();

            /*
                Tab completion is the surface players see: CompleteCommand
                fills the suggestion buffer for the typed prefix and the hint
                popup renders it with the selected row highlighted.
             */
            _terminal._commandInput.value = CompletionQuery;
            yield return null;
            _terminal.CompleteCommand();
            yield return WaitForCompletionHints();
            _terminal.SetCursorBlinkPaused(true);
            yield return CaptureSurface(
                nameof(CapturesCompletionHintsSurface),
                CaptureBounds.Default()
            );
            AssertAcceptable(_lastOutcome);
        }

        /*
            The palette's native TextField caret is frozen through its
            cursorColor for the capture; a repeat readback past the native
            blink interval must be byte-identical, proving the pixels no
            longer move with the blink phase.
         */
        [UnityTest]
        public IEnumerator CapturesCommandPaletteSurface()
        {
            yield return SpawnCalibratedTerminal(TerminalState.OpenSmall, withPalette: true);
            RegisterCaptureCommands();
            _palette.Open();
            yield return null;
            _palette._input.value = PaletteQuery;
            yield return WaitForPaletteRows();
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

                yield return WaitFrames(BlinkSettleFrames);
                VisualElement contentRoot = _terminal._uiDocument.rootVisualElement;
                string repeatPath = Path.Combine(_runDirectory, scenario + "-repeat.png");
                TerminalSurfaceCapture.CaptureToPng(target, contentRoot, repeatPath);
                byte[] expected = File.ReadAllBytes(_lastOutcome.PngPath);
                byte[] actual = File.ReadAllBytes(repeatPath);
                Assert.That(
                    actual,
                    Is.EqualTo(expected),
                    "The palette render must be blink-invariant with the caret frozen"
                );
            }
            finally
            {
                cursorFreeze.Dispose();
                DetachRenderTarget(target);
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
            yield return CaptureThemedSurface(LightThemeName, "CapturesLightThemeSurface", themed);
            yield return CaptureThemedSurface(DarkThemeName, "CapturesDarkThemeSurface", themed);
            AssertAcceptable(themed[0]);
            AssertAcceptable(themed[1]);

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
            through an IMGUIContainer on the capture panel. The closed
            terminal contributes no pixels, so each capture shows only the
            inspector surface under test.
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
            RenderTexture target = new RenderTexture(
                Screen.width,
                Screen.height,
                24,
                RenderTextureFormat.ARGB32
            )
            {
                name = "T4Capture-BlankControl",
            };
            Assert.IsTrue(target.Create(), "Sanity: the blank-control target created");
            _panelSettings.targetTexture = target;

            try
            {
                RenderTexture previousTarget = RenderTexture.active;
                RenderTexture.active = target;
                GL.Clear(true, true, Color.clear);
                RenderTexture.active = previousTarget;

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

        private IEnumerator SpawnTerminal()
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
            _terminal._fontPack = LoadPack<TerminalFontPack>(FontPackPath);
            _tracker = _surfaceObject.AddComponent<StartTracker>();
            _surfaceObject.SetActive(true);
            yield return new WaitUntil(() => _tracker.Started);
        }

        private IEnumerator SpawnCalibratedTerminal(TerminalState state, bool withPalette = false)
        {
            /*
                The host game view's zoom and Retina backing decide how many
                capture pixels one panel point gets. Measure the live panel
                against the frame it renders into and record the verdict in
                the manifest; scenario layouts that stay inside the small
                state capture cleanly at any stretch factor.
             */
            yield return SpawnTerminal();
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

        private IEnumerator WaitForCompletionHints()
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
                $"Completion candidates were computed for '{CompletionQuery}'"
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

        private IEnumerator WaitForPaletteRows()
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
                $"Palette matched capture commands for query '{PaletteQuery}'"
            );
            Assert.That(0 < _palette._rows.Count, "Palette built result rows for the query");
        }

        private IEnumerator CaptureSurface(string scenario, CaptureBounds bounds)
        {
            RenderTexture target = AttachRenderTarget(scenario);
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
            Inspector captures bypass the panel entirely: the draw surface
            paints the inspector into a standalone target during game view
            repaints, so the wait floors on real repaint passes instead of
            frame counts.
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
                inspector.Dispose();
                DetachRenderTarget(renderTarget);
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
            RenderTexture target = CreateRenderTarget(scenario);
            _panelSettings.targetTexture = target;
            return target;
        }

        /*
            Creates the capture target and clears it, so an inspector capture
            paints onto a known background instead of stale target contents.
         */
        private RenderTexture CreateRenderTarget(string scenario)
        {
            RenderTexture target = new RenderTexture(
                Screen.width,
                Screen.height,
                24,
                RenderTextureFormat.ARGB32
            )
            {
                name = $"T4Capture-{scenario}",
            };
            Assert.IsTrue(
                target.Create(),
                $"Sanity: the {Screen.width}x{Screen.height} capture target created"
            );
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
                ResolutionWidth = Screen.width,
                ResolutionHeight = Screen.height,
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
