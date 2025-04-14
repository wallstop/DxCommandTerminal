namespace DxCommandTerminal.Tests.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using CommandTerminal;
    using Components;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class TerminalTests
    {
        [TearDown]
        public void TearDown()
        {
            if (ImGuiTerminal.Instance != null)
            {
                Object.Destroy(ImGuiTerminal.Instance.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator ToggleResetsState()
        {
            yield return SpawnTerminal(resetStateOnInit: true);

            ImGuiTerminal terminal = ImGuiTerminal.Instance;
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
            yield return SpawnTerminal(resetStateOnInit: true);

            ImGuiTerminal terminal1 = ImGuiTerminal.Instance;
            Assert.IsNotNull(terminal1);
            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell);
            CommandHistory history = Terminal.History;
            Assert.IsNotNull(history);
            CommandLog buffer = Terminal.Buffer;
            Assert.IsNotNull(buffer);
            CommandAutoComplete autoComplete = Terminal.AutoComplete;
            Assert.IsNotNull(autoComplete);

            yield return SpawnTerminal(resetStateOnInit: false);

            ImGuiTerminal terminal2 = ImGuiTerminal.Instance;
            Assert.IsNotNull(ImGuiTerminal.Instance);
            Assert.AreNotSame(terminal1, ImGuiTerminal.Instance);
            Assert.AreSame(shell, Terminal.Shell);
            Assert.AreSame(history, Terminal.History);
            Assert.AreSame(buffer, Terminal.Buffer);
            Assert.AreSame(autoComplete, Terminal.AutoComplete);

            yield return SpawnTerminal(resetStateOnInit: true);

            Assert.IsNotNull(ImGuiTerminal.Instance);
            Assert.AreNotSame(terminal2, ImGuiTerminal.Instance);
            Assert.AreNotSame(terminal1, ImGuiTerminal.Instance);
            Assert.AreNotSame(shell, Terminal.Shell);
            Assert.AreNotSame(shell, Terminal.Shell);
            Assert.IsNotNull(Terminal.Shell);
            Assert.AreNotSame(history, Terminal.History);
            Assert.IsNotNull(Terminal.History);
            Assert.AreNotSame(buffer, Terminal.Buffer);
            Assert.IsNotNull(Terminal.Buffer);
            Assert.AreNotSame(autoComplete, Terminal.AutoComplete);
            Assert.IsNotNull(Terminal.AutoComplete);
        }

        internal static IEnumerator SpawnTerminal(bool resetStateOnInit)
        {
            GameObject go = new("Terminal", typeof(StartTracker), typeof(ImGuiTerminal));
            ImGuiTerminal terminal = go.GetComponent<ImGuiTerminal>();
            terminal.resetStateOnInit = resetStateOnInit;
            StartTracker startTracker = go.GetComponent<StartTracker>();
            yield return new WaitUntil(() => startTracker.Started);
        }
    }
}
