namespace DxCommandTerminal.Tests.Tests.Runtime
{
    using System.Collections;
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
            if (Terminal.Instance != null)
            {
                Object.Destroy(Terminal.Instance.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator CleanConstruction()
        {
            yield return SpawnTerminal(resetStateOnInit: true);

            Terminal terminal1 = Terminal.Instance;
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

            Terminal terminal2 = Terminal.Instance;
            Assert.IsNotNull(Terminal.Instance);
            Assert.AreNotSame(terminal1, Terminal.Instance);
            Assert.AreSame(shell, Terminal.Shell);
            Assert.AreSame(history, Terminal.History);
            Assert.AreSame(buffer, Terminal.Buffer);
            Assert.AreSame(autoComplete, Terminal.AutoComplete);

            yield return SpawnTerminal(resetStateOnInit: true);

            Assert.IsNotNull(Terminal.Instance);
            Assert.AreNotSame(terminal2, Terminal.Instance);
            Assert.AreNotSame(terminal1, Terminal.Instance);
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
            GameObject go = new("Terminal", typeof(StartTracker), typeof(Terminal));
            Terminal terminal = go.GetComponent<Terminal>();
            terminal.resetStateOnInit = resetStateOnInit;
            StartTracker startTracker = go.GetComponent<StartTracker>();
            yield return new WaitUntil(() => startTracker.Started);
        }
    }
}
