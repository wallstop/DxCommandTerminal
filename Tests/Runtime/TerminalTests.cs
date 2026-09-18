namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Linq;
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

    public sealed class TerminalTests
    {
        /*
           Dynamically derived from RegisteredCommands so the test stays in sync
           with any additions/removals of default commands in BuiltinCommands.cs.
        */
        private static readonly string[] KnownDefaultCommands = CommandShell
            .RegisteredCommands.Value.Where(tuple => tuple.attribute.Default)
            .Select(tuple => tuple.attribute.Name)
            .ToArray();

        internal static IEnumerator SpawnTerminal(bool resetStateOnInit)
        {
            return SpawnTerminal(resetStateOnInit, ignoreDefaultCommands: false);
        }

        internal static IEnumerator SpawnTerminal(bool resetStateOnInit, bool ignoreDefaultCommands)
        {
            GameObject go = new("Terminal", typeof(StartTracker), typeof(TerminalUI));
            TerminalUI terminal = go.GetComponent<TerminalUI>();
            terminal.resetStateOnInit = resetStateOnInit;
            terminal.ignoreDefaultCommands = ignoreDefaultCommands;
            StartTracker startTracker = go.GetComponent<StartTracker>();
            yield return new WaitUntil(() => startTracker.Started);
        }

        /*
            A scripted add leaves the serialized _uiDocument null while a
            UIDocument component exists on the GameObject (consumer-reported
            failure mode: a TerminalUI in an empty scene never launches).
            SetupUI must recover through TryGetComponent and build the tree.
         */
        [UnityTest]
        public IEnumerator MissingSerializedDocumentRecoversFromComponent()
        {
            const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
            PanelSettings settings = ScriptableObject.CreateInstance<PanelSettings>();
            GameObject go = new("TerminalUnassignedDoc");
            go.SetActive(false);
            go.AddComponent<UIDocument>().panelSettings = settings;
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.resetStateOnInit = true;
            terminal._themePack = AssetDatabase.LoadAssetAtPath<TerminalThemePack>(
                $"{PackageRoot}/Packs/Themes/Medium.asset"
            );
            terminal._fontPack = AssetDatabase.LoadAssetAtPath<TerminalFontPack>(
                $"{PackageRoot}/Packs/Fonts/Medium.asset"
            );
            go.AddComponent<StartTracker>();

            go.SetActive(true);
            StartTracker tracker = go.GetComponent<StartTracker>();
            yield return new WaitUntil(() => tracker.Started);

            /*
                The tree builds on the first open; Awake's TryGetComponent
                recovery assigns the document before SetupUI can run.
             */
            terminal.SetState(TerminalState.OpenFull);

            Assert.AreEqual(
                1,
                go.GetComponent<UIDocument>().rootVisualElement.childCount,
                "SetupUI must recover via TryGetComponent and build the terminal tree"
            );
            Assert.IsNotNull(terminal._commandInput, "The command input must exist after recovery");

            UnityEngine.Object.Destroy(settings);
        }

        [TearDown]
        public void TearDown()
        {
            if (TerminalUI.Instance != null)
            {
                UnityEngine.Object.Destroy(TerminalUI.Instance.gameObject);
            }
        }

        [TestCase(LogType.Error, TerminalLogType.Error, 0)]
        [TestCase(LogType.Assert, TerminalLogType.Assert, 1)]
        [TestCase(LogType.Warning, TerminalLogType.Warning, 2)]
        [TestCase(LogType.Log, TerminalLogType.Message, 3)]
        [TestCase(LogType.Exception, TerminalLogType.Exception, 4)]
        public void UnityLogOrdinalsRemainCompatible(
            LogType unityType,
            TerminalLogType terminalType,
            int ordinal
        )
        {
            Assert.AreEqual(ordinal, (int)terminalType);
            Assert.AreEqual(terminalType, (TerminalLogType)unityType);
            CommandLog log = new(4, new[] { terminalType });
            Assert.IsFalse(log.HandleLog("ignored", string.Empty, (TerminalLogType)unityType));
            Assert.IsEmpty(log.Logs);
        }

        [TestCase(TerminalLogType.Input, 5)]
        [TestCase(TerminalLogType.ShellMessage, 6)]
        public void TerminalOnlyLogOrdinalsRemainCompatible(TerminalLogType type, int ordinal)
        {
            Assert.AreEqual(ordinal, (int)type);
        }

        [UnityTest]
        public IEnumerator ToggleResetsState()
        {
            yield return SpawnTerminal(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            CommandShell shell = Terminal.Shell;
            CommandHistory history = Terminal.History;
            CommandLog buffer = Terminal.Buffer;
            CommandAutoComplete autoComplete = Terminal.AutoComplete;

            shell.RunCommand("log");

            string[] events = history.GetHistory(onlySuccess: true, onlyErrorFree: true).ToArray();
            Assert.AreNotEqual(
                0,
                events.Length,
                "Expected at least one history event after running 'log'"
            );

            terminal.enabled = false;
            terminal.resetStateOnInit = false;
            terminal.ignoreDefaultCommands = !terminal.ignoreDefaultCommands;
            terminal.enabled = true;
            Assert.AreSame(
                shell,
                Terminal.Shell,
                "Shell instance should be reused when resetStateOnInit is false"
            );
            Assert.IsNotEmpty(
                KnownDefaultCommands,
                "Sanity: expected at least one default command to be registered via [RegisterCommand(isDefault: true)]"
            );
            foreach (string command in KnownDefaultCommands)
            {
                Assert.AreNotEqual(
                    terminal.ignoreDefaultCommands,
                    shell.Commands.ContainsKey(command),
                    $"Default command '{command}' should {(terminal.ignoreDefaultCommands ? "not be" : "be")} registered when ignoreDefaultCommands={terminal.ignoreDefaultCommands}"
                );
            }
            Assert.AreEqual(
                terminal.ignoreDefaultCommands,
                shell.IgnoringDefaultCommands,
                "Shell.IgnoringDefaultCommands should match terminal.ignoreDefaultCommands"
            );
            Assert.AreSame(
                history,
                Terminal.History,
                "History instance should be reused when resetStateOnInit is false"
            );
            string[] currentEvents = history
                .GetHistory(onlySuccess: true, onlyErrorFree: true)
                .ToArray();
            Assert.AreEqual(
                events.Length,
                currentEvents.Length,
                "History length should be preserved after toggling ignoreDefaultCommands"
            );
            for (int i = 0; i < events.Length; ++i)
            {
                Assert.AreEqual(events[i], currentEvents[i], $"History event {i} wasn't the same!");
            }
            Assert.AreSame(
                buffer,
                Terminal.Buffer,
                "Buffer instance should be reused when resetStateOnInit is false"
            );
            Assert.AreSame(
                autoComplete,
                Terminal.AutoComplete,
                "AutoComplete instance should be reused when resetStateOnInit is false"
            );
        }

        [UnityTest]
        public IEnumerator CleanConstruction()
        {
            yield return SpawnTerminal(resetStateOnInit: true);

            TerminalUI terminal1 = TerminalUI.Instance;
            Assert.That(
                terminal1 != null,
                "TerminalUI.Instance should not be null after SpawnTerminal"
            );
            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell, "Terminal.Shell should not be null after SpawnTerminal");
            CommandHistory history = Terminal.History;
            Assert.IsNotNull(history, "Terminal.History should not be null after SpawnTerminal");
            CommandLog buffer = Terminal.Buffer;
            Assert.IsNotNull(buffer, "Terminal.Buffer should not be null after SpawnTerminal");
            CommandAutoComplete autoComplete = Terminal.AutoComplete;
            Assert.IsNotNull(
                autoComplete,
                "Terminal.AutoComplete should not be null after SpawnTerminal"
            );

            yield return SpawnTerminal(resetStateOnInit: false);

            TerminalUI terminal2 = TerminalUI.Instance;
            Assert.That(
                TerminalUI.Instance != null,
                "TerminalUI.Instance should not be null after second SpawnTerminal"
            );
            Assert.AreNotSame(
                terminal1,
                TerminalUI.Instance,
                "New terminal instance should be created on second SpawnTerminal"
            );
            Assert.AreSame(
                shell,
                Terminal.Shell,
                "Shell should be reused when resetStateOnInit is false"
            );
            Assert.AreSame(
                history,
                Terminal.History,
                "History should be reused when resetStateOnInit is false"
            );
            Assert.AreSame(
                buffer,
                Terminal.Buffer,
                "Buffer should be reused when resetStateOnInit is false"
            );
            Assert.AreSame(
                autoComplete,
                Terminal.AutoComplete,
                "AutoComplete should be reused when resetStateOnInit is false"
            );

            yield return SpawnTerminal(resetStateOnInit: true);

            Assert.That(
                TerminalUI.Instance != null,
                "TerminalUI.Instance should not be null after third SpawnTerminal"
            );
            Assert.AreNotSame(
                terminal2,
                TerminalUI.Instance,
                "New terminal instance should be created on third SpawnTerminal"
            );
            Assert.AreNotSame(
                terminal1,
                TerminalUI.Instance,
                "Third terminal should differ from first terminal"
            );
            Assert.AreNotSame(
                shell,
                Terminal.Shell,
                "Shell should be recreated when resetStateOnInit is true"
            );
            Assert.IsNotNull(Terminal.Shell, "Terminal.Shell should not be null after reset");
            Assert.AreNotSame(
                history,
                Terminal.History,
                "History should be recreated when resetStateOnInit is true"
            );
            Assert.IsNotNull(Terminal.History, "Terminal.History should not be null after reset");
            Assert.AreNotSame(
                buffer,
                Terminal.Buffer,
                "Buffer should be recreated when resetStateOnInit is true"
            );
            Assert.IsNotNull(Terminal.Buffer, "Terminal.Buffer should not be null after reset");
            Assert.AreNotSame(
                autoComplete,
                Terminal.AutoComplete,
                "AutoComplete should be recreated when resetStateOnInit is true"
            );
            Assert.IsNotNull(
                Terminal.AutoComplete,
                "Terminal.AutoComplete should not be null after reset"
            );
        }

        [UnityTest]
        public IEnumerator IgnoreDefaultCommandsExcludesDefaults()
        {
            yield return SpawnTerminal(resetStateOnInit: true, ignoreDefaultCommands: true);

            Assert.IsNotEmpty(
                KnownDefaultCommands,
                "Sanity: expected at least one default command to be registered via [RegisterCommand(isDefault: true)]"
            );
            CommandShell shell = Terminal.Shell;
            Assert.IsTrue(
                shell.IgnoringDefaultCommands,
                "Shell.IgnoringDefaultCommands should be true when ignoreDefaultCommands is true"
            );
            foreach (string command in KnownDefaultCommands)
            {
                Assert.IsFalse(
                    shell.Commands.ContainsKey(command),
                    $"Default command '{command}' should not be registered when ignoreDefaultCommands is true"
                );
            }
        }

        [UnityTest]
        public IEnumerator IncludeDefaultCommandsRegistersDefaults()
        {
            yield return SpawnTerminal(resetStateOnInit: true, ignoreDefaultCommands: false);

            Assert.IsNotEmpty(
                KnownDefaultCommands,
                "Sanity: expected at least one default command to be registered via [RegisterCommand(isDefault: true)]"
            );
            CommandShell shell = Terminal.Shell;
            Assert.IsFalse(
                shell.IgnoringDefaultCommands,
                "Shell.IgnoringDefaultCommands should be false when ignoreDefaultCommands is false"
            );
            foreach (string command in KnownDefaultCommands)
            {
                Assert.IsTrue(
                    shell.Commands.ContainsKey(command),
                    $"Default command '{command}' should be registered when ignoreDefaultCommands is false"
                );
            }
        }

        [UnityTest]
        public IEnumerator TerminalUIDefersAutoCommandRegistrationUntilFirstUse()
        {
            yield return SpawnTerminal(resetStateOnInit: true);

            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell, "Terminal.Shell should not be null after SpawnTerminal");
            Assert.IsFalse(
                shell.AutoCommandsRegistered,
                "Terminal enabling must not register auto commands; registration is "
                    + "deferred to the first command request"
            );
            Assert.IsEmpty(
                shell.AutoRegisteredCommands,
                "Auto commands must stay unregistered before the first use"
            );

            /*
               Pick a zero-argument auto command that is safe to run inside a
               test session (never quit/exit).
            */
            string knownCommand = CommandShell
                .RegisteredCommands.Value.Where(tuple =>
                    tuple.attribute.MinArgCount == 0
                    && tuple.attribute.MaxArgCount == 0
                    && !tuple.attribute.Name.Equals("quit", StringComparison.OrdinalIgnoreCase)
                )
                .Select(tuple => tuple.attribute.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .First();

            Assert.IsTrue(
                shell.RunCommand(knownCommand),
                $"First command request '{knownCommand}' should apply the deferred "
                    + "registration and run"
            );
            Assert.IsTrue(
                shell.AutoCommandsRegistered,
                "The first command request should complete the deferred registration"
            );
            Assert.IsNotEmpty(
                shell.AutoRegisteredCommands,
                "Deferred registration should surface auto commands on first use"
            );
        }
    }
}
