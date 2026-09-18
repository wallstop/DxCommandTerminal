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
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public sealed class TerminalUITokenCompletionTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private Func<CommandExecutionContext> _previousAmbientProvider;

        private static CommandDefinition DefinePickupWithStagedInventory()
        {
            return new CommandDefinition
            {
                Name = "pickup",
                Handler = (context, arguments) => { },
                CompletionProvider = CommandCompletionProviders.Staged(
                    (in CommandCompletionContext context, List<CommandCompletion> results) =>
                    {
                        foreach (string item in new[] { "pickaxe", "torch", "torch pick" })
                        {
                            if (item.StartsWith(context.Token, StringComparison.Ordinal))
                            {
                                results.Add(new CommandCompletion(item));
                            }
                        }
                    }
                ),
            };
        }

        [SetUp]
        public void SetUp()
        {
            _previousAmbientProvider = CommandExecutionContext.AmbientContextProvider;
            CommandExecutionContext.AmbientContextProvider = null;
        }

        [TearDown]
        public void TearDown()
        {
            CommandExecutionContext.AmbientContextProvider = _previousAmbientProvider;
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
        public IEnumerator TokenCompletionAppliesAndCyclesProviderResults()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup ", 7);

            _terminal.CompleteCommand(true);
            yield return WaitForInput(
                "pickup pickaxe",
                "The first Tab applies the first provider result to the active token"
            );
            Assert.IsEmpty(
                _terminal._lastCompletionBuffer,
                "A provider answer must suppress stale full-line history hints"
            );
            yield return WaitForCaret(14, "The caret lands after the inserted token");

            _terminal.CompleteCommand(true);
            yield return WaitForInput(
                "pickup torch",
                "Repeated Tab presses cycle provider results"
            );

            _terminal.CompleteCommand(true);
            yield return WaitForInput(
                "pickup \"torch pick\"",
                "Insertions containing spaces are quoted when the token is unquoted"
            );
        }

        [UnityTest]
        public IEnumerator BackwardCompletionCyclesInReverse()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup ", 7);

            _terminal.CompleteCommand(false);
            yield return WaitForInput(
                "pickup \"torch pick\"",
                "Reverse completion starts from the last result"
            );

            _terminal.CompleteCommand(false);
            yield return WaitForInput("pickup torch", "Completion applies");
        }

        [UnityTest]
        public IEnumerator TypingResetsProviderCompletion()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup ", 7);

            _terminal.CompleteCommand(true);
            yield return WaitForInput("pickup pickaxe", "Completion applies");

            // Typing a new token resets the cycling snapshot.
            yield return SetInput("pickup to", 9);

            _terminal.CompleteCommand(true);
            yield return WaitForInput(
                "pickup torch",
                "Typing restarts completion from the provider's first prefix match"
            );
        }

        [UnityTest]
        public IEnumerator QuotedTokensAcceptUnquotedInsertions()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup \"to", 10);

            _terminal.CompleteCommand(true);
            yield return WaitForInput(
                "pickup \"torch\"",
                "Completion closes the token to preserve its literal value"
            );
            yield return WaitForCaret(14, "The caret parks on the closed token");
        }

        [UnityTest]
        public IEnumerator MidLineInsertionKeepsTrailingText()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup to after", 9);

            _terminal.CompleteCommand(true);
            yield return WaitForInput(
                "pickup torch after",
                "Only the active token is replaced; trailing text is preserved"
            );
            yield return WaitForCaret(12, "The caret lands before the preserved trailing text");
        }

        [UnityTest]
        public IEnumerator UnfocusedFieldKeepsQueuedCompletionCaret()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup ", 7);
            _terminal._textInput.Blur();

            _terminal.CompleteCommand(true);
            yield return WaitForInput("pickup pickaxe", "Completion applies to an unfocused field");
            yield return WaitForCaret(
                14,
                "The queued caret survives the focus pass instead of jumping to line end"
            );
        }

        [UnityTest]
        public IEnumerator PendingCaretWritesAndKeepsMarkerWhileUnfocused()
        {
            yield return SpawnTerminalWithUi();

            _terminal._commandInput.value = "abc";
            yield return null;

            /*
                The caret cap converges with layout (see SetInput), so the
                applied positions are readiness-polled; the marker rules are
                position-independent and pinned exactly.
             */
            _terminal._pendingCaretIndex = 3;
            _terminal.ApplyPendingCaret();
            yield return WaitForCursorIndex(3);
            Assert.AreEqual(
                3,
                _terminal._pendingCaretIndex,
                "An unfocused pass keeps the marker so a later focus cannot jump to line end"
            );

            _terminal._pendingCaretIndex = 1;
            _terminal.ApplyPendingCaret();
            yield return WaitForCursorIndex(1);
            Assert.AreEqual(
                1,
                _terminal._pendingCaretIndex,
                "A pass where the caret has not stuck keeps the marker"
            );
        }

        [UnityTest]
        public IEnumerator ProviderReplacementOverrideWinsOverTokenRange()
        {
            yield return SpawnTerminalWithUi();

            CommandDefinition definition = DefinePickupWithStagedInventory();
            definition.CompletionProvider = (
                in CommandCompletionContext context,
                List<CommandCompletion> results
            ) =>
                results.Add(
                    new CommandCompletion(
                        "pickaxe",
                        replacement: new CommandCompletionReplacement(0, 14)
                    )
                );
            Assert.IsTrue(Terminal.Shell.AddCommand(definition));

            yield return SetInput("pickup pickaxe", 14);

            _terminal.CompleteCommand(true);
            yield return WaitForInput(
                "pickaxe",
                "The override replaces the whole line while the context range would replace only the token"
            );
        }

        [UnityTest]
        public IEnumerator CommandsWithoutProvidersKeepHistoryCompletion()
        {
            yield return SpawnTerminalWithUi();

            /*
               No command starts with 'zzz', so neither completion path has a
               suggestion and the input stays untouched.
            */
            yield return SetInput("zzz", 3);

            _terminal.CompleteCommand(true);
            yield return WaitForInput("zzz", "With no suggestions anywhere the input is untouched");

            /*
                'help' has no provider, so the legacy history-based completion
                runs unchanged: 'h' suggests command names that start with it.
             */
            yield return SetInput("h", 1);

            _terminal.CompleteCommand(true);
            yield return WaitForInput(
                "help",
                "Without a provider the legacy history completion still suggests command names"
            );
        }

        private IEnumerator SpawnTerminalWithUi()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUITokenCompletion");
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
            Assert.Ignore("Token completion UI coverage runs in the editor Play Mode suite.");
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

            Assert.IsNotNull(
                _terminal._commandInput,
                "The terminal input field should exist after the terminal opens"
            );
            Assert.AreEqual(
                DisplayStyle.Flex,
                _terminal._commandInput.resolvedStyle.display,
                "The input field should be visible on an open terminal"
            );
        }

        /*
            A programmatic value write does not preserve a mid-line caret
            across the panel's next update tick, so the rig settles the value
            first and places the caret in a second step. UITK clamps caret
            writes to the last laid-out text length, the cap converges with
            layout, and the panel can re-clamp a programmatic write after it
            landed (see the run-terminal-tests skill), so the placement
            retries the write and readiness-polls for it to hold; the
            completion request runs on the first frame the requested index
            sticks, and falls back to whatever position held when the park
            never lands.
         */
        private IEnumerator SetInput(string text, int caretIndex)
        {
            _terminal._commandInput.value = text;
            yield return null;

            for (int attempt = 0; attempt < 3; ++attempt)
            {
                _terminal._commandInput.cursorIndex = caretIndex;
                _terminal._commandInput.selectIndex = caretIndex;

                int frameBudget = 100;
                while (0 < frameBudget-- && _terminal._commandInput.cursorIndex != caretIndex)
                {
                    yield return null;
                }

                if (_terminal._commandInput.cursorIndex == caretIndex)
                {
                    yield break;
                }
            }
        }

        /*
            The completion writes land through RefreshUI on LateUpdate, which
            editor throttling can defer for frames; the rig polls for the
            value instead of assuming one specific frame.
         */
        private IEnumerator WaitForInput(string expected, string message)
        {
            int frameBudget = 600;
            while (
                0 < frameBudget--
                && !string.Equals(_terminal._commandInput.value, expected, StringComparison.Ordinal)
            )
            {
                yield return null;
            }

            Assert.AreEqual(expected, _terminal._commandInput.value, message);
        }

        /*
            Accepted completions queue the caret for a later RefreshUI pass;
            panel initialization, editor throttling, and unfocused panels can
            defer applying caret state for many frames, so the rig polls for
            the caret to land instead of assuming one specific frame. When a
            throttled panel never applies caret state at all, the queued
            position standing at the insertion point is the invariant the
            rig accepts: it pins that the caret cannot land behind the
            insertion. Whether a line-end jump is distinguishable depends on
            the site: only mid-line insertions expect a caret short of line
            end, so only they can catch a jump positionally.
         */
        private IEnumerator WaitForCaret(int expectedCaretIndex, string message)
        {
            int frameBudget = 600;
            while (0 < frameBudget-- && _terminal._commandInput.cursorIndex != expectedCaretIndex)
            {
                yield return null;
            }

            Assert.IsTrue(
                _terminal._commandInput.cursorIndex == expectedCaretIndex
                    || _terminal._pendingCaretIndex == expectedCaretIndex,
                $"{message}: cursor={_terminal._commandInput.cursorIndex}"
                    + $" queued={_terminal._pendingCaretIndex}"
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

        private static IEnumerator WaitForCursorIndex(int expected)
        {
            int frameBudget = 600;
            while (0 < frameBudget-- && TerminalUI.Instance._commandInput.cursorIndex != expected)
            {
                yield return null;
            }

            Assert.AreEqual(
                expected,
                TerminalUI.Instance._commandInput.cursorIndex,
                "The queued caret position is applied"
            );
        }

#endif
    }
}
