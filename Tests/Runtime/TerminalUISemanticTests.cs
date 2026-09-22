namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DxCommandTerminal.Input;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Pins the input semantics of an open terminal: focus placement,
        execution and its buffer trail, error retention, history navigation,
        and completion-hint selection. The rig uses zero-duration animations
        and polls every frame-coupled surface (see the run-terminal-tests
        skill) so assertions hold under editor throttling.
     */
    public sealed class TerminalUISemanticTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private const int FrameBudget = 600;

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private static void AssertLogContains(TerminalLogType type, string fragment, string message)
        {
            IReadOnlyList<LogItem> logs = Terminal.Buffer.Logs;
            for (int index = 0; index < logs.Count; ++index)
            {
                LogItem item = logs[index];
                if (
                    item.type == type
                    && 0 <= item.message.IndexOf(fragment, StringComparison.Ordinal)
                )
                {
                    return;
                }
            }

            Assert.Fail(
                $"{message}: no {type} entry containing '{fragment}' in "
                    + $"{logs.Count} buffer entries"
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

        [TearDown]
        public void TearDown()
        {
            /*
                The input abstraction is a process-wide singleton outside the
                per-test session reset; clear it so a mid-test failure cannot
                leak staged text into the next test's first command.
             */
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
        public IEnumerator OpenFocusesTheInputField()
        {
            yield return SpawnOpenTerminal();

            yield return WaitForFocusedInput("Opening the terminal must focus the input field");
        }

        [UnityTest]
        public IEnumerator EnterCommandRunsClearsAndLogsTheInput()
        {
            yield return SpawnOpenTerminal();

            int runs = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "semanticrun",
                    _ => ++runs,
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the probe command registers"
            );

            DefaultTerminalInput.Instance.CommandText = "semanticrun";
            _terminal.EnterCommand();

            Assert.AreEqual(1, runs, "EnterCommand must run the typed command exactly once");
            Assert.AreEqual(
                string.Empty,
                DefaultTerminalInput.Instance.CommandText,
                "EnterCommand must clear the input after running"
            );
            AssertLogContains(
                TerminalLogType.Input,
                "semanticrun",
                "The executed line must be logged as input"
            );
            yield return WaitForFocusedInput("EnterCommand must keep the input focused");
        }

        [UnityTest]
        public IEnumerator FailedCommandRetainsErrorAndStaysUsable()
        {
            yield return SpawnOpenTerminal();

            int runs = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "semanticafter",
                    _ => ++runs,
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the probe command registers"
            );

            DefaultTerminalInput.Instance.CommandText = "not-a-real-command-058";
            _terminal.EnterCommand();

            AssertLogContains(
                TerminalLogType.Error,
                "not-a-real-command-058",
                "The failed command's error must be retained in the buffer"
            );
            Assert.IsFalse(
                Terminal.Shell.TryConsumeErrorMessage(out _),
                "EnterCommand must drain the shell error queue with the retained log entry"
            );
            Assert.AreEqual(
                string.Empty,
                DefaultTerminalInput.Instance.CommandText,
                "A failed command still clears the input"
            );

            DefaultTerminalInput.Instance.CommandText = "semanticafter";
            _terminal.EnterCommand();
            Assert.AreEqual(1, runs, "The terminal must stay usable after a failed command");
        }

        [UnityTest]
        public IEnumerator HistoryNavigationLoadsEntriesIntoInput()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "hist-alpha",
                    _ => { },
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the first history command registers"
            );
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "hist-beta",
                    _ => { },
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the second history command registers"
            );

            RunThroughInput("hist-alpha");
            RunThroughInput("hist-beta");

            _terminal.HandlePrevious();
            Assert.AreEqual(
                "hist-beta",
                DefaultTerminalInput.Instance.CommandText,
                "The first Previous must load the newest history entry"
            );

            _terminal.HandlePrevious();
            Assert.AreEqual(
                "hist-alpha",
                DefaultTerminalInput.Instance.CommandText,
                "The second Previous must load the older history entry"
            );

            _terminal.HandleNext();
            Assert.AreEqual(
                "hist-beta",
                DefaultTerminalInput.Instance.CommandText,
                "Next must walk forward toward the newest entry"
            );

            _terminal.HandleNext();
            Assert.AreEqual(
                string.Empty,
                DefaultTerminalInput.Instance.CommandText,
                "Next past the newest entry must empty the input"
            );
        }

        [UnityTest]
        public IEnumerator HistorySkipsConsecutiveDuplicateEntries()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "hist-dup",
                    _ => { },
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the duplicate history command registers"
            );
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "hist-unique",
                    _ => { },
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the unique history command registers"
            );

            RunThroughInput("hist-dup");
            RunThroughInput("hist-dup");
            RunThroughInput("hist-unique");

            _terminal.HandlePrevious();
            Assert.AreEqual(
                "hist-unique",
                DefaultTerminalInput.Instance.CommandText,
                "The first Previous must load the newest entry"
            );

            _terminal.HandlePrevious();
            Assert.AreEqual(
                "hist-dup",
                DefaultTerminalInput.Instance.CommandText,
                "The second Previous must land on the repeated entry once"
            );

            _terminal.HandlePrevious();
            Assert.AreEqual(
                string.Empty,
                DefaultTerminalInput.Instance.CommandText,
                "The repeated entry must not come back a second time while walking backward"
            );
        }

        [UnityTest]
        public IEnumerator CompletionHintsRenderAndSelectionFollowsCycling()
        {
            yield return SpawnOpenTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "alphacmd",
                    _ => { },
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the first completion candidate registers"
            );
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "alphabet",
                    _ => { },
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the second completion candidate registers"
            );

            yield return SetInputText("alph");

            _terminal.CompleteCommand(true);

            ScrollView hints = null;
            int frameBudget = FrameBudget;
            while (0 < frameBudget--)
            {
                hints = _terminal._autoCompleteContainer;
                if (hints != null && hints.childCount == 2)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                hints != null && hints.childCount == 2,
                "Completion must render one hint per candidate"
            );
            yield return WaitForInputText(
                "alphabet",
                "The first completion must apply the first candidate"
            );
            yield return WaitForSelectedHint(
                hints,
                "alphabet",
                "The selected hint must match the applied candidate"
            );

            /*
                The hint view is a carousel: cycling rotates the rendered
                hints so the selected candidate stays in view, so the
                selection is pinned by text, not by child position.
             */
            _terminal.CompleteCommand(true);

            yield return WaitForInputText(
                "alphacmd",
                "Cycling must apply the next candidate to the input"
            );
            yield return WaitForSelectedHint(
                hints,
                "alphacmd",
                "The selection must follow the cycled candidate"
            );
        }

        private IEnumerator SpawnOpenTerminal()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUISemantics");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("Terminal UI semantic coverage runs in the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);

            /*
                SetState flags a command as issued for the current frame; the
                input change handler reverts field writes until that flag
                clears, so the rig settles for one frame before driving the
                input.
             */
            yield return null;

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
                "The terminal input field must exist after the terminal opens"
            );
        }

        private void RunThroughInput(string commandName)
        {
            DefaultTerminalInput.Instance.CommandText = commandName;
            _terminal.EnterCommand();
        }

        private IEnumerator SetInputText(string text)
        {
            _terminal._commandInput.value = text;
            yield return null;
        }

        private IEnumerator WaitForFocusedInput(string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !InputOwnsFocus())
            {
                yield return null;
            }

            Assert.IsTrue(InputOwnsFocus(), message);
        }

        /*
            The completion writes land through RefreshUI on LateUpdate, which
            editor throttling can defer for frames; the rig polls the input
            abstraction and the field mirror instead of assuming a frame.
         */
        private IEnumerator WaitForInputText(string expected, string message)
        {
            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && !string.Equals(
                    DefaultTerminalInput.Instance.CommandText,
                    expected,
                    StringComparison.Ordinal
                )
                && !string.Equals(_terminal._commandInput.value, expected, StringComparison.Ordinal)
            )
            {
                yield return null;
            }

            Assert.IsTrue(
                string.Equals(
                    DefaultTerminalInput.Instance.CommandText,
                    expected,
                    StringComparison.Ordinal
                )
                    || string.Equals(
                        _terminal._commandInput.value,
                        expected,
                        StringComparison.Ordinal
                    ),
                $"{message}: field='{_terminal._commandInput.value}'"
                    + $" input='{DefaultTerminalInput.Instance.CommandText}'"
            );
        }

        /*
            The hint view is a carousel (UpdateAutoCompleteView rotates the
            rendered hints so the selection stays in view), so the selected
            candidate is pinned by text, never by child position. The
            selected class is written by RefreshAutoCompleteHints during
            LateUpdate, so the rig polls for it like every other frame-
            coupled surface.
         */
        private IEnumerator WaitForSelectedHint(
            ScrollView hints,
            string expectedText,
            string message
        )
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !HasSingleSelectedHint(hints, expectedText))
            {
                yield return null;
            }

            Assert.IsTrue(
                HasSingleSelectedHint(hints, expectedText),
                $"{message}: {DescribeSelectedHints(hints)}"
            );
        }

        private bool HasSingleSelectedHint(ScrollView hints, string expectedText)
        {
            int selectedCount = 0;
            bool matchedText = false;
            for (int index = 0; index < hints.childCount; ++index)
            {
                VisualElement hint = hints[index];
                if (!hint.ClassListContains("autocomplete-item-selected"))
                {
                    continue;
                }

                ++selectedCount;
                matchedText |= string.Equals(
                    (hint as TextElement)?.text,
                    expectedText,
                    StringComparison.Ordinal
                );
            }

            return selectedCount == 1 && matchedText;
        }

        private string DescribeSelectedHints(ScrollView hints)
        {
            string description = "selected=";
            for (int index = 0; index < hints.childCount; ++index)
            {
                VisualElement hint = hints[index];
                if (!hint.ClassListContains("autocomplete-item-selected"))
                {
                    continue;
                }

                description += $"'{(hint as TextElement)?.text}' ";
            }

            return description;
        }

        /*
            Depending on panel state the focus controller reports either the
            TextField or its inner text-input element; both mean the input
            owns focus (see the palette suite's equivalent helper).
         */
        private bool InputOwnsFocus()
        {
            if (_terminal._commandInput == null || _terminal._uiDocument == null)
            {
                return false;
            }

            FocusController focusController = _terminal
                ._uiDocument
                .rootVisualElement
                .focusController;
            VisualElement focused = focusController?.focusedElement as VisualElement;
            return focused == _terminal._commandInput || _terminal._commandInput.Contains(focused);
        }
    }
}
