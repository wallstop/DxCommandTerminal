namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Linq;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class TerminalTests
    {
        // Dynamically derived from RegisteredCommands so the test stays in sync
        // with any additions/removals of default commands in BuiltinCommands.cs.
        private static readonly string[] KnownDefaultCommands = CommandShell
            .RegisteredCommands.Value.Where(tuple => tuple.attribute.Default)
            .Select(tuple => tuple.attribute.Name)
            .ToArray();

        [TearDown]
        public void TearDown()
        {
            if (TerminalUI.Instance != null)
            {
                Object.Destroy(TerminalUI.Instance.gameObject);
            }
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
            LogAssert.Expect(LogType.Error, "No UIDocument assigned, cannot setup UI.");
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
            Assert.IsNotNull(
                terminal1,
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
            Assert.IsNotNull(
                TerminalUI.Instance,
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

            Assert.IsNotNull(
                TerminalUI.Instance,
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

        internal static IEnumerator SpawnTerminal(bool resetStateOnInit)
        {
            return SpawnTerminal(resetStateOnInit, ignoreDefaultCommands: false);
        }

        internal static IEnumerator SpawnTerminal(bool resetStateOnInit, bool ignoreDefaultCommands)
        {
            LogAssert.Expect(LogType.Error, "No UIDocument assigned, cannot setup UI.");
            GameObject go = new("Terminal", typeof(StartTracker), typeof(TerminalUI));
            TerminalUI terminal = go.GetComponent<TerminalUI>();
            terminal.resetStateOnInit = resetStateOnInit;
            terminal.ignoreDefaultCommands = ignoreDefaultCommands;
            StartTracker startTracker = go.GetComponent<StartTracker>();
            yield return new WaitUntil(() => startTracker.Started);
        }
    }
}
