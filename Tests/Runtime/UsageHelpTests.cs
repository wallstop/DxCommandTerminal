namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class UsageHelpTests
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
        public IEnumerator HelpShowsUsage()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);
            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell);

            // Query help for a known command without hint but known args: time-scale has 1 arg
            bool saw = false;
            Application.logMessageReceived += OnLog;
            try
            {
                shell.RunCommand("help time-scale");
                // let logs flush
                yield return null;
                Assert.IsTrue(saw);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            void OnLog(string message, string stack, LogType type)
            {
                if (
                    message != null
                    && message.ToLowerInvariant().Contains("usage:")
                    && message.ToLowerInvariant().Contains("time-scale")
                )
                {
                    saw = true;
                }
            }
        }
    }
}
