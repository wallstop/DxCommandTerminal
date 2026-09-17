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
        /*
            Readiness-poll headroom, not a fixed expectation: under editor
            throttling (unfocused/agent-driven panels) caret parks and focus
            writes can take far more than the ~60 frames a focused editor
            needs, and the two-pass caret stick rule needs several passes to
            land. The polls exit the frame they succeed, so the extra
            headroom never slows a green run.
         */
        private const int FrameBudget = 600;

        private static readonly string[] InventoryItems = { "pickaxe", "torch", "torch pick" };

        private CommandPaletteUI _palette;
        private GameObject _paletteObject;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;
        private TerminalUI _sharedTerminal;
        private GameObject _extraPaletteObject;

        private static void RegisterPair()
        {
            Terminal.Shell.AddCommand("paletteping1", _ => { }, help: "first");
            Terminal.Shell.AddCommand("paletteping2", _ => { }, help: "second");
        }

        private static void RegisterEchoCommand()
        {
            /*
                Emits the two log shapes an output-producing command produces
                (like list-fonts) so the palette's output capture has entries
                to surface.
             */
            Terminal.Shell.AddCommand(
                "paletteecho",
                _ =>
                {
                    Terminal.Log(TerminalLogType.Message, "Fira Mono");
                    Terminal.Log(TerminalLogType.Warning, "No font pack found.");
                },
                help: "Echoes two output lines"
            );
        }

        private static void AssertResolved(string key, string expectedName, bool shift, bool ctrl)
        {
            InputHelpers.CachedKeyName resolved = InputHelpers.ResolveKeyName(key);
            Assert.AreEqual(expectedName, resolved.Name, $"Resolved key name for '{key}'");
            Assert.AreEqual(shift, resolved.ShiftRequired, $"Shift requirement for '{key}'");
            Assert.AreEqual(ctrl, resolved.CtrlRequired, $"Ctrl requirement for '{key}'");
        }

        private static CommandCompletionProvider InventoryStage()
        {
            return (in CommandCompletionContext context, List<CommandCompletion> results) =>
            {
                foreach (string item in InventoryItems)
                {
                    if (item.StartsWith(context.Token, StringComparison.Ordinal))
                    {
                        results.Add(new CommandCompletion(item));
                    }
                }
            };
        }

        private static void RegisterInventoryCommand()
        {
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "pickitem",
                        Handler = (context, arguments) => { },
                        CompletionProvider = InventoryStage(),
                    }
                )
            );
        }

        private static void RegisterSpawnItemCommand()
        {
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "spawnitem",
                        Handler = (context, arguments) => { },
                        CompletionProvider = CommandCompletionProviders.Staged(
                            InventoryStage(),
                            (
                                in CommandCompletionContext context,
                                List<CommandCompletion> results
                            ) =>
                            {
                                foreach (string count in new[] { "1", "2", "3" })
                                {
                                    results.Add(new CommandCompletion(count));
                                }
                            }
                        ),
                    }
                )
            );
        }

        private static void RegisterEmptyChoiceCommand()
        {
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "emptychoice",
                        Handler = (context, arguments) => { },
                        CompletionProvider = (
                            in CommandCompletionContext context,
                            List<CommandCompletion> results
                        ) => { },
                    }
                )
            );
        }

        private static void RegisterEditorOnlyCommand()
        {
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "editcommand",
                        Contexts = CommandExecutionContexts.EditorEditMode,
                        Handler = (context, arguments) => { },
                        CompletionProvider = (
                            in CommandCompletionContext context,
                            List<CommandCompletion> results
                        ) => results.Add(new CommandCompletion("editvalue")),
                    }
                )
            );
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

            _sharedTerminal = null;

            if (_extraPaletteObject != null)
            {
                UnityEngine.Object.Destroy(_extraPaletteObject);
                _extraPaletteObject = null;
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
            Assert.IsFalse(InputHelpers.IsKeyPressed("space", default));
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
            yield return null;

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
        public IEnumerator SharedSurfaceKeepsRootHeightAndFocus()
        {
            yield return SpawnSharedSurface();
            RegisterPair();

            TerminalUI.Instance.SetState(TerminalState.OpenSmall);
            yield return null;
            Assert.IsFalse(
                TerminalUI.Instance.IsClosed,
                "Sanity: the terminal is open before the palette opens"
            );

            _palette.Open();
            yield return null;
            Assert.IsTrue(_palette.IsOpen, "The palette opens over the shared terminal");

            int frameBudget = 10;
            while (0 < frameBudget--)
            {
                yield return null;
            }

            VisualElement documentRoot = _palette._uiDocument.rootVisualElement;
            Assert.AreEqual(
                StyleKeyword.Auto,
                documentRoot.style.height.keyword,
                "The terminal must not clamp the shared root to its own window "
                    + "height while the palette owns the surface"
            );
            Assert.IsTrue(
                InputOwnsFocus(),
                "The palette input keeps panel focus while the terminal runs its "
                    + "close animation"
            );

            _palette.Close();
            yield return null;

            Assert.AreNotEqual(
                StyleKeyword.Auto,
                documentRoot.style.height.keyword,
                "Closing the palette hands the shared root back to the terminal's "
                    + "own window height"
            );
        }

        [UnityTest]
        public IEnumerator OpeningTerminalClosesAllPalettesOnSharedDocument()
        {
            yield return SpawnSharedSurface();
            RegisterPair();

            /*
                A second palette shares the document without claiming the
                static Instance (the first palette owns it); CloseActive alone
                leaves it open and the terminal never reclaims the surface.
             */
            _extraPaletteObject = new GameObject("ExtraPalette");
            UIDocument extraDocument = _extraPaletteObject.AddComponent<UIDocument>();
            extraDocument.panelSettings = _panelSettings;
            CommandPaletteUI extraPalette = _extraPaletteObject.AddComponent<CommandPaletteUI>();
            extraPalette._uiDocument = _palette._uiDocument;
            extraPalette.verticalPosition = 0.8f;
            extraPalette.Open();

            TerminalUI.Instance.SetState(TerminalState.OpenSmall);
            _palette.Open();
            extraPalette.Open();
            yield return null;

            Assert.IsTrue(_palette.IsOpen, "Sanity: the instance palette is open");
            Assert.IsTrue(extraPalette.IsOpen, "Sanity: the extra palette is open");

            TerminalUI.Instance.SetState(TerminalState.OpenSmall);
            yield return null;

            Assert.IsFalse(_palette.IsOpen, "Opening the terminal closes the instance palette");
            Assert.IsFalse(
                extraPalette.IsOpen,
                "Opening the terminal closes every palette on the shared document"
            );
            Assert.IsFalse(
                TerminalUI.Instance.IsClosed,
                "The terminal stays open and reclaims the surface"
            );

            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && _palette._uiDocument.rootVisualElement.style.height.keyword == StyleKeyword.Auto
            )
            {
                yield return null;
            }

            Assert.AreNotEqual(
                StyleKeyword.Auto,
                _palette._uiDocument.rootVisualElement.style.height.keyword,
                "The terminal reasserts the shared root height once no palette holds it"
            );

            UnityEngine.Object.Destroy(_extraPaletteObject);
            _extraPaletteObject = null;
        }

        [UnityTest]
        public IEnumerator OutputCommandStaysOpenAndShowsOutput()
        {
            yield return SpawnPalette();
            RegisterEchoCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "paletteecho";
            yield return null;

            Assert.IsTrue(_palette.Submit(), "An output-producing command submits");
            Assert.IsTrue(_palette.IsOpen, "Output-producing commands keep the palette open");
            Assert.AreEqual(
                DisplayStyle.Flex,
                _palette._output.style.display.value,
                "The command output is displayed"
            );
            StringAssert.Contains("Fira Mono", _palette._output.text, "Output lines shown");
            StringAssert.Contains("No font pack found.", _palette._output.text, "Warnings shown");
            StringAssert.DoesNotContain(
                "paletteecho",
                _palette._output.text,
                "The input echo is not part of the displayed output"
            );
            Assert.AreEqual(
                string.Empty,
                _palette._input.value,
                "The executed command leaves the bar so the output reads on its own"
            );
            AssertResultsCollapsed(
                "The executed command's result rows collapse with the cleared bar"
            );
            Assert.IsFalse(
                _palette.Submit(),
                "Enter with the cleared bar does not re-run the command"
            );
            Assert.IsTrue(_palette.IsOpen, "A no-op Enter keeps the output visible");
            Assert.AreEqual(
                DisplayStyle.Flex,
                _palette._output.style.display.value,
                "The no-op Enter keeps the output visible"
            );

            _palette._input.value = "heal";
            yield return null;

            Assert.AreEqual(
                DisplayStyle.None,
                _palette._output.style.display.value,
                "Typing a new query clears the previous run's output"
            );
        }

        [UnityTest]
        public IEnumerator DownAfterOutputRunKeepsInputClean()
        {
            yield return SpawnPalette();
            RegisterEchoCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "paletteecho";
            yield return null;

            Assert.IsTrue(_palette.Submit(), "The command runs and shows output");
            Assert.AreEqual(
                string.Empty,
                _palette._input.value,
                "Sanity: the completed command left the bar"
            );

            yield return null;
            yield return SendKeyDown(KeyCode.DownArrow);

            Assert.AreEqual(
                _palette._input.selectIndex,
                _palette._input.cursorIndex,
                "Down after a completed command never selects input text"
            );
            Assert.AreEqual(
                string.Empty,
                _palette._input.value,
                "Down after a completed command leaves the bar clear"
            );
            Assert.IsTrue(_palette.IsOpen, "The output stays visible");
        }

        [UnityTest]
        public IEnumerator ResultsScrollerIsNotFocusable()
        {
            yield return SpawnPalette();
            for (int index = 0; index < 10; ++index)
            {
                Terminal.Shell.AddCommand(
                    "palettescroll" + index,
                    _ => { },
                    help: "overflow row " + index
                );
            }

            _palette.Open();
            yield return null;
            _palette._input.value = "palettescroll";
            yield return null;

            Assert.IsTrue(
                10 <= _palette._matchNames.Count,
                "Sanity: the query matches enough rows to overflow"
            );

            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && _palette._results.verticalScroller == null)
            {
                yield return null;
            }

            Assert.IsNotNull(
                _palette._results.verticalScroller,
                "The overflowing results create a scroller"
            );
            Assert.IsFalse(
                _palette._results.verticalScroller.focusable,
                "Clicking the scrollbar must not steal panel focus from the input"
            );
            Assert.IsNotNull(
                _palette._results.verticalScroller.slider,
                "The scroller hosts the slider UITK focuses on click"
            );
            Assert.IsFalse(
                _palette._results.verticalScroller.slider.focusable,
                "The child slider must opt out of focus too; it is what takes the caret"
            );
        }

        [UnityTest]
        public IEnumerator RunDiagnosticsReplaceEachOther()
        {
            yield return SpawnPalette();
            RegisterEchoCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "paletteecho";
            yield return null;
            Assert.IsTrue(_palette.Submit(), "The output command runs");
            Assert.AreEqual(
                DisplayStyle.Flex,
                _palette._output.style.display.value,
                "Sanity: the output is shown"
            );

            _palette._input.value = "palettenosuchcommand";
            yield return null;
            Assert.IsFalse(_palette.Submit(), "The unknown command runs and fails");

            Assert.AreEqual(
                DisplayStyle.None,
                _palette._output.style.display.value,
                "A later error hides the previous run's output"
            );
            StringAssert.StartsWith(
                "Error:",
                _palette._feedback.text,
                "The later error is the visible diagnostic"
            );

            _palette._input.value = "paletteecho";
            yield return null;
            Assert.IsTrue(_palette.Submit(), "The output command runs again");
            Assert.AreEqual(
                DisplayStyle.Flex,
                _palette._output.style.display.value,
                "Sanity: the output is shown again"
            );
            Assert.AreEqual(
                DisplayStyle.None,
                _palette._feedback.style.display.value,
                "A later output hides the previous run's error"
            );
        }

        [UnityTest]
        public IEnumerator ArgumentCompletionListsProviderCandidates()
        {
            yield return SpawnPalette();
            RegisterInventoryCommand();
            RegisterSpawnItemCommand();

            _palette.Open();
            yield return null;

            (string query, string[] expectedRows)[] cases =
            {
                ("pickitem ", new[] { "pickaxe", "torch", "torch pick" }),
                ("pickitem to", new[] { "torch", "torch pick" }),
                ("pickitem \"pi", new[] { "pickaxe" }),
            };
            foreach ((string query, string[] expectedRows) in cases)
            {
                _palette.SetQuery(query);
                yield return null;

                Assert.AreEqual(
                    expectedRows,
                    _palette._matchNames.ToArray(),
                    $"Completion rows for '{query}'"
                );
                string[] insertions = new string[expectedRows.Length];
                for (int index = 0; index < _palette._completions.Count; ++index)
                {
                    insertions[index] = _palette._completions[index].InsertionText;
                }

                CollectionAssert.AreEqual(
                    expectedRows,
                    insertions,
                    $"Completion insertions for '{query}'"
                );
            }
        }

        [UnityTest]
        public IEnumerator ZeroCandidateProviderCollapsesResults()
        {
            yield return SpawnPalette();
            RegisterEmptyChoiceCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "emptychoice x";
            yield return null;

            Assert.AreEqual(
                0,
                _palette._completions.Count,
                "A provider answer without candidates leaves no completion state"
            );
            AssertResultsCollapsed("An empty provider answer must not fall back to name filtering");
        }

        [UnityTest]
        public IEnumerator ArgumentCompletionFallsBackToCommandNames()
        {
            yield return SpawnPalette();
            RegisterInventoryCommand();
            RegisterPair();

            _palette.Open();
            yield return null;
            _palette._input.value = "pickitem";
            yield return null;

            Assert.AreEqual(
                0,
                _palette._completions.Count,
                "A query without an active argument stage stays a command-name search"
            );
            Assert.AreEqual(
                new[] { "pickitem" },
                _palette._matchNames.ToArray(),
                "Command-name filtering keeps answering while no argument is active"
            );
        }

        [UnityTest]
        public IEnumerator IneligibleCommandArgumentIsNotCompleted()
        {
            yield return SpawnPalette();
            RegisterEditorOnlyCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "editcommand ";
            yield return null;

            Assert.AreEqual(
                0,
                _palette._completions.Count,
                "An Editor-only command must not offer argument candidates in Play Mode"
            );
            Assert.AreEqual(
                0,
                _palette._matchNames.Count,
                "The name filter also hides the ineligible command"
            );
            AssertResultsCollapsed("The bar stays collapsed for an ineligible command");
        }

        [UnityTest]
        public IEnumerator TabAppliesArgumentCompletionWithQuoting()
        {
            yield return SpawnPalette();
            RegisterInventoryCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "pickitem ";
            yield return null;
            Assert.IsTrue(_palette.MoveSelection(2), "Navigate to the multi-word item");

            yield return SendKeyDown(KeyCode.Tab);
            yield return WaitForCaret(21, "The caret lands after the quoted insertion");

            Assert.AreEqual(
                "pickitem \"torch pick\"",
                _palette._input.value,
                "Tab quotes an insertion containing spaces when the token is unquoted"
            );
        }

        [UnityTest]
        public IEnumerator TabAppliesInsideExistingQuotesVerbatim()
        {
            yield return SpawnPalette();
            RegisterInventoryCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "pickitem \"to";
            yield return null;
            Assert.AreEqual(
                new[] { "torch", "torch pick" },
                _palette._matchNames.ToArray(),
                "The quoted token prefix still filters provider candidates"
            );

            yield return SendKeyDown(KeyCode.Tab);
            yield return WaitForCaret(15, "The caret lands after the inserted item");

            Assert.AreEqual(
                "pickitem \"torch",
                _palette._input.value,
                "Tab inside an open quote inserts verbatim without extra quoting"
            );
        }

        /*
            Issue #74: a panel-driven re-clamp can move the caret away after
            the first parking pass already landed. The queued caret must
            re-assert, then drain so later movement is never fought.
         */
        [UnityTest]
        public IEnumerator TabCaretSurvivesPanelReclamp()
        {
            yield return SpawnPalette();
            RegisterInventoryCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "pickitem to";
            yield return null;
            Assert.AreEqual(
                new[] { "torch", "torch pick" },
                _palette._matchNames.ToArray(),
                "Sanity: the stage offers the item candidates"
            );

            yield return SendKeyDown(KeyCode.Tab);
            yield return WaitForCaret(14, "The caret lands after the applied candidate");

            _palette._input.cursorIndex = 9;
            _palette._input.selectIndex = 9;

            yield return WaitForCaret(
                14,
                "The queued caret re-asserts after a panel-driven re-clamp"
            );
            Assert.AreEqual(
                14,
                _palette._input.selectIndex,
                "The re-asserted caret is not left selected at the stale position"
            );

            yield return WaitForPendingCaretDrained();
            _palette._input.cursorIndex = 2;
            yield return null;
            yield return null;
            Assert.AreEqual(
                2,
                _palette._input.cursorIndex,
                "Movement after the caret settles is never re-asserted"
            );
        }

        [UnityTest]
        public IEnumerator PendingCaretConsumesOnlyAfterStablePasses()
        {
            yield return SpawnPalette();
            _palette.Open();
            yield return null;

            _palette._input.SetValueWithoutNotify("pickitem torch");
            yield return null;

            /*
                The field's caret model clamps to its last laid-out text
                length, which can lag the value by frames under session
                sequences (issue #74 family), so the rule pins run against a
                position the field actually holds, discovered by probe. The
                rules themselves are position-independent.
             */
            _palette._input.cursorIndex = 14;
            _palette._input.selectIndex = 14;
            int target = _palette._input.cursorIndex;
            Assert.GreaterOrEqual(target, 1, "Sanity: the field holds a parkable caret position");

            _palette._input.cursorIndex = 0;
            _palette._input.selectIndex = 0;
            _palette._pendingCaretIndex = target;
            _palette.ApplyPendingCaret();
            Assert.AreEqual(target, _palette._input.cursorIndex, "The first pass parks the caret");
            Assert.AreEqual(0, _palette._caretStickPasses, "The parking pass has not stuck yet");
            Assert.AreEqual(
                target,
                _palette._pendingCaretIndex,
                "The marker survives the parking pass"
            );

            _palette._input.cursorIndex = target - 1;
            _palette._input.selectIndex = target - 1;
            _palette.ApplyPendingCaret();
            Assert.AreEqual(
                target,
                _palette._input.cursorIndex,
                "A drift pass re-asserts the queued caret"
            );
            Assert.AreEqual(
                target,
                _palette._pendingCaretIndex,
                "The marker survives the drift pass"
            );

            _palette.ApplyPendingCaret();
            Assert.AreEqual(
                target,
                _palette._pendingCaretIndex,
                "One stable pass is not enough to consume"
            );

            _palette.ApplyPendingCaret();
            Assert.IsNull(_palette._pendingCaretIndex, "Two stable passes consume the marker");
            Assert.AreEqual(
                target,
                _palette._input.cursorIndex,
                "The caret stays parked after consumption"
            );

            _palette._pendingCaretIndex = 30;
            _palette.ApplyPendingCaret();
            Assert.AreEqual(
                target,
                _palette._input.cursorIndex,
                "A position beyond the value is not written yet"
            );
            Assert.AreEqual(30, _palette._pendingCaretIndex, "The marker waits for the value sync");

            _palette._input.cursorIndex = 2;
            _palette._pendingCaretIndex = null;
            _palette.ApplyPendingCaret();
            Assert.AreEqual(2, _palette._input.cursorIndex, "No marker means no caret write");
        }

        [UnityTest]
        public IEnumerator UserEditCancelsPendingCaret()
        {
            yield return SpawnPalette();
            _palette.Open();
            yield return null;

            _palette._input.SetValueWithoutNotify("pickitem torch");
            yield return null;

            _palette._pendingCaretIndex = 14;
            _palette._input.value = "pickitem torchx";
            int caret = _palette._input.cursorIndex;
            _palette.ApplyPendingCaret();
            Assert.IsNull(
                _palette._pendingCaretIndex,
                "A field change is a user edit and cancels the queued caret"
            );
            Assert.AreEqual(
                caret,
                _palette._input.cursorIndex,
                "The cancelled caret writes nothing on the pass"
            );
        }

        [UnityTest]
        public IEnumerator ArgumentCompletionChainsToNextStage()
        {
            yield return SpawnPalette();
            RegisterSpawnItemCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "spawnitem torch ";
            yield return null;
            Assert.AreEqual(
                new[] { "1", "2", "3" },
                _palette._matchNames.ToArray(),
                "The second argument stage answers after the first is parsed"
            );

            yield return SendKeyDown(KeyCode.Tab);

            Assert.AreEqual(
                "spawnitem torch 1",
                _palette._input.value,
                "Tab applies the selected stage-one candidate"
            );
            Assert.AreEqual(
                new[] { "1", "2", "3" },
                _palette._matchNames.ToArray(),
                "The caret stays on the applied token, so Tab can keep cycling its candidates"
            );
            AssertResultsExpanded("The applied stage keeps its candidate list visible");
        }

        [UnityTest]
        public IEnumerator TypingAfterCommandApplyContinuesIntoArgumentCompletion()
        {
            yield return SpawnPalette();
            RegisterInventoryCommand();

            _palette.Open();
            yield return null;
            _palette._input.value = "pickitem";
            yield return null;
            Assert.AreEqual(
                new[] { "pickitem" },
                _palette._matchNames.ToArray(),
                "The name filter answers the command name first"
            );

            yield return SendKeyDown(KeyCode.Tab);
            Assert.AreEqual(
                "pickitem",
                _palette._input.value,
                "Tab commits the selected command name"
            );

            /*
               Appending to the committed command derives the caret from the
               change itself (the text field's cursorIndex lags value writes),
               so the next keystroke completes the new token, not the stale
               caret position.
             */
            _palette._input.value = "pickitem ";
            yield return null;
            Assert.AreEqual(
                new[] { "pickaxe", "torch", "torch pick" },
                _palette._matchNames.ToArray(),
                "The append opens the command's stage-zero candidates"
            );

            _palette._input.value = "pickitem to";
            yield return null;
            Assert.AreEqual(
                new[] { "torch", "torch pick" },
                _palette._matchNames.ToArray(),
                "The append-derived caret completes the token the user is editing"
            );
        }

        [UnityTest]
        public IEnumerator EnterRunsArgumentCompletedCommand()
        {
            yield return SpawnPalette();
            int executed = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "pickitem",
                        Handler = (context, arguments) =>
                        {
                            ++executed;
                        },
                        CompletionProvider = InventoryStage(),
                    }
                )
            );

            _palette.Open();
            yield return null;
            _palette._input.value = "pickitem torch";
            yield return null;
            Assert.AreEqual(
                new[] { "torch", "torch pick" },
                _palette._matchNames.ToArray(),
                "The argument stage shows the provider's candidates"
            );

            yield return SendKeyDown(KeyCode.Return);

            Assert.AreEqual(1, executed, "Enter runs the command with its completed arguments");
            Assert.IsFalse(_palette.IsOpen, "A successful run closes the palette");
        }

        [UnityTest]
        public IEnumerator RowActivationAppliesArgumentCompletionWithoutRunning()
        {
            yield return SpawnPalette();
            int executed = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "pickitem",
                        Handler = (context, arguments) =>
                        {
                            ++executed;
                        },
                        CompletionProvider = InventoryStage(),
                    }
                )
            );

            _palette.Open();
            yield return null;
            _palette._input.value = "pickitem to";
            yield return null;
            Assert.AreEqual(2, _palette._rows.Count, "The activation target row exists");

            /*
                Unity's dispatcher drops synthetic pointer events (the
                session-016 pointer-parity limitation), so the row's
                activation logic is driven at the index level; the
                ClickEvent binding itself is Unity plumbing.
             */
            _palette.RowActivated(0);
            yield return null;

            Assert.AreEqual(0, executed, "Applying an argument completion must not run anything");
            Assert.IsTrue(_palette.IsOpen, "Applying keeps the palette open");
            Assert.AreEqual(
                "pickitem torch",
                _palette._input.value,
                "The activation applies the selected candidate to the active token"
            );
        }

        private IEnumerator WaitForCaret(int expected, string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && _palette._input.cursorIndex != expected)
            {
                yield return null;
            }

            Assert.AreEqual(expected, _palette._input.cursorIndex, message);
        }

        private IEnumerator WaitForPendingCaretDrained()
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && _palette._pendingCaretIndex != null)
            {
                yield return null;
            }

            Assert.IsNull(
                _palette._pendingCaretIndex,
                "The queued caret drains once the position holds"
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

        private IEnumerator SpawnSharedSurface()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _paletteObject = new GameObject("SharedSurface");
            _paletteObject.SetActive(false);
            UIDocument document = _paletteObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _sharedTerminal = _paletteObject.AddComponent<TerminalUI>();
            _palette = _paletteObject.AddComponent<CommandPaletteUI>();
            _sharedTerminal._uiDocument = document;
            _palette._uiDocument = document;
            LogAssert.Expect(LogType.Error, "No theme pack assigned, cannot initialize theme.");
            LogAssert.Expect(LogType.Error, "No font pack assigned, cannot initialize font.");
            LogAssert.Expect(LogType.Error, "Failed to load any themes!");
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
