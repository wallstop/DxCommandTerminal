namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Globalization;
    using Backend;
    using Components;
    using Input;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        The log view follows new output until the developer scrolls away. The
        decision itself is engine-independent and lives in LogTailFollower;
        this suite is the wiring - that the terminal reads the scroller, asks
        for a pin, and records where the pin landed.

        400 lines into the default 256-entry buffer is the state a Play Mode
        session reaches in seconds with Unity log forwarding on: the log view
        overflows, and every line after the 256th rotates the ring without
        changing the view's child count.

        Every poll waits for a value to hold rather than a frame: the pin is
        written on LateUpdate and the layout that grows the extent runs after
        it, so a scroll settles across frames (see the run-terminal-tests
        skill). The end-of-log poll also requires the last label to carry the
        line the test just logged, so it cannot succeed on the state the
        previous line left behind.
     */
    public sealed class TerminalUILogTailTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const int FrameBudget = 300;
        private const float Tolerance = 0.5f;
        private const int FillLines = 400;
        private const string FillMarker = "of 400";
        private const string WrapMarker = "multiple rendered rows";
        private const string ProbeName = "log-tail-probe";
        private const string ProbeLine = "log tail probe output";

        private static readonly string WrappingLine =
            "a long line that wraps across several rows so the content grows: "
            + "0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZ the quick brown fox jumps over "
            + "the lazy dog and keeps going so the log view has to break it into "
            + WrapMarker;

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private static IEnumerator FillTheLog()
        {
            for (int line = 1; line <= FillLines; ++line)
            {
                Terminal.Log(
                    "fill "
                        + line.ToString(CultureInfo.InvariantCulture)
                        + " of "
                        + FillLines.ToString(CultureInfo.InvariantCulture)
                );
            }

            yield return null;
        }

        private static IEnumerator Settle()
        {
            yield return null;
            yield return null;
        }

        [SetUp]
        public void SetUp()
        {
            DefaultTerminalInput.Instance.CommandText = string.Empty;
        }

        [TearDown]
        public void TearDown()
        {
            DefaultTerminalInput.Instance.CommandText = string.Empty;
            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator AFullBufferStillFollowsNewOutput()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(
                FillMarker,
                "A burst of output leaves the view at its end"
            );

            Terminal.Log("arrived after the buffer was full");
            yield return WaitForLogAtEnd(
                "arrived after the buffer was full",
                "A log that rotates a full buffer is followed"
            );

            /*
                A line taller than the one it replaced grows the extent with no
                child added or removed, which is the whole reason the trigger
                is the buffer version. The extent is compared before and after,
                so the test fails if the line turns out not to wrap.
             */
            float extentBefore = LogScroller().highValue;
            Terminal.Log(WrappingLine);
            yield return WaitForLogAtEnd(
                WrapMarker,
                "A line taller than the one it replaced is followed"
            );
            Assert.That(
                LogScroller().highValue,
                Is.GreaterThan(extentBefore),
                "The wrapping line really did make the log content taller"
            );
        }

        [UnityTest]
        public IEnumerator AScrolledUpViewIsLeftWhereTheDeveloperPutIt()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The fill leaves the view at its end");

            Scroller scroller = LogScroller();
            float parked = (scroller.lowValue + scroller.value) / 2f;
            Assert.That(
                parked,
                Is.GreaterThan(scroller.lowValue),
                "The fill really overflowed the view, so a scroll away has somewhere to go"
            );
            scroller.value = parked;
            yield return WaitForLogValue(parked, "The developer's scroll landed");

            Terminal.Log("arrived while parked");
            yield return Settle();
            Assert.That(
                scroller.value,
                Is.EqualTo(parked).Within(Tolerance),
                "Output that arrives at a scrolled-up view does not yank it to the end"
            );
        }

        [UnityTest]
        public IEnumerator ScrollingBackToTheEndFollowsAgain()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The fill leaves the view at its end");

            Scroller scroller = LogScroller();
            scroller.value = (scroller.lowValue + scroller.value) / 2f;
            yield return null;

            scroller.value = scroller.highValue;
            yield return WaitForLogAtEnd(FillMarker, "Reaching the end re-attaches the tail");

            Terminal.Log("arrived after re-attaching");
            yield return WaitForLogAtEnd(
                "arrived after re-attaching",
                "A re-attached tail follows the next line"
            );
        }

        [UnityTest]
        public IEnumerator RunningACommandReattachesAScrolledUpView()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The fill leaves the view at its end");

            Scroller scroller = LogScroller();
            float parked = (scroller.lowValue + scroller.value) / 2f;
            scroller.value = parked;
            yield return WaitForLogValue(parked, "The developer's scroll landed");

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    ProbeName,
                    _ => Terminal.Log(ProbeLine),
                    minArgs: 0,
                    maxArgs: 0,
                    help: "Log tail probe"
                ),
                "The probe command registers"
            );

            DefaultTerminalInput.Instance.CommandText = ProbeName;
            _terminal.EnterCommand();
            yield return WaitForLogAtEnd(ProbeLine, "The output of the command just run is shown");
        }

        private ScrollView LogView()
        {
            ScrollView logView =
                _terminal._uiDocument.rootVisualElement.Q("LogScrollView") as ScrollView;
            Assert.That(logView, Is.Not.Null, "The log scroll view exists on an open terminal");
            return logView;
        }

        private Scroller LogScroller()
        {
            ScrollView logView = LogView();
            Assert.That(logView.verticalScroller, Is.Not.Null, "It exposes a vertical scroller");
            return logView.verticalScroller;
        }

        private string LastLogText()
        {
            VisualElement content = LogView().contentContainer;
            if (0 == content.childCount)
            {
                return string.Empty;
            }

            return (content[content.childCount - 1] as Label)?.text ?? string.Empty;
        }

        /*
            The poll takes the text the last label must carry as well as the
            scroll position. A position-only poll can succeed on the frame the
            log call was made, before any LateUpdate folded the new line into
            the view, and would then assert against the previous line - so it
            would pass for a view that never followed anything.
         */
        private IEnumerator WaitForLogAtEnd(string expectedLastLine, string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget--)
            {
                Scroller scroller = LogScroller();
                if (
                    0f < scroller.highValue
                    && scroller.highValue - scroller.value < Tolerance
                    && LastLogText().Contains(expectedLastLine, StringComparison.Ordinal)
                )
                {
                    break;
                }

                yield return null;
            }

            Scroller settled = LogScroller();
            Assert.That(
                0f < settled.highValue,
                Is.True,
                "The log content overflows the view so the scroller engages"
            );
            Assert.That(settled.highValue - settled.value, Is.LessThan(Tolerance), message);
            Assert.That(
                LastLogText(),
                Does.Contain(expectedLastLine),
                $"The log line '{expectedLastLine}' is the one in view at the end"
            );
        }

        private IEnumerator WaitForLogValue(float expected, string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && Tolerance < Mathf.Abs(LogScroller().value - expected))
            {
                yield return null;
            }

            Assert.That(LogScroller().value, Is.EqualTo(expected).Within(Tolerance), message);
        }

        private IEnumerator SpawnOpenTerminal()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUILogTail");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("The log tail needs the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);

            /*
                Two frames, not one: SetState flags a command as issued for
                the current frame, and the state frame can land after that
                frame's own LateUpdate, so the pass that clears the flag is
                the frame after the one that set it.
             */
            yield return null;
            yield return null;

            int frameBudget = 600;
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
        }

#if UNITY_EDITOR
        private static T LoadAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.That(asset != null, $"Expected the test asset at {PackageRoot}/{relativePath}");
            return asset;
        }
#endif
    }
}
