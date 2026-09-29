namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
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

        Every poll waits for the view to hold a position rather than a frame:
        the pin is written on LateUpdate and the layout that grows the extent
        runs after it, so a scroll settles across frames (see the
        run-terminal-tests skill).
     */
    public sealed class TerminalUILogTailTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const int FrameBudget = 300;
        private const float Tolerance = 0.5f;
        private const int FillLines = 400;

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

            Terminal.Log("arrived after the buffer was full");
            yield return WaitForLogAtEnd("A log that rotates a full buffer is followed");
        }

        [UnityTest]
        public IEnumerator AScrolledUpViewIsLeftWhereTheDeveloperPutIt()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();

            Scroller scroller = LogScroller();
            float parked = (scroller.lowValue + scroller.value) / 2f;
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

            Scroller scroller = LogScroller();
            scroller.value = (scroller.lowValue + scroller.value) / 2f;
            yield return null;

            scroller.value = scroller.highValue;
            yield return WaitForLogAtEnd("Reaching the end re-attaches the tail");

            Terminal.Log("arrived after re-attaching");
            yield return WaitForLogAtEnd("A re-attached tail follows the next line");
        }

        [UnityTest]
        public IEnumerator RunningACommandReattachesAScrolledUpView()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();

            Scroller scroller = LogScroller();
            float parked = (scroller.lowValue + scroller.value) / 2f;
            scroller.value = parked;
            yield return WaitForLogValue(parked, "The developer's scroll landed");

            DefaultTerminalInput.Instance.CommandText = "help";
            _terminal.EnterCommand();
            yield return WaitForLogAtEnd("The output of the command just run is shown");
        }

        private Scroller LogScroller()
        {
            ScrollView logView =
                _terminal._uiDocument.rootVisualElement.Q("LogScrollView") as ScrollView;
            Assert.That(logView, Is.Not.Null, "The log scroll view exists on an open terminal");
            Assert.That(logView.verticalScroller, Is.Not.Null, "It exposes a vertical scroller");
            return logView.verticalScroller;
        }

        private IEnumerator WaitForLogAtEnd(string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget--)
            {
                Scroller scroller = LogScroller();
                if (0f < scroller.highValue && scroller.highValue - Tolerance <= scroller.value)
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

            /* SetState flags a command as issued for the current frame. */
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
