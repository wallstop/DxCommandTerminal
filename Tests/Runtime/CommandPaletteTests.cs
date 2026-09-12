namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using Components;
    using Input;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public sealed class CommandPaletteTests
    {
        private const int FrameBudget = 60;

        private CommandPaletteUI _palette;
        private GameObject _paletteObject;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private static void RegisterPair()
        {
            Terminal.Shell.AddCommand("paletteping1", _ => { }, help: "first");
            Terminal.Shell.AddCommand("paletteping2", _ => { }, help: "second");
        }

        private static void AssertResolved(string key, string expectedName, bool shift, bool ctrl)
        {
            InputHelpers.CachedKeyName resolved = InputHelpers.ResolveKeyName(key);
            Assert.AreEqual(expectedName, resolved.Name, $"Resolved key name for '{key}'");
            Assert.AreEqual(shift, resolved.ShiftRequired, $"Shift requirement for '{key}'");
            Assert.AreEqual(ctrl, resolved.CtrlRequired, $"Ctrl requirement for '{key}'");
        }

        [SetUp]
        public void SetUp()
        {
            Terminal.Buffer = new CommandLog(64, null);
            Terminal.History = new CommandHistory(64);
            Terminal.Shell = new CommandShell(Terminal.History);
            Terminal.AutoComplete = new CommandAutoComplete(Terminal.History, Terminal.Shell);
        }

        [TearDown]
        public void TearDown()
        {
            if (_paletteObject != null)
            {
                UnityEngine.Object.Destroy(_paletteObject);
            }

            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }

        [Test]
        public void HotkeyParsingResolvesModifiersAndNames()
        {
            AssertResolved("space", "space", shift: false, ctrl: false);
            AssertResolved("ctrl+space", "space", shift: false, ctrl: true);
            AssertResolved("control+space", "space", shift: false, ctrl: true);
            AssertResolved("CTRL+Space", "Space", shift: false, ctrl: true);
            AssertResolved("#space", "space", shift: true, ctrl: false);
            AssertResolved("shift+space", "space", shift: true, ctrl: false);
            AssertResolved("ctrl+shift+a", "a", shift: true, ctrl: true);
            AssertResolved("shift+ctrl+a", "a", shift: true, ctrl: true);
            AssertResolved("a", "a", shift: false, ctrl: false);
            AssertResolved("#a", "a", shift: true, ctrl: false);
            AssertResolved("escape", "escape", shift: false, ctrl: false);
            AssertResolved("ctrl+", "ctrl+", shift: false, ctrl: false);
            AssertResolved(string.Empty, string.Empty, shift: false, ctrl: false);
        }

        [Test]
        public void IsKeyPressedRejectsInvalidInputWithoutThrowing()
        {
            Assert.IsFalse(InputHelpers.IsKeyPressed(null, InputMode.LegacyInputSystem));
            Assert.IsFalse(InputHelpers.IsKeyPressed(string.Empty, InputMode.LegacyInputSystem));
#pragma warning disable CS0612 // Type or member is obsolete
            Assert.IsFalse(InputHelpers.IsKeyPressed("space", InputMode.None));
#pragma warning restore CS0612 // Type or member is obsolete
        }

        [Test]
        public void RankTiersMatchExactPrefixThenSubsequence()
        {
            Assert.AreEqual(
                CommandPaletteSearch.ExactMatch,
                CommandPaletteSearch.Rank("alpha", "alpha")
            );
            Assert.AreEqual(
                CommandPaletteSearch.PrefixMatch,
                CommandPaletteSearch.Rank("alpha", "alphabet")
            );
            Assert.AreEqual(
                CommandPaletteSearch.SubsequenceMatch,
                CommandPaletteSearch.Rank("alpha", "zalphabet")
            );
            Assert.AreEqual(
                CommandPaletteSearch.NoMatch,
                CommandPaletteSearch.Rank("alpha", "beta")
            );
            Assert.AreEqual(
                CommandPaletteSearch.NoMatch,
                CommandPaletteSearch.Rank(string.Empty, "beta")
            );
        }

        [Test]
        public void RankMatchingIsCaseInsensitive()
        {
            Assert.AreEqual(
                CommandPaletteSearch.ExactMatch,
                CommandPaletteSearch.Rank("ALPHA", "alpha")
            );
            Assert.AreEqual(
                CommandPaletteSearch.PrefixMatch,
                CommandPaletteSearch.Rank("Alpha", "ALPHABET")
            );
            Assert.AreEqual(
                CommandPaletteSearch.PrefixMatch,
                CommandPaletteSearch.Rank("ZALPHA", "zalphabet")
            );
            Assert.AreEqual(
                CommandPaletteSearch.SubsequenceMatch,
                CommandPaletteSearch.Rank("LPHAB", "zalphabet")
            );
        }

        [Test]
        public void FilterGroupsMatchesByTierPreservingSourceOrderWithinTier()
        {
            List<string> source = new() { "zalphabet", "beta", "alphabet", "alpha" };
            List<string> results = new();

            CommandPaletteSearch.Filter("alpha", source, results);

            Assert.AreEqual(
                new[] { "alpha", "alphabet", "zalphabet" },
                results.ToArray(),
                "Exact matches come first, then prefixes, then subsequence matches"
            );
        }

        [Test]
        public void FilterEmptyQueryReturnsSourceOrder()
        {
            List<string> source = new() { "zeta", "alpha", "beta" };
            List<string> results = new();

            CommandPaletteSearch.Filter(string.Empty, source, results);

            Assert.AreEqual(source.ToArray(), results.ToArray());
        }

        [UnityTest]
        public IEnumerator OpenShowsCommandsAndFocusesInput()
        {
            yield return SpawnPalette();
            Terminal.Shell.AddCommand("paletteping", _ => { }, help: "test command");

            int openedCount = 0;
            _palette.Opened += () => ++openedCount;
            _palette.Open();
            yield return null;

            Assert.IsTrue(_palette.IsOpen, "Open() must open the palette");
            Assert.AreEqual(1, openedCount, "Opened must fire exactly once per open");
            Assert.AreEqual(
                new[] { "paletteping" },
                _palette._matchNames.ToArray(),
                "A fresh deferred shell lists only its manually registered commands"
            );
            yield return WaitForFocusedInput("The palette input should be focused after open");
        }

        [UnityTest]
        public IEnumerator BlankInputListsCommandsInOrdinalOrder()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;

            Assert.AreEqual(
                new[] { "paletteping1", "paletteping2" },
                _palette._matchNames.ToArray(),
                "Blank input lists every command in the shell's ordinal order"
            );
            Assert.IsTrue(
                _palette.TryGetSelected(out string selected),
                "A non-empty match list always has a selection"
            );
            Assert.AreEqual("paletteping1", selected, "Selection starts on the first row");
        }

        [UnityTest]
        public IEnumerator TypedQueryRanksPrefixesOverSubsequences()
        {
            yield return SpawnPalette();
            Terminal.Shell.AddCommand("alphabet", _ => { }, help: "prefix match");
            Terminal.Shell.AddCommand("zalphabet", _ => { }, help: "subsequence match");

            _palette.Open();
            yield return null;
            _palette._input.value = "alpha";
            yield return null;

            Assert.AreEqual(
                new[] { "alphabet", "zalphabet" },
                _palette._matchNames.ToArray(),
                "Prefix matches outrank subsequence matches"
            );
            Assert.IsTrue(_palette.TryGetSelected(out string selected));
            Assert.AreEqual("alphabet", selected, "Selection resets to the first match");
        }

        [UnityTest]
        public IEnumerator NavigateMovesSelectionWithinBounds()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;

            Assert.IsTrue(_palette.MoveSelection(1), "Moving down from the first row succeeds");
            Assert.IsTrue(
                _palette.TryGetSelected(out string down),
                "Selection survives navigation"
            );
            Assert.AreEqual("paletteping2", down, "One down move lands on the second row");
            Assert.IsFalse(_palette.MoveSelection(1), "Moving past the last row is rejected");
            Assert.IsTrue(_palette.MoveSelection(-1), "Moving up from the second row succeeds");
            Assert.IsTrue(_palette.TryGetSelected(out string up), "Selection survives navigation");
            Assert.AreEqual("paletteping1", up);
            Assert.IsFalse(_palette.MoveSelection(-1), "Moving past the first row is rejected");
        }

        [UnityTest]
        public IEnumerator SubmitRunsCommandTextAndCloses()
        {
            yield return SpawnPalette();
            int executed = 0;
            Terminal.Shell.AddCommand("paletteping", _ => ++executed, help: "test command");

            _palette.Open();
            yield return null;
            _palette._input.value = "paletteping";
            yield return null;

            Assert.IsTrue(_palette.Submit(), "A valid command submits successfully");
            Assert.AreEqual(1, executed, "The command handler must run exactly once");
            Assert.IsFalse(_palette.IsOpen, "A successful submission closes the palette");
        }

        [UnityTest]
        public IEnumerator EnterOnBlankInputRunsSelectedRow()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;
            Assert.IsTrue(_palette.MoveSelection(1), "Navigate to the second row");
            Assert.IsTrue(_palette.TryGetSelected(out string expected));
            Assert.AreEqual("paletteping2", expected);

            yield return SendKeyDown(KeyCode.Return);

            Assert.IsFalse(_palette.IsOpen, "Enter executes the selection and closes");
            string[] history = CollectHistory();
            Assert.IsTrue(0 < history.Length, "The executed command must be recorded in history");
            Assert.AreEqual("paletteping2", history[^1], "The selected row must execute");
        }

        [UnityTest]
        public IEnumerator FailedSubmissionKeepsPaletteOpenWithFeedback()
        {
            yield return SpawnPalette();

            _palette.Open();
            yield return null;
            _palette._input.value = "palettenosuchcommand";
            yield return null;

            Assert.IsFalse(_palette.Submit(), "An unknown command fails to submit");
            Assert.IsTrue(_palette.IsOpen, "A failed submission keeps the palette open");
            StringAssert.StartsWith(
                "Error:",
                _palette._feedback.text,
                "The failure message must be visible as feedback"
            );
        }

        [UnityTest]
        public IEnumerator EscapeClosesAndRestoresPreviousFocus()
        {
            yield return SpawnPalette();

            TextField probe = new() { name = "FocusProbe" };
            _palette._uiDocument.rootVisualElement.Add(probe);
            probe.Focus();

            _palette.Open();
            yield return WaitForFocusedInput("The palette input takes focus on open");

            yield return SendKeyDown(KeyCode.Escape);

            Assert.IsFalse(_palette.IsOpen, "Escape closes the palette");
            yield return WaitForFocused(probe, "The previously focused element regains focus");
        }

        [UnityTest]
        public IEnumerator TabAppliesSelectedNameToInput()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;
            Assert.IsTrue(_palette.MoveSelection(1), "Navigate to the second row");

            yield return SendKeyDown(KeyCode.Tab);

            Assert.AreEqual(
                "paletteping2",
                _palette._input.value,
                "Tab applies the selected command name to the input"
            );
            Assert.IsTrue(_palette.TryGetSelected(out string selected), "Selection stays valid");
            Assert.AreEqual("paletteping2", selected, "Tab keeps the applied row selected");
        }

        [UnityTest]
        public IEnumerator ToggleFlipsOpenStateAndFiresEvents()
        {
            yield return SpawnPalette();

            int openedCount = 0;
            int closedCount = 0;
            _palette.Opened += () => ++openedCount;
            _palette.Closed += () => ++closedCount;

            _palette.Toggle();
            Assert.IsTrue(_palette.IsOpen, "The first toggle opens the palette");
            Assert.AreEqual(1, openedCount, "Opened fires once per open");
            Assert.AreEqual(0, closedCount, "Closed does not fire on open");

            _palette.Toggle();
            Assert.IsFalse(_palette.IsOpen, "The second toggle closes the palette");
            Assert.AreEqual(1, closedCount, "Closed fires once per close");

            _palette.Toggle();
            Assert.IsTrue(_palette.IsOpen, "The third toggle reopens the palette");
        }

        [UnityTest]
        public IEnumerator OpeningPaletteClosesOpenTerminal()
        {
            yield return SpawnPalette();
            yield return SpawnTerminal();
            TerminalUI.Instance.SetState(TerminalState.OpenSmall);
            Assert.IsFalse(
                TerminalUI.Instance.IsClosed,
                "Sanity: the terminal is open before the palette opens"
            );

            _palette.Open();
            yield return null;

            Assert.IsTrue(_palette.IsOpen, "The palette is open");
            Assert.IsTrue(TerminalUI.Instance.IsClosed, "Opening the palette closes the terminal");
        }

        [UnityTest]
        public IEnumerator OpeningTerminalClosesOpenPalette()
        {
            yield return SpawnPalette();
            _palette.Open();
            yield return null;
            Assert.IsTrue(_palette.IsOpen, "The palette is open");

            yield return SpawnTerminal();
            TerminalUI.Instance.SetState(TerminalState.OpenSmall);
            yield return null;

            Assert.IsFalse(_palette.IsOpen, "Opening the terminal closes the palette");
            Assert.IsFalse(
                TerminalUI.Instance.IsClosed,
                "The terminal stays open after taking over"
            );
        }

        private IEnumerator SpawnPalette()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _paletteObject = new GameObject("CommandPalette");
            _paletteObject.SetActive(false);
            UIDocument document = _paletteObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _palette = _paletteObject.AddComponent<CommandPaletteUI>();
            _palette._uiDocument = document;
            _paletteObject.SetActive(true);
            yield return null;
        }

        private IEnumerator SpawnTerminal()
        {
            LogAssert.Expect(LogType.Error, "No UIDocument assigned, cannot setup UI.");
            _terminalObject = new GameObject("Terminal", typeof(StartTracker), typeof(TerminalUI));
            _terminalObject.SetActive(true);
            StartTracker tracker = _terminalObject.GetComponent<StartTracker>();
            yield return new WaitUntil(() => tracker.Started);
        }

        private string[] CollectHistory()
        {
            List<string> entries = new();
            foreach (
                string entry in Terminal.History.GetHistory(onlySuccess: true, onlyErrorFree: true)
            )
            {
                entries.Add(entry);
            }

            return entries.ToArray();
        }

        private IEnumerator WaitForFocusedInput(string message)
        {
            /*
                Depending on panel state the focus controller reports either the
                TextField or its inner text-input element; both mean the input
                owns focus.
             */
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !InputOwnsFocus())
            {
                yield return null;
            }

            Assert.IsTrue(InputOwnsFocus(), message);
        }

        private bool InputOwnsFocus()
        {
            VisualElement focused =
                _palette._uiDocument.rootVisualElement.focusController.focusedElement
                as VisualElement;
            return focused == _palette._input || _palette._input.Contains(focused);
        }

        private IEnumerator WaitForFocused(VisualElement expected, string message)
        {
            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && !ReferenceEquals(
                    _palette._uiDocument.rootVisualElement.focusController.focusedElement,
                    expected
                )
            )
            {
                yield return null;
            }

            Assert.AreEqual(
                expected,
                _palette._uiDocument.rootVisualElement.focusController.focusedElement,
                message
            );
        }

        /*
            Sends a synthetic key press through the palette root so the
            TrickleDown keyboard handler runs exactly as it does for real
            input, independent of the active input backend.
         */
        private IEnumerator SendKeyDown(KeyCode keyCode)
        {
            using (
                KeyDownEvent keyDown = KeyDownEvent.GetPooled('\0', keyCode, EventModifiers.None)
            )
            {
                _palette._paletteRoot.SendEvent(keyDown);
            }

            yield return null;
        }
    }
}
