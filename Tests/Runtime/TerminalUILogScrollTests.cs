namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
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
        The log can be moved with the keyboard, which is the only way a
        developer can get to an error in a full 256-entry buffer.

        The command line holds panel focus for as long as the console is open,
        so these keys arrive at the command field and the terminal routes them
        to the log. LogScrollKeysTests owns the decision; this suite is the
        wiring, and only a live panel can answer it: that the key travels,
        that the scroller moves by a page, and - the part that matters most -
        that a developer who has paged back is not yanked to the newest line by
        the output that arrives next.

        Keys are injected at the document root, which is what makes the event
        travel down to the field the way a real keystroke does. A panel routes
        a key by focus, and this synthetic panel has none, so an event aimed at
        the field itself is dropped.

        Every poll waits for a value to hold rather than a frame: the write
        lands during event dispatch and the layout that grows the extent runs
        after it, so a scroll settles across frames.
     */
    public sealed class TerminalUILogScrollTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const int FrameBudget = 300;
        private const int StableLayoutFrames = 5;
        private const float Tolerance = 0.5f;
        private const int FillLines = 400;
        private const string FillMarker = "of 400";
        private const string LateLine = "logged after the developer paged back";

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private static T LoadAsset<T>(string relativePath)
            where T : ScriptableObject
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PageUpMovesTheLogBackOneViewport()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The log follows its own output to the end");

            float atEnd = LogScroller().value;
            yield return SendKey(KeyCode.PageUp);

            yield return WaitForLogValue(
                expected: atEnd - LogView().contentViewport.layout.height,
                message: "Page Up moves the log back by what one viewport shows"
            );
            Assert.That(
                LogScroller().value,
                Is.LessThan(atEnd),
                "A key the log answered moved it, so the route is not just a consumed key"
            );
        }

        [UnityTest]
        public IEnumerator PageDownReturnsToWherePagingStarted()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The log follows its own output to the end");

            float atEnd = LogScroller().value;
            yield return SendKey(KeyCode.PageUp);
            yield return WaitForLogValue(
                atEnd - LogView().contentViewport.layout.height,
                "The first page lands before the second is measured against it"
            );

            float paged = LogScroller().value;
            yield return SendKey(KeyCode.PageDown);
            yield return WaitForLogValue(
                paged + LogView().contentViewport.layout.height,
                "Page Down moves forward by the same viewport, so the two are inverses"
            );
        }

        [UnityTest]
        public IEnumerator OutputAfterPagingBackDoesNotYankTheLogToTheEnd()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The log follows its own output to the end");

            yield return SendKey(KeyCode.PageUp);
            float paged = LogScroller().value;
            yield return WaitForLogValue(
                paged,
                "The page is where the developer put it before anything else happens"
            );

            Terminal.Log(LateLine);
            yield return null;
            yield return null;

            Assert.That(
                LogScroller().value,
                Is.EqualTo(paged).Within(Tolerance),
                "A developer reading an earlier line is not dragged to the newest one"
            );
        }

        [UnityTest]
        public IEnumerator CommandEndReturnsToTheTailAndFollowsAgain()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The log follows its own output to the end");

            yield return SendKey(KeyCode.PageUp);
            Terminal.Log(LateLine);
            yield return null;

            yield return SendKey(KeyCode.End, EventModifiers.Command);

            yield return WaitForLogAtEnd(
                LateLine,
                "Command+End is the way back to the tail, and the line logged while parked is the one it lands on"
            );
        }

        [UnityTest]
        public IEnumerator CommandHomeJumpsToTheOldestBufferedLine()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The log follows its own output to the end");

            yield return SendKey(KeyCode.Home, EventModifiers.Command);

            yield return WaitForLogValue(
                expected: 0f,
                message: "Command+Home reaches the oldest line the buffer still holds"
            );
        }

        [UnityTest]
        public IEnumerator AKeyTheFieldWantsIsLeftAlone()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The log follows its own output to the end");

            yield return SendKey(KeyCode.PageUp);
            float paged = LogScroller().value;
            yield return WaitForLogValue(paged, "The log is parked where the developer left it");

            yield return SendKey(KeyCode.DownArrow);

            Assert.That(
                LogScroller().value,
                Is.EqualTo(paged).Within(Tolerance),
                "History recall belongs to the command line, so the log does not answer it"
            );
        }

        [UnityTest]
        public IEnumerator PlainHomeStillBelongsToTheCommandLine()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForLogAtEnd(FillMarker, "The log follows its own output to the end");

            yield return SendKey(KeyCode.PageUp);
            float paged = LogScroller().value;
            yield return WaitForLogValue(paged, "The log is parked where the developer left it");

            yield return SendKey(KeyCode.Home);

            Assert.That(
                LogScroller().value,
                Is.EqualTo(paged).Within(Tolerance),
                "A bare Home moves the caret in a one-line field, which is what a developer editing a command expects"
            );
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

        private IEnumerator FillTheLog()
        {
            for (int line = 1; line <= FillLines; ++line)
            {
                Terminal.Log($"fill line {line} {FillMarker}");
            }

            yield return null;

            /*
                A scroller with no high value is a log view the panel never laid
                out, which is what a headless editor with no rendered view
                gives: the content exists, the geometry does not, and every
                assertion below would read a zero the environment produced.
                That is a host limit, not a defect, so it is reported as one -
                skipping with the reason - rather than as seven red tests that
                say nothing about the change. See #193 for the wider gap this
                host has with UI coverage.

                Held, not merely seen. A panel left half-built by an earlier
                test can report a scroller range for a frame or two and lose it
                again, and a test that starts on that flicker and asserts into
                a geometry that has gone is a flaky red that says the change
                is broken. A layout that survives several consecutive frames is
                one this environment can actually answer; one that does not is
                a host limit, and the honest report is the same either way.
             */
            int stableFrames = 0;
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && stableFrames < StableLayoutFrames)
            {
                ScrollView view = LogView();
                bool laidOut =
                    0f < LogScroller().highValue && 0f < view.contentViewport.layout.height;
                stableFrames = laidOut ? stableFrames + 1 : 0;
                yield return null;
            }

            if (stableFrames < StableLayoutFrames)
            {
                Assert.Ignore(
                    "The log view did not hold a layout in this environment, so its scroller "
                        + "has nothing to scroll. A headless editor with no rendered view cannot "
                        + "answer this suite; run it where the Game view renders."
                );
            }
        }

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
            Assert.That(0f < settled.highValue, Is.True, "The log content overflows the view");
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

        private string LastLogText()
        {
            VisualElement content = LogView().contentContainer;
            if (0 == content.childCount)
            {
                return string.Empty;
            }

            return (content[content.childCount - 1] as Label)?.text ?? string.Empty;
        }

        private IEnumerator SendKey(KeyCode keyCode, EventModifiers modifiers = EventModifiers.None)
        {
            using (KeyDownEvent key = KeyDownEvent.GetPooled('\0', keyCode, modifiers))
            {
                _terminal._uiDocument.rootVisualElement.SendEvent(key);
            }

            yield return null;
        }

        private IEnumerator SpawnOpenTerminal()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUILogScroll");
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
            Assert.Ignore("Log scrolling needs the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);

            /* Two frames, not one: SetState flags a command as issued for the
               frame it runs on, and the frame that clears the flag is the frame
               after the one that set it. */
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
                _terminal._commandInput,
                Is.Not.Null,
                "The terminal input field should exist after the terminal opens"
            );

            DefaultTerminalInput.Instance.CommandText = string.Empty;
        }
    }
}
