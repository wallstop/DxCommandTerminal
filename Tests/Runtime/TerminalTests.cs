#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using Backend.Profiles;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public sealed class TerminalTests
    {
        private TerminalRuntimeProfile _runtimeProfileUnderTest;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();
            if (_runtimeProfileUnderTest != null)
            {
                ScriptableObject.DestroyImmediate(_runtimeProfileUnderTest);
                _runtimeProfileUnderTest = null;
            }
        }

        [UnityTest]
        public IEnumerator ToggleResetsState()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            CommandShell shell = Terminal.Shell;
            Dictionary<string, CommandInfo> shellCommands = shell.Commands.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
            );
            CommandHistory history = Terminal.History;
            CommandLog buffer = Terminal.Buffer;
            CommandAutoComplete autoComplete = Terminal.AutoComplete;

            shell.RunCommand("log");

            string[] events = history.GetHistory(onlySuccess: true, onlyErrorFree: true).ToArray();
            Assert.AreNotEqual(0, events.Length);

            terminal.enabled = false;
            terminal.resetStateOnInit = false;
            terminal.ignoreDefaultCommands = !terminal.ignoreDefaultCommands;
            terminal.enabled = true;
            Assert.AreSame(shell, Terminal.Shell);
            Assert.AreNotEqual(shellCommands.Count, shell.Commands.Count);
            Assert.AreSame(history, Terminal.History);
            string[] currentEvents = history
                .GetHistory(onlySuccess: true, onlyErrorFree: true)
                .ToArray();
            Assert.AreEqual(events.Length, currentEvents.Length);
            for (int i = 0; i < events.Length; ++i)
            {
                Assert.AreEqual(events[i], currentEvents[i], $"History event {i} wasn't the same!");
            }
            Assert.AreSame(buffer, Terminal.Buffer);
            Assert.AreSame(autoComplete, Terminal.AutoComplete);
        }

        [UnityTest]
        public IEnumerator CleanConstruction()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TerminalUI terminal1 = TerminalUI.Instance;
            Assert.IsNotNull(terminal1);
            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell);
            CommandHistory history = Terminal.History;
            Assert.IsNotNull(history);
            CommandLog buffer = Terminal.Buffer;
            Assert.IsNotNull(buffer);
            CommandAutoComplete autoComplete = Terminal.AutoComplete;
            Assert.IsNotNull(autoComplete);

            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: false);

            TerminalUI terminal2 = TerminalUI.Instance;
            Assert.IsNotNull(TerminalUI.Instance);
            Assert.AreNotSame(terminal1, TerminalUI.Instance);
            Assert.AreSame(shell, Terminal.Shell);
            Assert.AreSame(history, Terminal.History);
            Assert.AreSame(buffer, Terminal.Buffer);
            Assert.AreSame(autoComplete, Terminal.AutoComplete);

            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            Assert.IsNotNull(TerminalUI.Instance);
            Assert.AreNotSame(terminal2, TerminalUI.Instance);
            Assert.AreNotSame(terminal1, TerminalUI.Instance);
            Assert.AreNotSame(shell, Terminal.Shell);
            Assert.IsNotNull(Terminal.Shell);
            Assert.AreNotSame(history, Terminal.History);
            Assert.IsNotNull(Terminal.History);
            Assert.AreNotSame(buffer, Terminal.Buffer);
            Assert.IsNotNull(Terminal.Buffer);
            Assert.AreNotSame(autoComplete, Terminal.AutoComplete);
            Assert.IsNotNull(Terminal.AutoComplete);
        }

        internal static IEnumerator SpawnTerminal(
            bool resetStateOnInit,
            Action<TerminalUI> configure = null,
            bool ensureLargeLogBuffer = true
        )
        {
            GameObject go = new("Terminal");
            go.SetActive(false);

            // In tests we skip building UI entirely to avoid engine panel updates

            // Create lightweight test packs to avoid warnings
            TestThemePack themePack = ScriptableObject.CreateInstance<TestThemePack>();
            StyleSheet style = ScriptableObject.CreateInstance<StyleSheet>();
            themePack.Add(style, "test-theme");

            TestFontPack fontPack = ScriptableObject.CreateInstance<TestFontPack>();
            // UI is disabled during tests; no need to add a real font asset

            StartTracker startTracker = go.AddComponent<StartTracker>();

            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            terminal.InjectPacks(themePack, fontPack);
            terminal.resetStateOnInit = resetStateOnInit;

            if (configure != null)
            {
                configure(terminal);
            }

            go.SetActive(true);
            yield return new WaitUntil(() => startTracker.Started);
            // Ensure the buffer is large enough for concurrency tests
            if (ensureLargeLogBuffer && Terminal.Buffer != null)
            {
                Terminal.Buffer.Resize(4096);
            }
        }

        [UnityTest]
        public IEnumerator CleanRestartHelperWorks()
        {
            // Start with reset and capture instances
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            CommandShell shell1 = Terminal.Shell;
            CommandHistory history1 = Terminal.History;
            CommandLog buffer1 = Terminal.Buffer;
            CommandAutoComplete auto1 = Terminal.AutoComplete;
            Assert.IsNotNull(shell1);
            Assert.IsNotNull(history1);

            // Clean restart without reset should keep instances
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: false);
            Assert.AreSame(shell1, Terminal.Shell);
            Assert.AreSame(history1, Terminal.History);
            Assert.AreSame(buffer1, Terminal.Buffer);
            Assert.AreSame(auto1, Terminal.AutoComplete);

            // Clean restart with reset should replace instances
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);
            Assert.AreNotSame(shell1, Terminal.Shell);
            Assert.AreNotSame(history1, Terminal.History);
            Assert.AreNotSame(buffer1, Terminal.Buffer);
            Assert.AreNotSame(auto1, Terminal.AutoComplete);
        }

        [UnityTest]
        public IEnumerator RuntimeModeOptionsApplySelectedConfiguration()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            TerminalRuntimeConfig.SetMode(TerminalRuntimeModeFlags.None);

            TerminalUI.RuntimeModeOption[] options = new TerminalUI.RuntimeModeOption[]
            {
                new TerminalUI.RuntimeModeOption
                {
                    id = "editor",
                    displayName = "Editor Only",
                    modes = TerminalRuntimeModeFlags.Editor,
                },
                new TerminalUI.RuntimeModeOption
                {
                    id = "production",
                    displayName = "Production Only",
                    modes = TerminalRuntimeModeFlags.Production,
                },
            };

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.SetRuntimeModeOptions(options, "production")
            );

            TerminalRuntimeModeFlags configuredMode = TerminalRuntimeConfig.GetModeForTests();
            Assert.AreEqual(TerminalRuntimeModeFlags.Production, configuredMode);
        }

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator RuntimeProfileOverridesEmbeddedSettings()
        {
            TerminalRuntimeProfile profile = ScriptableObject.CreateInstance<TerminalRuntimeProfile>();
            _runtimeProfileUnderTest = profile;

            profile.ConfigureForTests(
                logBufferSize: 10,
                historyBufferSize: 5,
                ignoreDefaults: true,
                ignoredLogTypes: new[] { TerminalLogType.Warning },
                disabledCommands: new[] { "clear" }
            );

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.SetRuntimeProfileForTests(profile),
                ensureLargeLogBuffer: false
            );

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            CommandLog log = terminal.Runtime.Log;
            CommandHistory history = terminal.Runtime.History;
            CommandShell shell = terminal.Runtime.Shell;

            Assert.AreEqual(10, log.Capacity);
            Assert.AreEqual(5, history.Capacity);
            Assert.IsTrue(shell.IgnoringDefaultCommands);
            Assert.IsTrue(shell.IgnoredCommands.Contains("clear"));
            Assert.IsTrue(log.ignoredLogTypes.Contains(TerminalLogType.Warning));
        }
