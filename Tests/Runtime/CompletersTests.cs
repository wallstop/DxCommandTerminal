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
            var themePack = ScriptableObject.CreateInstance<TestThemePack>();
            var style = ScriptableObject.CreateInstance<StyleSheet>();
            themePack.Add(style, "theme-Alpha");
            themePack.Add(style, "beta-theme");
            themePack.Add(style, "Gamma");
            TerminalUI.Instance.InjectPacks(
                themePack,
                ScriptableObject.CreateInstance<TestFontPack>()
            );

            ThemeArgumentCompleter completer = new();
            var ctx = new CommandCompletionContext(
                "set-theme ",
                "set-theme",
                new List<CommandArg>(),
                "b",
                0,
                Terminal.Shell
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

            var fontPack = ScriptableObject.CreateInstance<TestFontPack>();
            // Create test fonts with names (Font is a UnityEngine.Object, not a ScriptableObject)
            var f1 = new Font();
            f1.name = "Consolas";
            var f2 = new Font();
            f2.name = "Cousine";
            var f3 = new Font();
            f3.name = "consolas"; // duplicate name differing by case

            fontPack.Add(f1);
            fontPack.Add(f2);
            fontPack.Add(f3);

            TerminalUI.Instance.InjectPacks(
                ScriptableObject.CreateInstance<TestThemePack>(),
                fontPack
            );

            FontArgumentCompleter completer = new();
            var ctx = new CommandCompletionContext(
                "set-font ",
                "set-font",
                new List<CommandArg>(),
                "co",
                0,
                Terminal.Shell
            );

            List<string> results = new(completer.Complete(ctx));
            // Distinct should collapse 'Consolas' duplicates; filter 'co' -> Consolas and Cousine
            CollectionAssert.AreEquivalent(new[] { "Consolas", "Cousine" }, results);
        }
    }
}
