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
        private TerminalCommandProfile _commandProfileUnderTest;
        private TerminalAppearanceProfile _appearanceProfileUnderTest;
        private TerminalConfigurationAsset _configurationAssetUnderTest;

        private sealed class CapturingRuntimeFactory : ITerminalRuntimeFactory
        {
            internal ITerminalSettingsProvider CapturedProvider { get; private set; }

            private readonly ITerminalRuntimeFactory _innerFactory;

            internal CapturingRuntimeFactory()
            {
                _innerFactory = new TerminalRuntimeFactory();
            }

            public ITerminalRuntime CreateRuntime(ITerminalSettingsProvider settingsProvider)
            {
                CapturedProvider = settingsProvider;
                return _innerFactory.CreateRuntime(settingsProvider);
            }
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();
            if (_runtimeProfileUnderTest != null)
            {
                ScriptableObject.DestroyImmediate(_runtimeProfileUnderTest);
                _runtimeProfileUnderTest = null;
            }
            if (_appearanceProfileUnderTest != null)
            {
                ScriptableObject.DestroyImmediate(_appearanceProfileUnderTest);
                _appearanceProfileUnderTest = null;
            }
            if (_commandProfileUnderTest != null)
            {
                ScriptableObject.DestroyImmediate(_commandProfileUnderTest);
                _commandProfileUnderTest = null;
            }
            if (_configurationAssetUnderTest != null)
            {
                ScriptableObject.DestroyImmediate(_configurationAssetUnderTest);
                _configurationAssetUnderTest = null;
            }
        }

        [UnityTest]
        public IEnumerator ToggleResetsState()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            CommandShell shell = TestRuntimeScope.Shell;
            Dictionary<string, CommandInfo> shellCommands = shell.Commands.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
            );
            CommandHistory history = TestRuntimeScope.History;
            CommandLog buffer = TestRuntimeScope.Buffer;
            CommandAutoComplete autoComplete = TestRuntimeScope.AutoComplete;

            shell.RunCommand("log");

            string[] events = history.GetHistory(onlySuccess: true, onlyErrorFree: true).ToArray();
            Assert.AreNotEqual(0, events.Length);

            terminal.enabled = false;
            terminal.resetStateOnInit = false;
            terminal.ignoreDefaultCommands = !terminal.ignoreDefaultCommands;
            terminal.enabled = true;
            Assert.AreSame(shell, TestRuntimeScope.Shell);
            Assert.AreNotEqual(shellCommands.Count, shell.Commands.Count);
            Assert.AreSame(history, TestRuntimeScope.History);
            string[] currentEvents = history
                .GetHistory(onlySuccess: true, onlyErrorFree: true)
                .ToArray();
            Assert.AreEqual(events.Length, currentEvents.Length);
            for (int i = 0; i < events.Length; ++i)
            {
                Assert.AreEqual(events[i], currentEvents[i], $"History event {i} wasn't the same!");
            }
            Assert.AreSame(buffer, TestRuntimeScope.Buffer);
            Assert.AreSame(autoComplete, TestRuntimeScope.AutoComplete);
        }

        [UnityTest]
        public IEnumerator CleanConstruction()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TerminalUI terminal1 = TerminalUI.Instance;
            Assert.IsNotNull(terminal1);
            CommandShell shell = TestRuntimeScope.Shell;
            Assert.IsNotNull(shell);
            CommandHistory history = TestRuntimeScope.History;
            Assert.IsNotNull(history);
            CommandLog buffer = TestRuntimeScope.Buffer;
            Assert.IsNotNull(buffer);
            CommandAutoComplete autoComplete = TestRuntimeScope.AutoComplete;
            Assert.IsNotNull(autoComplete);

            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: false);

            TerminalUI terminal2 = TerminalUI.Instance;
            Assert.IsNotNull(TerminalUI.Instance);
            Assert.AreNotSame(terminal1, TerminalUI.Instance);
            Assert.AreSame(shell, TestRuntimeScope.Shell);
            Assert.AreSame(history, TestRuntimeScope.History);
            Assert.AreSame(buffer, TestRuntimeScope.Buffer);
            Assert.AreSame(autoComplete, TestRuntimeScope.AutoComplete);

            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            Assert.IsNotNull(TerminalUI.Instance);
            Assert.AreNotSame(terminal2, TerminalUI.Instance);
            Assert.AreNotSame(terminal1, TerminalUI.Instance);
            Assert.AreNotSame(shell, TestRuntimeScope.Shell);
            Assert.IsNotNull(TestRuntimeScope.Shell);
            Assert.AreNotSame(history, TestRuntimeScope.History);
            Assert.IsNotNull(TestRuntimeScope.History);
            Assert.AreNotSame(buffer, TestRuntimeScope.Buffer);
            Assert.IsNotNull(TestRuntimeScope.Buffer);
            Assert.AreNotSame(autoComplete, TestRuntimeScope.AutoComplete);
            Assert.IsNotNull(TestRuntimeScope.AutoComplete);
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
            if (ensureLargeLogBuffer && TestRuntimeScope.Buffer != null)
            {
                TestRuntimeScope.Buffer.Resize(4096);
            }
        }

        [UnityTest]
        public IEnumerator CleanRestartHelperWorks()
        {
            // Start with reset and capture instances
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            CommandShell shell1 = TestRuntimeScope.Shell;
            CommandHistory history1 = TestRuntimeScope.History;
            CommandLog buffer1 = TestRuntimeScope.Buffer;
            CommandAutoComplete auto1 = TestRuntimeScope.AutoComplete;
            Assert.IsNotNull(shell1);
            Assert.IsNotNull(history1);

            // Clean restart without reset should keep instances
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: false);
            Assert.AreSame(shell1, TestRuntimeScope.Shell);
            Assert.AreSame(history1, TestRuntimeScope.History);
            Assert.AreSame(buffer1, TestRuntimeScope.Buffer);
            Assert.AreSame(auto1, TestRuntimeScope.AutoComplete);

            // Clean restart with reset should replace instances
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);
            Assert.AreNotSame(shell1, TestRuntimeScope.Shell);
            Assert.AreNotSame(history1, TestRuntimeScope.History);
            Assert.AreNotSame(buffer1, TestRuntimeScope.Buffer);
            Assert.AreNotSame(auto1, TestRuntimeScope.AutoComplete);
        }

        [UnityTest]
        public IEnumerator AcquireRuntimeUsesConfigurationAssetProvider()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            TerminalConfigurationAsset configurationAsset =
                ScriptableObject.CreateInstance<TerminalConfigurationAsset>();
            _configurationAssetUnderTest = configurationAsset;

            CapturingRuntimeFactory runtimeFactory = new();

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal =>
                {
                    terminal.SetRuntimeFactoryForTests(runtimeFactory);
                    terminal.SetConfigurationAssetForTests(configurationAsset);
                    terminal.SetRuntimeProfileForTests(null);
                },
                ensureLargeLogBuffer: false
            );

            Assert.IsNotNull(runtimeFactory.CapturedProvider);
            Assert.AreSame(configurationAsset, runtimeFactory.CapturedProvider);
        }

        [UnityTest]
        public IEnumerator AcquireRuntimeFallsBackToRuntimeProfileProvider()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            TerminalRuntimeProfile runtimeProfile =
                ScriptableObject.CreateInstance<TerminalRuntimeProfile>();
            _runtimeProfileUnderTest = runtimeProfile;
            runtimeProfile.ConfigureForTests(
                logBufferSize: 32,
                historyBufferSize: 16,
                includeDefaults: true,
                allowedLogTypes: Array.Empty<TerminalLogType>(),
                blockedLogTypes: Array.Empty<TerminalLogType>(),
                allowedCommands: Array.Empty<string>(),
                blockedCommands: Array.Empty<string>()
            );

            CapturingRuntimeFactory runtimeFactory = new();

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal =>
                {
                    terminal.SetRuntimeFactoryForTests(runtimeFactory);
                    terminal.SetConfigurationAssetForTests(null);
                    terminal.SetRuntimeProfileForTests(runtimeProfile);
                },
                ensureLargeLogBuffer: false
            );

            Assert.IsNotNull(runtimeFactory.CapturedProvider);
            Assert.IsInstanceOf<RuntimeProfileSettingsProvider>(runtimeFactory.CapturedProvider);
        }

        [UnityTest]
        public IEnumerator AcquireRuntimeFallsBackToDefaultSettingsProvider()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            CapturingRuntimeFactory runtimeFactory = new();

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal =>
                {
                    terminal.SetRuntimeFactoryForTests(runtimeFactory);
                    terminal.SetConfigurationAssetForTests(null);
                    terminal.SetRuntimeProfileForTests(null);
                },
                ensureLargeLogBuffer: false
            );

            Assert.IsNotNull(runtimeFactory.CapturedProvider);
            Assert.IsInstanceOf<DefaultTerminalSettingsProvider>(runtimeFactory.CapturedProvider);
        }

        [UnityTest]
        public IEnumerator RuntimeModeOptionsApplySelectedConfiguration()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            TerminalRuntimeConfig.SetMode(TerminalRuntimeModeFlags.None);

            TerminalUI.RuntimeModeOption[] options = new TerminalUI.RuntimeModeOption[]
            {
                new()
                {
                    id = "editor",
                    displayName = "Editor Only",
                    modes = TerminalRuntimeModeFlags.Editor,
                },
                new()
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
            TerminalRuntimeProfile profile =
                ScriptableObject.CreateInstance<TerminalRuntimeProfile>();
            _runtimeProfileUnderTest = profile;

            profile.ConfigureForTests(
                logBufferSize: 10,
                historyBufferSize: 5,
                includeDefaults: false,
                allowedLogTypes: Array.Empty<TerminalLogType>(),
                blockedLogTypes: new[] { TerminalLogType.Warning },
                allowedCommands: Array.Empty<string>(),
                blockedCommands: new[] { "clear" }
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

        [UnityTest]
        public IEnumerator AppearanceProfileOverridesSerializedFields()
        {
            TerminalAppearanceProfile profile =
                ScriptableObject.CreateInstance<TerminalAppearanceProfile>();
            _appearanceProfileUnderTest = profile;
            profile.showGUIButtons = false;
            profile.runButtonText = "execute";
            profile.closeButtonText = "dismiss";
            profile.smallButtonText = "mini";
            profile.fullButtonText = "mega";
            profile.launcherButtonText = "apps";
            profile.hintDisplayMode = HintDisplayMode.Always;
            profile.makeHintsClickable = false;
            profile.historyFadeTargets = TerminalHistoryFadeTargets.Launcher;
            profile.cursorBlinkRateMilliseconds = 250;
            profile.logUnityMessages = true;

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.SetAppearanceProfileForTests(profile)
            );

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            Assert.IsFalse(terminal.showGUIButtons);
            Assert.AreEqual("execute", terminal.runButtonText);
            Assert.AreEqual("dismiss", terminal.closeButtonText);
            Assert.AreEqual("mini", terminal.smallButtonText);
            Assert.AreEqual("mega", terminal.fullButtonText);
            Assert.AreEqual("apps", terminal.launcherButtonText);
            Assert.AreEqual(HintDisplayMode.Always, terminal.hintDisplayMode);
            Assert.IsFalse(terminal.makeHintsClickable);
            Assert.AreEqual(
                TerminalHistoryFadeTargets.Launcher,
                TestRuntimeScope.HistoryFadeTargetsForTests
            );
            Assert.AreEqual(250, terminal.CursorBlinkRateForTests);
            Assert.IsTrue(TestRuntimeScope.LogUnityMessagesForTests);

            ScriptableObject.DestroyImmediate(profile);
            _appearanceProfileUnderTest = null;
        }

        [UnityTest]
        public IEnumerator CommandProfileOverridesShellConfiguration()
        {
            TerminalCommandProfile profile =
                ScriptableObject.CreateInstance<TerminalCommandProfile>();
            _commandProfileUnderTest = profile;
            profile.CommandFilters.includeDefaultCommands = false;
            profile.CommandFilters.blockedCommands = new List<string> { "help" };
            profile.LogFilters.blockedLogTypes = new List<TerminalLogType>
            {
                TerminalLogType.Warning,
            };

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.SetCommandProfileForTests(profile)
            );

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            Assert.IsTrue(terminal.ignoreDefaultCommands);
            CollectionAssert.Contains(terminal.BlockedCommandsForTests, "help");
            CollectionAssert.Contains(terminal.BlockedLogTypesForTests, TerminalLogType.Warning);

            CommandShell shell = terminal.Runtime.Shell;
            Assert.IsTrue(shell.IgnoringDefaultCommands);
            CollectionAssert.Contains(shell.IgnoredCommands, "help");

            CommandLog log = terminal.Runtime.Log;
            Assert.IsTrue(log.ignoredLogTypes.Contains(TerminalLogType.Warning));
        }
