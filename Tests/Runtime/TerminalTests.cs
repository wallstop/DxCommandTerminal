namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class TerminalTests
    {
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
            Dictionary<string, CommandInfo> shellCommands = shell.Commands.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value
            );
            CommandHistory history = Terminal.History;
            CommandLog buffer = Terminal.Buffer;
            CommandAutoComplete autoComplete = Terminal.AutoComplete;

            shell.RunCommand("log");

            string[] events = history.GetHistory(onlySuccess: true, onlyErrorFree: true).ToArray();
            Assert.AreNotEqual(0, events.Length, "Expected at least one history event after running 'log'");

            terminal.enabled = false;
            terminal.resetStateOnInit = false;
            terminal.ignoreDefaultCommands = !terminal.ignoreDefaultCommands;
            LogAssert.Expect(LogType.Error, "No UIDocument assigned, cannot setup UI.");
            terminal.enabled = true;
            Assert.AreSame(shell, Terminal.Shell, "Shell instance should be reused when resetStateOnInit is false");
            Assert.AreNotEqual(shellCommands.Count, shell.Commands.Count, $"Command count should change after toggling ignoreDefaultCommands (was {shellCommands.Count}, now {shell.Commands.Count})");
            Assert.IsTrue((shell.Commands.Count < shellCommands.Count) == terminal.ignoreDefaultCommands, $"When ignoreDefaultCommands={terminal.ignoreDefaultCommands}, command count should be {(terminal.ignoreDefaultCommands ? "less" : "more")} (was {shellCommands.Count}, now {shell.Commands.Count})");
            Assert.AreEqual(terminal.ignoreDefaultCommands, shell.IgnoringDefaultCommands, "Shell.IgnoringDefaultCommands should match terminal.ignoreDefaultCommands");
            Assert.AreSame(history, Terminal.History, "History instance should be reused when resetStateOnInit is false");
            string[] currentEvents = history
                .GetHistory(onlySuccess: true, onlyErrorFree: true)
                .ToArray();
            Assert.AreEqual(events.Length, currentEvents.Length, "History length should be preserved after toggling ignoreDefaultCommands");
            for (int i = 0; i < events.Length; ++i)
            {
                Assert.AreEqual(events[i], currentEvents[i], $"History event {i} wasn't the same!");
            }
            Assert.AreSame(buffer, Terminal.Buffer, "Buffer instance should be reused when resetStateOnInit is false");
            Assert.AreSame(autoComplete, Terminal.AutoComplete, "AutoComplete instance should be reused when resetStateOnInit is false");
        }

        [UnityTest]
        public IEnumerator CleanConstruction()
        {
            yield return SpawnTerminal(resetStateOnInit: true);

            TerminalUI terminal1 = TerminalUI.Instance;
            Assert.IsNotNull(terminal1, "TerminalUI.Instance should not be null after SpawnTerminal");
            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell, "Terminal.Shell should not be null after SpawnTerminal");
            CommandHistory history = Terminal.History;
            Assert.IsNotNull(history, "Terminal.History should not be null after SpawnTerminal");
            CommandLog buffer = Terminal.Buffer;
            Assert.IsNotNull(buffer, "Terminal.Buffer should not be null after SpawnTerminal");
            CommandAutoComplete autoComplete = Terminal.AutoComplete;
            Assert.IsNotNull(autoComplete, "Terminal.AutoComplete should not be null after SpawnTerminal");

            yield return SpawnTerminal(resetStateOnInit: false);

            TerminalUI terminal2 = TerminalUI.Instance;
            Assert.IsNotNull(TerminalUI.Instance, "TerminalUI.Instance should not be null after second SpawnTerminal");
            Assert.AreNotSame(terminal1, TerminalUI.Instance, "New terminal instance should be created on second SpawnTerminal");
            Assert.AreSame(shell, Terminal.Shell, "Shell should be reused when resetStateOnInit is false");
            Assert.AreSame(history, Terminal.History, "History should be reused when resetStateOnInit is false");
            Assert.AreSame(buffer, Terminal.Buffer, "Buffer should be reused when resetStateOnInit is false");
            Assert.AreSame(autoComplete, Terminal.AutoComplete, "AutoComplete should be reused when resetStateOnInit is false");

            yield return SpawnTerminal(resetStateOnInit: true);

            Assert.IsNotNull(TerminalUI.Instance, "TerminalUI.Instance should not be null after third SpawnTerminal");
            Assert.AreNotSame(terminal2, TerminalUI.Instance, "New terminal instance should be created on third SpawnTerminal");
            Assert.AreNotSame(terminal1, TerminalUI.Instance, "Third terminal should differ from first terminal");
            Assert.AreNotSame(shell, Terminal.Shell, "Shell should be recreated when resetStateOnInit is true");
            Assert.IsNotNull(Terminal.Shell, "Terminal.Shell should not be null after reset");
            Assert.AreNotSame(history, Terminal.History, "History should be recreated when resetStateOnInit is true");
            Assert.IsNotNull(Terminal.History, "Terminal.History should not be null after reset");
            Assert.AreNotSame(buffer, Terminal.Buffer, "Buffer should be recreated when resetStateOnInit is true");
            Assert.IsNotNull(Terminal.Buffer, "Terminal.Buffer should not be null after reset");
            Assert.AreNotSame(autoComplete, Terminal.AutoComplete, "AutoComplete should be recreated when resetStateOnInit is true");
            Assert.IsNotNull(Terminal.AutoComplete, "Terminal.AutoComplete should not be null after reset");
        }

        [UnityTest]
        public IEnumerator IgnoreDefaultCommandsExcludesDefaults()
        {
            yield return SpawnTerminal(resetStateOnInit: true, ignoreDefaultCommands: true);

            CommandShell shell = Terminal.Shell;
            Assert.IsTrue(shell.IgnoringDefaultCommands, "Shell.IgnoringDefaultCommands should be true when ignoreDefaultCommands is true");
            // All built-in commands use isDefault: true, so none should be registered
            string[] unexpectedCommands = shell.Commands.Keys.ToArray();
            Assert.AreEqual(0, unexpectedCommands.Length, $"Expected no commands when ignoreDefaultCommands is true, but found: {string.Join(", ", unexpectedCommands)}");
        }

        [UnityTest]
        public IEnumerator IncludeDefaultCommandsRegistersDefaults()
        {
            yield return SpawnTerminal(resetStateOnInit: true, ignoreDefaultCommands: false);

            CommandShell shell = Terminal.Shell;
            Assert.IsFalse(shell.IgnoringDefaultCommands, "Shell.IgnoringDefaultCommands should be false when ignoreDefaultCommands is false");
            Assert.IsTrue(shell.Commands.ContainsKey("log"), "Default command 'log' should be registered when ignoreDefaultCommands is false");
            Assert.IsTrue(shell.Commands.ContainsKey("help"), "Default command 'help' should be registered when ignoreDefaultCommands is false");
            Assert.IsTrue(shell.Commands.ContainsKey("quit"), "Default command 'quit' should be registered when ignoreDefaultCommands is false");
            Assert.IsTrue(shell.Commands.ContainsKey("no-op"), "Default command 'no-op' should be registered when ignoreDefaultCommands is false");
            Assert.Less(0, shell.Commands.Count, "Should have at least one command when ignoreDefaultCommands is false");
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
