namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Linq;
    using System.Text;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using Application = UnityEngine.Device.Application;

    public sealed class CommandShellTests
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
        public IEnumerator UnescapedQuotes()
        {
            yield return TerminalTests.SpawnTerminal(resetStateOnInit: true);

            int logCount = 0;
            Exception exception = null;
            Action<string> assertion = null;

            Application.logMessageReceived += HandleMessageReceived;
            try
            {
                CommandShell shell = Terminal.Shell;
                Assert.IsNotNull(shell);
                CommandHistory history = Terminal.History;
                Assert.IsNotNull(history);

                int expectedLogCount = 0;
                assertion = message => Assert.AreEqual(string.Empty, message);
                string command = "log '             ";
                shell.RunCommand(command);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsNull(exception, $"Error running {command}: {exception}");
                string[] logs = history.GetHistory(true, true).ToArray();
                Assert.AreEqual(
                    expectedLogCount,
                    logs.Length,
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );

                string expected = "' abd      \"   ";
                assertion = message => Assert.AreEqual(expected.Substring(1), message);
                command = "log " + expected;
                shell.RunCommand(command);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsNull(exception, $"Error running {command}: {exception}");
                logs = history.GetHistory(true, true).ToArray();
                Assert.AreEqual(
                    expectedLogCount,
                    logs.Length,
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log " + expected),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
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
                    assertion?.Invoke(message);
                }
                catch (Exception e)
                {
                    exception = e;
                    throw;
                }
            }
        }

        [UnityTest]
        public IEnumerator RunCommandLineNominal()
        {
            yield return TerminalTests.SpawnTerminal(resetStateOnInit: true);

            int logCount = 0;
            Exception exception = null;
            Action<string> assertion = null;

            Application.logMessageReceived += HandleMessageReceived;
            try
            {
                CommandShell shell = Terminal.Shell;
                Assert.IsNotNull(shell);
                CommandHistory history = Terminal.History;
                Assert.IsNotNull(history);

                int expectedLogCount = 0;
                assertion = message => Assert.AreEqual(string.Empty, message);
                string command = "log";
                shell.RunCommand(command);
                Assert.IsNull(exception, $"Error running {command}: {exception}");
                Assert.AreEqual(++expectedLogCount, logCount);
                string[] logs = history.GetHistory(true, true).ToArray();
                Assert.AreEqual(
                    expectedLogCount,
                    logs.Length,
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );

                assertion = message => Assert.AreEqual("test", message);
                command = "log test";
                shell.RunCommand(command);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsNull(exception, $"Error running {command}: {exception}");
                logs = history.GetHistory(true, true).ToArray();
                Assert.AreEqual(
                    expectedLogCount,
                    logs.Length,
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log test"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );

                assertion = message => Assert.AreEqual("quoted argument", message);
                command = "log \"quoted argument\"";
                shell.RunCommand(command);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsNull(exception, $"Error running {command}: {exception}");
                logs = history.GetHistory(true, true).ToArray();
                Assert.AreEqual(
                    expectedLogCount,
                    logs.Length,
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log test"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log \"quoted argument\""),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );

                assertion = message => Assert.AreEqual("multi argument", message);
                command = "log multi argument";
                shell.RunCommand("log multi argument");
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsNull(exception, $"Error running {command}: {exception}");
                logs = history.GetHistory(true, true).ToArray();
                Assert.AreEqual(
                    expectedLogCount,
                    logs.Length,
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log test"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log \"quoted argument\""),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log multi argument"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );

                assertion = message => Assert.AreEqual("a a a a a a aaaa aa aaa d", message);
                command = "log a a a a a a aaaa aa aaa d";
                shell.RunCommand(command);
                Assert.AreEqual(++expectedLogCount, logCount);
                Assert.IsNull(exception, $"Error running {command}: {exception}");
                logs = history.GetHistory(true, true).ToArray();
                Assert.AreEqual(
                    expectedLogCount,
                    logs.Length,
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log test"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log \"quoted argument\""),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );
                Assert.IsTrue(
                    logs.Contains("log a a a a a a aaaa aa aaa d"),
                    $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                );

                char[] quotes = CommandArg.Quotes.ToArray();
                CommandArg.Quotes.Clear();
                CommandArg.Quotes.Add('"');
                CommandArg.Quotes.Add('\'');
                try
                {
                    int simpleCommandCount = 1;
                    foreach (char quote in CommandArg.Quotes)
                    {
                        string expected =
                            $"{quote}     {quote} {quote} aa bbb ccc  {quote} {quote}{Environment.NewLine}abd {Environment.NewLine} \r \t \n__{quote}";
                        foreach (char otherQuote in CommandArg.Quotes.Except(new[] { quote }))
                        {
                            expected += $" {quote}  {otherQuote}ab {quote}";
                        }

                        expected += $" {quote}{quote} final string";
                        assertion = message =>
                            Assert.AreEqual(
                                expected.Replace(quote.ToString(), string.Empty),
                                message
                            );
                        command = "log " + expected;
                        shell.RunCommand(command);
                        Assert.AreEqual(++expectedLogCount, logCount);
                        Assert.IsNull(exception, $"Error running {command}: {exception}");
                        logs = history.GetHistory(true, true).ToArray();
                        Assert.AreEqual(
                            expectedLogCount,
                            logs.Length,
                            $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                        );
                        Assert.IsTrue(
                            logs.Contains(command),
                            $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                        );

                        expected = $"{quote}{quote}";
                        assertion = message =>
                            Assert.AreEqual(
                                expected.Replace(quote.ToString(), string.Empty),
                                message
                            );
                        command = "log " + expected;
                        shell.RunCommand(command);
                        Assert.AreEqual(++expectedLogCount, logCount);
                        Assert.IsNull(exception, $"Error running {command}: {exception}");
                        logs = history.GetHistory(true, true).ToArray();
                        Assert.AreEqual(
                            expectedLogCount,
                            logs.Length,
                            $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                        );
                        Assert.IsTrue(
                            logs.Contains(command),
                            $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                        );

                        expected = $"{quote}";
                        assertion = message => Assert.AreEqual(string.Empty, message);
                        command = "log " + expected;
                        shell.RunCommand(command);
                        Assert.AreEqual(++expectedLogCount, logCount);
                        Assert.IsNull(exception, $"Error running {command}: {exception}");
                        logs = history.GetHistory(true, true).ToArray();
                        Assert.AreEqual(
                            expectedLogCount,
                            logs.Length,
                            $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                        );
                        Assert.AreEqual(
                            ++simpleCommandCount,
                            logs.Count(message => string.Equals(message, "log")),
                            $"Unexpected logs:{Environment.NewLine}{string.Join(Environment.NewLine, logs)}"
                        );
                    }
                }
                finally
                {
                    CommandArg.Quotes.Clear();
                    CommandArg.Quotes.AddRange(quotes);
                }

                StringBuilder errorBuilder = new();
                bool anyError = false;
                bool initialHadError = Terminal.Shell.HasErrors;
                while (Terminal.Shell.TryConsumeErrorMessage(out string errorMessage))
                {
                    anyError = true;
                    errorBuilder.AppendLine(errorMessage);
                }

                Assert.IsFalse(anyError, errorBuilder.ToString());
                Assert.IsFalse(
                    initialHadError,
                    "Shell reported errors, but was unable to consume error messages!"
                );
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
                    assertion?.Invoke(message);
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
