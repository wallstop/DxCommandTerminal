namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class CommandShellEscapingTests
    {
        [TearDown]
        public void TearDown()
        {
            if (TerminalUI.Instance != null)
            {
                UnityEngine.Object.Destroy(TerminalUI.Instance.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator EscapedQuotesInsideQuotedArgumentAreHandled()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            int logCount = 0;
            Exception exception = null;
            string expected = @"he said: ""hi"" \back\"; // he said: "hi" \\back\\
            string command = "log \"he said: \\\"hi\\\" \\\\back\\\\\"";

            Application.logMessageReceived += HandleMessageReceived;
            try
            {
                CommandShell shell = Terminal.Shell;
                Assert.IsNotNull(shell);
                shell.RunCommand(command);
                Assert.AreEqual(1, logCount);
                Assert.IsNull(exception);
            }
            finally
            {
                Application.logMessageReceived -= HandleMessageReceived;
            }

            yield break;

            void HandleMessageReceived(string message, string stackTrace, LogType type)
            {
                ++logCount;
                try
                {
                    Assert.AreEqual(expected, message);
                }
                catch (Exception e)
                {
                    exception = e;
                    throw;
                }
            }
        }
    }
}