#endif

        [UnityTest]
        public IEnumerator LauncherHistoryShowsScrollbarAndOpacityGradient()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            yield return SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal =>
                {
                    terminal.disableUIForTests = true;
                }
            );

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            CommandHistory history = terminal.Runtime.History;
            Assert.IsNotNull(history);
            history.Clear();

            const int visibleCount = 3;
            const int totalEntries = visibleCount + 2;
            for (int i = 0; i < totalEntries; ++i)
            {
                history.Push($"command-{i}", success: true, errorFree: true);
            }

            LauncherLayoutMetrics metrics = new(
                width: 640f,
                height: 240f,
                left: 80f,
                top: 120f,
                historyHeight: 180f,
                cornerRadius: 12f,
                insetPadding: 8f,
                historyVisibleEntryCount: visibleCount,
                historyFadeExponent: 2f,
                snapOpen: true,
                animationDuration: 0.05f
            );

            ScrollView logScroll = new();
            ScrollView autoComplete = new(ScrollViewMode.Horizontal);
            VisualElement terminalContainer = new();
            VisualElement inputContainer = new();

            terminal.InjectLayoutElementsForTests(
                terminalContainer,
                inputContainer,
                autoComplete,
                logScroll
            );
            terminal.SetLauncherMetricsForTests(metrics);
            terminal.SetWindowHeightsForTests(metrics.Height, metrics.Height);

            terminal.SetState(TerminalState.OpenLauncher);
            terminal.RefreshUIForTests();
            terminal.RefreshLauncherHistoryForTests();
            terminal.RefreshUIForTests();

            Assert.That(logScroll.verticalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Auto));

            Scroller scroller = logScroll.verticalScroller;
            Assert.IsNotNull(scroller);
            Assert.That(scroller.highValue, Is.GreaterThan(0.01f));
            Assert.That(scroller.resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None));

            VisualElement content = logScroll.contentContainer;
            Assert.IsNotNull(content);
            Assert.That(content.childCount, Is.EqualTo(totalEntries));

            Label newest = content[0] as Label;
            Assert.IsNotNull(newest);
            Assert.That(newest!.text, Is.EqualTo($"command-{totalEntries - 1}"));
            Assert.That(newest.style.opacity.value, Is.EqualTo(1f).Within(0.001f));

            Label middle = content[1] as Label;
            Assert.IsNotNull(middle);
            Assert.That(
                middle!.style.opacity.value,
                Is.LessThan(newest.style.opacity.value)
                    .And.GreaterThan(terminal.LauncherFadeMinimumForTests)
            );

            Label oldest = content[totalEntries - 1] as Label;
            Assert.IsNotNull(oldest);
            Assert.That(
                oldest!.style.opacity.value,
                Is.EqualTo(terminal.LauncherFadeMinimumForTests).Within(0.001f)
            );

            yield return TestSceneHelpers.DestroyTerminalAndWait();
        }

        [UnityTest]
        public IEnumerator RuntimeModeOptionsFallbackToFirstWhenSelectionMissing()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            TerminalRuntimeConfig.SetMode(TerminalRuntimeModeFlags.None);

            TerminalUI.RuntimeModeOption[] options = new TerminalUI.RuntimeModeOption[]
            {
                new()
                {
                    id = "development",
                    displayName = "Development",
                    modes = TerminalRuntimeModeFlags.Development,
                },
                new()
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
                new()
                {
                    id = "editor",
                    displayName = "Editor",
                    modes = TerminalRuntimeModeFlags.Editor,
                },
                new()
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