#endif

        [UnityTest]
        public IEnumerator RuntimeModeOptionsFallbackToFirstWhenSelectionMissing()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            TerminalRuntimeConfig.SetMode(TerminalRuntimeModeFlags.None);

            TerminalUI.RuntimeModeOption[] options = new TerminalUI.RuntimeModeOption[]
            {
                new TerminalUI.RuntimeModeOption
                {
                    id = "development",
                    displayName = "Development",
                    modes = TerminalRuntimeModeFlags.Development,
                },
                new TerminalUI.RuntimeModeOption
                {
                    id = "production",
                    displayName = "Production",
                    modes = TerminalRuntimeModeFlags.Production,
                },
            };

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.SetRuntimeModeOptions(options, "missing")
            );

            TerminalRuntimeModeFlags configuredMode = TerminalRuntimeConfig.GetModeForTests();
            Assert.AreEqual(TerminalRuntimeModeFlags.Development, configuredMode);
        }

        [UnityTest]
        public IEnumerator TryApplyRuntimeModeChangesActiveSelection()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            TerminalRuntimeConfig.SetMode(TerminalRuntimeModeFlags.None);

            TerminalUI.RuntimeModeOption[] options = new TerminalUI.RuntimeModeOption[]
            {
                new TerminalUI.RuntimeModeOption
                {
                    id = "editor",
                    displayName = "Editor",
                    modes = TerminalRuntimeModeFlags.Editor,
                },
                new TerminalUI.RuntimeModeOption
                {
                    id = "production",
                    displayName = "Production",
                    modes = TerminalRuntimeModeFlags.Production,
                },
            };

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.SetRuntimeModeOptions(options, "editor")
            );

            TerminalRuntimeModeFlags initialMode = TerminalRuntimeConfig.GetModeForTests();
            Assert.AreEqual(TerminalRuntimeModeFlags.Editor, initialMode);

            TerminalUI terminalInstance = TerminalUI.Instance;
            Assert.IsNotNull(terminalInstance);

            bool switched = terminalInstance.TryApplyRuntimeMode("production");
            Assert.IsTrue(switched);

            TerminalRuntimeModeFlags updatedMode = TerminalRuntimeConfig.GetModeForTests();
            Assert.AreEqual(TerminalRuntimeModeFlags.Production, updatedMode);
        }

        // Test-only pack types moved to Components/TestPacks.cs
    }
}
