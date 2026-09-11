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
            yield return null;
            Assert.AreEqual(
                "pickup pickaxe",
                _terminal._commandInput.value,
                "The first Tab applies the first provider result to the active token"
            );
            Assert.IsEmpty(
                _terminal._lastCompletionBuffer,
                "A provider answer must suppress stale full-line history hints"
            );
            Assert.AreEqual(
                14,
                _terminal._commandInput.cursorIndex,
                "The caret lands after the inserted token"
            );

            _terminal.CompleteCommand(true);
            yield return null;
            Assert.AreEqual(
                "pickup torch",
                _terminal._commandInput.value,
                "Repeated Tab presses cycle provider results"
            );

            _terminal.CompleteCommand(true);
            yield return null;
            Assert.AreEqual(
                "pickup \"torch pick\"",
                _terminal._commandInput.value,
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
            yield return null;
            Assert.AreEqual(
                "pickup \"torch pick\"",
                _terminal._commandInput.value,
                "Reverse completion starts from the last result"
            );

            _terminal.CompleteCommand(false);
            yield return null;
            Assert.AreEqual("pickup torch", _terminal._commandInput.value);
        }

        [UnityTest]
        public IEnumerator TypingResetsProviderCompletion()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup ", 7);

            _terminal.CompleteCommand(true);
            yield return null;
            Assert.AreEqual("pickup pickaxe", _terminal._commandInput.value);

            // Typing a new token resets the cycling snapshot.
            yield return SetInput("pickup to", 9);

            _terminal.CompleteCommand(true);
            yield return null;
            Assert.AreEqual(
                "pickup torch",
                _terminal._commandInput.value,
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
            yield return null;
            Assert.AreEqual(
                "pickup \"torch",
                _terminal._commandInput.value,
                "Inside an open quote the insertion goes in verbatim"
            );
            Assert.AreEqual(13, _terminal._commandInput.cursorIndex);
        }

        [UnityTest]
        public IEnumerator MidLineInsertionKeepsTrailingText()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup to after", 9);

            _terminal.CompleteCommand(true);
            yield return null;
            Assert.AreEqual(
                "pickup torch after",
                _terminal._commandInput.value,
                "Only the active token is replaced; trailing text is preserved"
            );
            Assert.AreEqual(12, _terminal._commandInput.cursorIndex);
        }

        [UnityTest]
        public IEnumerator UnfocusedFieldKeepsQueuedCompletionCaret()
        {
            yield return SpawnTerminalWithUi();

            Assert.IsTrue(Terminal.Shell.AddCommand(DefinePickupWithStagedInventory()));

            yield return SetInput("pickup ", 7);
            _terminal._textInput.Blur();

            _terminal.CompleteCommand(true);
            yield return null;
            Assert.AreEqual(
                "pickup pickaxe",
                _terminal._commandInput.value,
                "Completion applies to an unfocused field"
            );
            Assert.AreEqual(
                14,
                _terminal._commandInput.cursorIndex,
                "The queued caret survives the focus pass instead of jumping to line end"
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
            yield return null;
            Assert.AreEqual(
                "pickaxe",
                _terminal._commandInput.value,
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
            yield return null;
            Assert.AreEqual(
                "zzz",
                _terminal._commandInput.value,
                "With no suggestions anywhere the input is untouched"
            );

            /*
                'help' has no provider, so the legacy history-based completion
                runs unchanged: 'h' suggests command names that start with it.
             */
            yield return SetInput("h", 1);

            _terminal.CompleteCommand(true);
            yield return null;
            Assert.AreEqual(
                "help",
                _terminal._commandInput.value,
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
            first and places the caret in a second step, with no panel tick
            between the caret placement and the completion request.
         */
        private IEnumerator SetInput(string text, int caretIndex)
        {
            _terminal._commandInput.value = text;
            yield return null;
            _terminal._commandInput.cursorIndex = caretIndex;
            _terminal._commandInput.selectIndex = caretIndex;
        }

#if UNITY_EDITOR
        private static T LoadAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.IsNotNull(asset, $"Expected the test asset at {PackageRoot}/{relativePath}");
            return asset;
        }
#endif
    }
}
