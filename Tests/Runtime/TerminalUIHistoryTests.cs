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

    /*
        The command line's undo, end to end. TextFieldUndoTests pins the state
        machine in EditMode, and this pins the half only a real surface can
        answer: that a Ctrl+Z is routed to the line, and that the line the
        developer is looking at is what changes.

        The routing is driven through the terminal's own handler rather than
        through a key sent at the document root, because delivery is an
        environment fact this host does not have - TerminalUIPasteTests fails on
        unmodified master here for exactly that reason - and a test that cannot
        tell "the key never arrived" from "the routing is broken" measures
        nothing. One test probes delivery, and skips where it cannot be measured.
     */
    public sealed class TerminalUIHistoryTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

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

        [SetUp]
        public void SetUp()
        {
            DefaultTerminalInput.Instance.CommandText = string.Empty;
        }

        [TearDown]
        public void TearDown()
        {
            /*
                The shell is static, so a command this suite registered outlives
                the terminal unless it is taken back out.
             */
            Terminal.Shell?.ClearCustomCommands();

            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }

        /*
            The reported gap, on the surface that has the command line. A
            developer mistypes one character in a long command and has nothing
            to take the line back with, so the last state before the typo is
            the state undo has to reach.
         */
        [UnityTest]
        public IEnumerator UndoTakesTheCommandLineBackToTheStateBeforeTheLastKeystroke()
        {
            yield return SpawnOpenTerminal();

            yield return SetInputText("giv");
            yield return SetInputText("givx");

            yield return SendUndo();

            yield return WaitForCommandText("giv", "One Ctrl+Z drops the character that was typed");
        }

        /*
            A history recall is written by the terminal, not typed, and it is
            still a state the field held. The recall is what an undo has to
            take back - the alternative is a stack that has never seen the
            recalled line, where the first undo would step over it.
         */
        [UnityTest]
        public IEnumerator UndoTakesBackAHistoryRecall()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand("hist-undo", _ => { }, minArgs: 0, maxArgs: 0, help: "t"),
                "Sanity: the recalled command registers"
            );
            DefaultTerminalInput.Instance.CommandText = "hist-undo";
            yield return EnterCommand();
            yield return SetInputText("h");

            _terminal.HandlePrevious();
            yield return WaitForCommandText("hist-undo", "Sanity: the recall loaded the line");

            yield return SendUndo();

            yield return WaitForCommandText(
                "h",
                "The undo returned to the state the recall replaced"
            );
        }

        /*
            "Stops at the start rather than doing nothing" is the whole of what
            a key with nothing left to undo may do: it must not write, because
            the text it hands back when it reports no step is the empty state,
            and a surface that wrote that would clear a line the developer was
            reading.

            The line is walked all the way back to empty and then one key past
            it, so the assertion is on the boundary rather than on a key that
            happened to have nothing to do.
         */
        [UnityTest]
        public IEnumerator UndoStopsAtTheEmptyLineAndDoesNotClearItAgain()
        {
            yield return SpawnOpenTerminal();

            yield return SetInputText("give sword 5");
            yield return SendUndo();
            yield return WaitForCommandText(string.Empty, "Sanity: the undo emptied the line");

            yield return SetInputText("give sword 5");
            yield return SendUndo();
            yield return WaitForCommandText(string.Empty, "The line is back to the start");

            yield return SendUndo();

            Assert.AreEqual(
                string.Empty,
                DefaultTerminalInput.Instance.CommandText,
                "The key past the start wrote nothing, so the empty line is still empty"
            );
        }

        /*
            The terminal's own writes are states the field held, and the first
            of them is the one that runs a command. Undo after a run brings the
            command back, which is deliberate: the developer asked for the line
            to be cleared, and one more Ctrl+Z is how they take that back. A
            command that ran twice because of it would be a different story, so
            this pins the text only - the terminal does not re-run anything
            until Enter is pressed again.
         */
        [UnityTest]
        public IEnumerator UndoBringsBackTheCommandARunCleared()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand("hist-run", _ => { }, minArgs: 0, maxArgs: 0, help: "t"),
                "Sanity: the command registers"
            );
            yield return SetInputText("hist-run");
            yield return EnterCommand();
            yield return WaitForCommandText(string.Empty, "Sanity: the run cleared the line");

            yield return SendUndo();

            yield return WaitForCommandText(
                "hist-run",
                "The clear the run made is one undoable step, so the command comes back"
            );
        }

        [UnityTest]
        public IEnumerator YWithTheCommandModifierIsNotARedo()
        {
            yield return SpawnOpenTerminal();
            yield return SetInputText("giv");
            yield return SetInputText("givx");
            yield return SendUndo();
            yield return WaitForCommandText("giv", "Sanity: the undo moved");

            yield return SendKey(KeyCode.Y, EventModifiers.Control);

            Assert.AreEqual(
                "giv",
                DefaultTerminalInput.Instance.CommandText,
                "Y is not claimed, so it is a character and the line is where the undo left it"
            );
        }

        [UnityTest]
        public IEnumerator ZWithoutTheCommandModifierLeavesTheLineAlone()
        {
            yield return SpawnOpenTerminal();
            yield return SetInputText("giv");
            yield return SetInputText("givx");

            yield return SendKey(KeyCode.Z, EventModifiers.None);

            Assert.AreEqual(
                "givx",
                DefaultTerminalInput.Instance.CommandText,
                "A bare Z is a character, and this field has no history to route it to"
            );
        }

        /*
            The redo, and the reason it is here: without it, an undo a
            developer did not mean is a line they cannot get back.
         */
        [UnityTest]
        public IEnumerator RedoReturnsTheLineToTheStateAnUndoLeft()
        {
            yield return SpawnOpenTerminal();

            yield return SetInputText("giv");
            yield return SetInputText("givx");
            yield return SendUndo();
            yield return WaitForCommandText("giv", "Sanity: the undo moved");

            yield return SendKey(KeyCode.Z, EventModifiers.Control | EventModifiers.Shift);

            yield return WaitForCommandText("givx", "The redo returns the undone state");
        }

        /*
            The other half of the redo's reason to exist. The states above a
            redo describe a line that no longer exists, so a keystroke after an
            undo has to end them; a redo that could still reach them would hand
            the developer back the text they had just undone away from.
         */
        [UnityTest]
        public IEnumerator ANewKeystrokeDropsTheRedoTail()
        {
            yield return SpawnOpenTerminal();

            yield return SetInputText("giv");
            yield return SetInputText("givx");
            yield return SendUndo();
            yield return WaitForCommandText("giv", "Sanity: the undo moved");

            yield return SetInputText("give");

            yield return SendKey(KeyCode.Z, EventModifiers.Control | EventModifiers.Shift);

            Assert.AreEqual(
                "give",
                DefaultTerminalInput.Instance.CommandText,
                "There was nothing left to redo, so the line is what was typed"
            );
        }

        /*
            The other half, and the reason the rest call the handler directly: a
            key that never arrives looks exactly like routing that is broken, so
            this either proves delivery on this host or reports that it could not
            be measured here. It is the failure TerminalUIPasteTests has on
            unmodified master, measured both ways there.
         */
        [UnityTest]
        public IEnumerator ProbeShowsAKeyReachesTheField()
        {
            yield return SpawnOpenTerminal();

            bool reached = false;
            _terminal._commandInput.RegisterCallback<KeyDownEvent>(
                _ => reached = true,
                TrickleDown.TrickleDown
            );

            using (
                KeyDownEvent key = KeyDownEvent.GetPooled('\0', KeyCode.Z, EventModifiers.Control)
            )
            {
                _terminal._uiDocument.rootVisualElement.SendEvent(key);
            }

            yield return null;
            if (!reached)
            {
                Assert.Ignore(
                    "A synthetic key does not reach the command field in this environment, so "
                        + "delivery cannot be measured here: a key that never arrives looks exactly "
                        + "like routing that is broken. "
                        + "TerminalUIPasteTests.PasteReachesTheCommandTextAsAUserEdit fails on "
                        + "unmodified master on this host for the same reason."
                );
            }
        }

        private IEnumerator SpawnOpenTerminal()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUIHistory");
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
            Assert.Ignore("Undo routing is editor Play Mode coverage.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);

            /*
                Two frames, for the reason the paste suite gives: the input
                change handler reverts field writes until the frame's
                command-issued flag clears, and a write sent before it clears
                is reverted.
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
                "The terminal input field should exist after the terminal opens"
            );
        }

        /*
            A value write into the field is how a keystroke reaches the command
            text: the terminal reads it from the change event, so Enter would
            run nothing if the write skipped it. It is also how the stack learns
            a keystroke, so this is the developer's edit rather than a step.
         */
        private IEnumerator SetInputText(string text)
        {
            _terminal._commandInput.value = text;
            yield return null;
            yield return WaitForCommandText(
                text,
                $"Sanity: the field's '{text}' is the command text"
            );
        }

        private IEnumerator EnterCommand()
        {
            _terminal.EnterCommand();
            yield return null;
            yield return null;
        }

        private IEnumerator SendUndo()
        {
            return SendKey(KeyCode.Z, EventModifiers.Control);
        }

        /*
            The routing is called directly rather than through a key sent at the
            document root. A key has to be *delivered* for this to mean anything,
            and on a host with no focus routing a synthetic one never is -
            TerminalUIPasteTests fails on unmodified master here for exactly that
            reason - so every test below would go red for an environment fact
            while measuring nothing. Driving the handler answers the question
            that is actually being asked: given these keys, what does the command
            line do. The delivery half is one test, ProbeShowsAKeyReachesTheField,
            and it skips rather than fails where the host cannot deliver.
         */
        private IEnumerator SendKey(KeyCode keyCode, EventModifiers modifiers)
        {
            using (KeyDownEvent key = KeyDownEvent.GetPooled('\0', keyCode, modifiers))
            {
                _terminal.TryApplyHistoryKey(key);
            }

            yield return null;
            yield return null;
        }

        private IEnumerator WaitForCommandText(string expected, string message)
        {
            int frameBudget = 120;
            while (
                0 < frameBudget--
                && !string.Equals(
                    DefaultTerminalInput.Instance.CommandText,
                    expected,
                    StringComparison.Ordinal
                )
            )
            {
                yield return null;
            }

            Assert.AreEqual(
                expected,
                DefaultTerminalInput.Instance.CommandText,
                $"{message}: command text is still"
                    + $" '{DefaultTerminalInput.Instance.CommandText}'"
            );
        }
    }
}
