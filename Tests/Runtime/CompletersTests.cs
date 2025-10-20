namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using Backend.Completers;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public sealed class CompletersTests
    {
        [UnityTest]
        public IEnumerator ThemeCompleterReturnsDistinctSortedAndFiltered()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            // Replace packs with custom contents
            TestThemePack themePack = ScriptableObject.CreateInstance<TestThemePack>();
            StyleSheet style = ScriptableObject.CreateInstance<StyleSheet>();
            themePack.Add(style, "theme-Alpha");
            themePack.Add(style, "beta-theme");
            themePack.Add(style, "Gamma");
            TerminalUI.Instance.InjectPacks(
                themePack,
                ScriptableObject.CreateInstance<TestFontPack>()
            );

            ThemeArgumentCompleter completer = new();
            CommandCompletionContext ctx = new CommandCompletionContext(
                "set-theme ",
                "set-theme",
                new List<CommandArg>(),
                "b",
                0,
                TestRuntimeScope.Shell
            );

            List<string> results = new(completer.Complete(ctx));
            // Friendly names should be alpha/beta/gamma, filtering by 'b' -> beta only
            Assert.AreEqual(1, results.Count);
            Assert.AreEqual("beta", results[0]);
        }

        [UnityTest]
        public IEnumerator FontCompleterHandlesDuplicatesAndFiltering()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TestFontPack fontPack = ScriptableObject.CreateInstance<TestFontPack>();
            // Create test fonts with names (Font is a UnityEngine.Object, not a ScriptableObject)
            Font f1 = new Font { name = "Consolas" };
            Font f2 = new Font { name = "Cousine" };
            Font f3 = new Font
            {
                name = "consolas", // duplicate name differing by case
            };

            fontPack.Add(f1);
            fontPack.Add(f2);
            fontPack.Add(f3);

            TerminalUI.Instance.InjectPacks(
                ScriptableObject.CreateInstance<TestThemePack>(),
                fontPack
            );

            FontArgumentCompleter completer = new();
            CommandCompletionContext ctx = new CommandCompletionContext(
                "set-font ",
                "set-font",
                new List<CommandArg>(),
                "co",
                0,
                TestRuntimeScope.Shell
            );

            List<string> results = new(completer.Complete(ctx));
            // Distinct should collapse 'Consolas' duplicates; filter 'co' -> Consolas and Cousine
            CollectionAssert.AreEquivalent(new[] { "Consolas", "Cousine" }, results);
        }
    }
}
