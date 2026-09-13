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
        public IEnumerator OpenShowsBarOnlyAndFocusesInput()
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
                0,
                _palette._matchNames.Count,
                "A blank query keeps the bar collapsed with no listed commands"
            );
            AssertResultsCollapsed("Opening without a query shows only the bar");
            yield return WaitForFocusedInput("The palette input should be focused after open");
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
            AssertResultsExpanded("Typing reveals the results dropdown");
        }

        [UnityTest]
        public IEnumerator TypingRevealsResultsAndClearingCollapses()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;
            AssertResultsCollapsed("A blank bar starts collapsed");

            _palette._input.value = "paletteping";
            yield return null;

            AssertResultsExpanded("Typing reveals the results dropdown");
            Assert.AreEqual(
                new[] { "paletteping1", "paletteping2" },
                _palette._matchNames.ToArray(),
                "Matches list in the shell's ordinal order"
            );

            _palette._input.value = string.Empty;
            yield return null;

            Assert.AreEqual(0, _palette._matchNames.Count, "Clearing the input clears the matches");
            AssertResultsCollapsed("Clearing the input collapses back to the bar");
        }

        [UnityTest]
        public IEnumerator NavigateAutoLoadsSelectionIntoInput()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;
            _palette._input.value = "paletteping";
            yield return null;

            Assert.AreEqual(
                new[] { "paletteping1", "paletteping2" },
                _palette._matchNames.ToArray(),
                "Both commands match the typed prefix in ordinal order"
            );

            Assert.IsTrue(_palette.MoveSelection(1), "Moving down from the first row succeeds");
            Assert.IsTrue(
                _palette.TryGetSelected(out string down),
                "Selection survives navigation"
            );
            Assert.AreEqual("paletteping2", down, "One down move lands on the second row");
            Assert.AreEqual(
                "paletteping2",
                _palette._input.value,
                "Arrow navigation auto-loads the selected command name into the input"
            );
            Assert.AreEqual(
                new[] { "paletteping1", "paletteping2" },
                _palette._matchNames.ToArray(),
                "Auto-loading keeps the match list so the typed query still drives the filter"
            );

            Assert.IsFalse(_palette.MoveSelection(1), "Moving past the last row is rejected");
            Assert.AreEqual(
                "paletteping2",
                _palette._input.value,
                "A rejected move keeps the loaded name"
            );

            Assert.IsTrue(_palette.MoveSelection(-1), "Moving up from the second row succeeds");
            Assert.IsTrue(_palette.TryGetSelected(out string up), "Selection survives navigation");
            Assert.AreEqual("paletteping1", up);
            Assert.AreEqual("paletteping1", _palette._input.value, "Moving up auto-loads again");
            Assert.IsFalse(_palette.MoveSelection(-1), "Moving past the first row is rejected");
            Assert.AreEqual(
                "paletteping1".Length,
                _palette._input.cursorIndex,
                "Auto-loading parks the caret at the end of the loaded name"
            );
            Assert.AreEqual(
                _palette._input.cursorIndex,
                _palette._input.selectIndex,
                "Auto-loading collapses the selection"
            );
        }

        [UnityTest]
        public IEnumerator EnterAfterAutoLoadRunsLoadedName()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;
            _palette._input.value = "paletteping";
            yield return null;
            Assert.IsTrue(_palette.MoveSelection(1), "Navigate to the second row");
            Assert.AreEqual("paletteping2", _palette._input.value, "The selection auto-loads");

            yield return SendKeyDown(KeyCode.Return);

            Assert.IsFalse(_palette.IsOpen, "Enter runs the loaded name and closes");
            string[] history = CollectHistory();
            Assert.IsTrue(0 < history.Length, "The executed command must be recorded in history");
            Assert.AreEqual(
                "paletteping2",
                history[^1],
                "Enter executes the auto-loaded name, not the typed filter query"
            );
        }

        [UnityTest]
        public IEnumerator EditingAfterAutoLoadReFilters()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;
            _palette._input.value = "paletteping";
            yield return null;
            Assert.IsTrue(_palette.MoveSelection(1), "Navigate to the second row");
            Assert.AreEqual("paletteping2", _palette._input.value, "The selection auto-loads");

            _palette._input.value = "paletteping1x";
            yield return null;

            Assert.AreEqual(
                0,
                _palette._matchNames.Count,
                "Editing after auto-load re-runs the filter on the edited text"
            );
            AssertResultsCollapsed("No matches collapse the results");
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
        public IEnumerator EnterWithNoResultsIsNoOp()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;

            yield return SendKeyDown(KeyCode.Return);

            Assert.IsTrue(_palette.IsOpen, "Enter without any results leaves the palette open");
            Assert.AreEqual(
                0,
                CollectHistory().Length,
                "Enter without any results executes nothing"
            );
            AssertResultsCollapsed("The bar stays collapsed");
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
            AssertResultsCollapsed("No matches keep the bar collapsed");
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
        public IEnumerator ClosingWithoutOtherFocusableUIReleasesPanelFocus()
        {
            yield return SpawnPalette();

            _palette.Open();
            yield return WaitForFocusedInput("The palette input takes focus on open");

            yield return SendKeyDown(KeyCode.Escape);

            Assert.IsFalse(_palette.IsOpen, "Escape closes the palette");
            Assert.IsNull(
                _palette._paletteRoot.parent,
                "A closed palette must be detached from the panel"
            );

            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && _palette._uiDocument.rootVisualElement.focusController.focusedElement != null
            )
            {
                yield return null;
            }

            Assert.IsNull(
                _palette._uiDocument.rootVisualElement.focusController.focusedElement,
                "Closing without other focusable UI must release panel focus, "
                    + "or the hidden palette keeps consuming game keys"
            );
        }

        [UnityTest]
        public IEnumerator ReopenAfterCloseShowsBarAndFocus()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return WaitForFocusedInput("The first open focuses the input");
            _palette._input.value = "paletteping";
            yield return null;
            AssertResultsExpanded("Typing before close expands the results");
            _palette.Close();
            Assert.IsNull(_palette._paletteRoot.parent, "Close detaches the palette tree");

            _palette.Open();
            yield return null;

            Assert.IsTrue(_palette.IsOpen, "Reopening after close works");
            Assert.AreEqual(0, _palette._matchNames.Count, "Reopen resets to the bar-only state");
            AssertResultsCollapsed("Reopen shows only the bar");
            yield return WaitForFocusedInput("The reopened palette focuses the input again");
        }

        [UnityTest]
        public IEnumerator TabAppliesSelectedNameToInput()
        {
            yield return SpawnPalette();
            RegisterPair();

            _palette.Open();
            yield return null;
            _palette._input.value = "paletteping";
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
            Assert.AreEqual(
                new[] { "paletteping2" },
                _palette._matchNames.ToArray(),
                "Applying re-filters to the exact command"
            );
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
    Visibility asserts read the imperative inline display state: test panels
    carry no stylesheet, so USS-driven visibility would not resolve here.
 */
        private void AssertResultsCollapsed(string message)
        {
            Assert.AreEqual(DisplayStyle.None, _palette._results.style.display.value, message);
            Assert.AreEqual(DisplayStyle.None, _palette._divider.style.display.value, message);
        }

        private void AssertResultsExpanded(string message)
        {
            Assert.AreEqual(DisplayStyle.Flex, _palette._results.style.display.value, message);
            Assert.AreEqual(DisplayStyle.Flex, _palette._divider.style.display.value, message);
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
