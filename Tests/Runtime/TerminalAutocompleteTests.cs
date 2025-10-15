namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public sealed class TerminalAutocompleteTests
    {
        private sealed class ThemeCompleter : IArgumentCompleter
        {
            public IEnumerable<string> Complete(CommandCompletionContext context)
            {
                return new[] { "dark", "light" };
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (TerminalUI.Instance != null)
            {
                Object.Destroy(TerminalUI.Instance.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator TabCyclesThroughMultipleSuggestions()
        {
            yield return SetupTerminalWithCustomInput(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            TestTerminalInput input = terminal.GetComponent<TestTerminalInput>();
            Assert.IsNotNull(input);

            Terminal.Shell.AddCommand("cycle-first", _ => { });
            Terminal.Shell.AddCommand("cycle-second", _ => { });

            terminal.SetState(TerminalState.OpenFull);
            yield return null;

            input.CommandText = "cyc";

            terminal.CompleteCommand();
            Assert.AreEqual("cycle-first", input.CommandText);

            terminal.CompleteCommand();
            Assert.AreEqual("cycle-second", input.CommandText);

            terminal.CompleteCommand();
            Assert.AreEqual("cycle-first", input.CommandText);
        }

        [UnityTest]
        public IEnumerator CompleterSuggestionsCycleWithPrefix()
        {
            yield return SetupTerminalWithCustomInput(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            TestTerminalInput input = terminal.GetComponent<TestTerminalInput>();
            Assert.IsNotNull(input);

            CommandShell shell = Terminal.Shell;
            shell.AddCommand(
                "set-theme",
                _ => { },
                0,
                -1,
                string.Empty,
                null,
                new ThemeCompleter()
            );

            terminal.SetState(TerminalState.OpenFull);
            yield return null;

            input.CommandText = "set-theme";

            terminal.CompleteCommand();
            Assert.AreEqual("set-theme dark", input.CommandText);
            Assert.IsTrue(Terminal.AutoComplete.LastCompletionUsedCompleter);
            Assert.AreEqual("set-theme ", Terminal.AutoComplete.LastCompleterPrefix);

            terminal.CompleteCommand();
            Assert.AreEqual("set-theme light", input.CommandText);
            Assert.AreEqual("set-theme ", Terminal.AutoComplete.LastCompleterPrefix);

            terminal.CompleteCommand(searchForward: false);
            Assert.AreEqual("set-theme dark", input.CommandText);
            Assert.AreEqual("set-theme ", Terminal.AutoComplete.LastCompleterPrefix);
        }

        private static IEnumerator SetupTerminalWithCustomInput(bool resetStateOnInit)
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            GameObject go = new("Terminal");
            go.SetActive(false);

            TestThemePack themePack = ScriptableObject.CreateInstance<TestThemePack>();
            StyleSheet style = ScriptableObject.CreateInstance<StyleSheet>();
            themePack.Add(style, "test-theme");

            TestFontPack fontPack = ScriptableObject.CreateInstance<TestFontPack>();

            go.AddComponent<TestTerminalInput>();
            StartTracker startTracker = go.AddComponent<StartTracker>();

            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            terminal.InjectPacks(themePack, fontPack);
            terminal.resetStateOnInit = resetStateOnInit;

            go.SetActive(true);
            yield return new WaitUntil(() => startTracker.Started);
        }
    }
}
