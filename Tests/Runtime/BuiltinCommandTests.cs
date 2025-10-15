namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using LogType = UnityEngine.LogType;

    public sealed class BuiltinCommandTests
    {
        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();
        }

        private static LogItem GetLastLog(CommandLog buffer, Func<LogItem, bool> predicate = null)
        {
            Assert.IsNotNull(buffer, "Command log is not initialized.");
            IReadOnlyList<LogItem> logs = buffer.Logs;
            for (int i = logs.Count - 1; i >= 0; --i)
            {
                LogItem entry = logs[i];
                if (predicate == null || predicate(entry))
                {
                    return entry;
                }
            }

            Assert.Fail(
                predicate == null
                    ? "Expected at least one log entry, but none were recorded."
                    : "Expected matching log entry but none were recorded."
            );
            return default;
        }

        private static IEnumerator RestartTerminal()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);
        }

        [UnityTest]
        public IEnumerator BuiltInCommandsAreRegistered()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell);

            HashSet<string> expected = new(
                new[]
                {
                    "list-themes",
                    "list-fonts",
                    "set-theme",
                    "set-font",
                    "get-theme",
                    "get-font",
                    "set-random-theme",
                    "set-random-font",
                    "clear-console",
                    "clear-history",
                    "help",
                    "time",
                    "time-scale",
                    "log-terminal",
                    "log",
                    "trace",
                    "clear-variable",
                    "clear-all-variables",
                    "set-variable",
                    "get-variable",
                    "list-variables",
                    "no-op",
                    "quit",
                },
                StringComparer.OrdinalIgnoreCase
            );

            CollectionAssert.IsSubsetOf(expected, shell.Commands.Keys);
            yield break;
        }

        [UnityTest]
        public IEnumerator ClearHistoryCommandAcceptsSpaceSeparatedInput()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;
            CommandHistory history = Terminal.History;
            Assert.IsNotNull(shell);
            Assert.IsNotNull(history);

            Assert.IsTrue(shell.RunCommand("log test-history"));
            Terminal.Buffer?.DrainPending();

            string[] suggestions = Terminal.AutoComplete.Complete("clear history");
            CollectionAssert.Contains(suggestions, "clear-history");

            bool executed = shell.RunCommand("clear history");
            Assert.IsTrue(executed);

            string[] entries = history.GetHistory(false, false).ToArray();
            Assert.IsEmpty(entries);
            yield break;
        }

        [UnityTest]
        public IEnumerator ClearConsoleCommandClearsBuffer()
        {
            yield return RestartTerminal();

            Terminal.Log(TerminalLogType.Message, "one");
            Terminal.Buffer?.DrainPending();
            Assert.Greater(Terminal.Buffer.Logs.Count, 0);

            bool executed = Terminal.Shell.RunCommand("clear-console");
            Assert.IsTrue(executed);

            Terminal.Buffer?.DrainPending();
            Assert.AreEqual(0, Terminal.Buffer.Logs.Count);
            yield break;
        }

        [UnityTest]
        public IEnumerator ClearHistoryCommandPurgesAllLogs()
        {
            yield return RestartTerminal();

            CommandLog buffer = Terminal.Buffer;
            Assert.IsNotNull(buffer);

            buffer.Clear();
            Terminal.Log(TerminalLogType.Input, "alpha");
            Terminal.Log(TerminalLogType.Message, "visible");
            buffer.DrainPending();
            Assert.IsTrue(buffer.Logs.Any(log => log.type == TerminalLogType.Input));
            Assert.IsTrue(buffer.Logs.Any(log => log.type == TerminalLogType.Message));

            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell);
            Assert.IsTrue(shell.RunCommand("clear-history"));

            buffer.DrainPending();
            Assert.AreEqual(0, buffer.Logs.Count);
            yield break;
        }

        [UnityTest]
        public IEnumerator TimeCommandMeasuresNestedExecution()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;
            int initialCount = Terminal.Buffer.Logs.Count;

            bool executed = shell.RunCommand("time log-terminal timed-message");
            Assert.IsTrue(executed);

            Terminal.Buffer?.DrainPending();
            LogItem timeLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.ShellMessage && item.message.StartsWith("Time:")
            );
            StringAssert.StartsWith("Time:", timeLog.message);
            Assert.Greater(Terminal.Buffer.Logs.Count, initialCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator TimeScaleCommandUpdatesTimeScale()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;
            float originalScale = Time.timeScale;

            try
            {
                Assert.IsTrue(shell.RunCommand("time-scale 0.42"));
                Assert.AreEqual(0.42f, Time.timeScale, 0.0001f);
            }
            finally
            {
                Time.timeScale = originalScale;
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator LogCommandsEmitOutput()
        {
            yield return RestartTerminal();

            List<(string message, LogType type)> captured = new();
            Application.logMessageReceived += HandleLog;
            try
            {
                Assert.IsTrue(Terminal.Shell.RunCommand("log-terminal captured"));
                Terminal.Buffer?.DrainPending();
                LogItem bufferLog = GetLastLog(
                    Terminal.Buffer,
                    item => item.type == TerminalLogType.ShellMessage && item.message == "captured"
                );
                Assert.AreEqual("captured", bufferLog.message);

                Assert.IsTrue(Terminal.Shell.RunCommand("log unity-log"));
                Assert.IsTrue(captured.Any(entry => entry.message == "unity-log"));
            }
            finally
            {
                Application.logMessageReceived -= HandleLog;
            }

            yield break;

            void HandleLog(string message, string stackTrace, LogType type)
            {
                captured.Add((message, type));
            }
        }

        [UnityTest]
        public IEnumerator TraceCommandProducesStackTraceWhenAvailable()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;

            Assert.IsTrue(shell.RunCommand("trace"));
            Terminal.Buffer?.DrainPending();
            LogItem warningLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Warning
            );
            StringAssert.Contains("Nothing to trace", warningLog.message);

            Terminal.Log(TerminalLogType.Message, "trace-target");
            Terminal.Buffer?.DrainPending();
            LogItem previousLog = GetLastLog(Terminal.Buffer);
            Assert.IsFalse(string.IsNullOrWhiteSpace(previousLog.stackTrace));

            Assert.IsTrue(shell.RunCommand("trace"));
            Terminal.Buffer?.DrainPending();
            LogItem traceLog = GetLastLog(Terminal.Buffer);
            Assert.AreEqual(previousLog.stackTrace, traceLog.message);
            yield break;
        }

        [UnityTest]
        public IEnumerator VariableCommandsManageLifecycle()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;

            Assert.IsTrue(shell.RunCommand("set-variable foo \"bar baz\""));
            Assert.IsTrue(shell.Variables.ContainsKey("foo"));

            Assert.IsTrue(shell.RunCommand("get-variable foo"));
            Terminal.Buffer?.DrainPending();
            LogItem getLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.ShellMessage
            );
            StringAssert.Contains("bar baz", getLog.message);

            Assert.IsTrue(shell.RunCommand("list-variables"));
            Terminal.Buffer?.DrainPending();
            LogItem listLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.ShellMessage
            );
            StringAssert.Contains("foo", listLog.message);

            Assert.IsTrue(shell.RunCommand("clear-variable foo"));
            Assert.IsFalse(shell.Variables.ContainsKey("foo"));

            Assert.IsTrue(shell.RunCommand("set-variable alpha one"));
            Assert.IsTrue(shell.RunCommand("set-variable beta two"));
            Assert.AreEqual(2, shell.Variables.Count);

            Assert.IsTrue(shell.RunCommand("clear-all-variables"));
            Assert.AreEqual(0, shell.Variables.Count);

            Assert.IsTrue(shell.RunCommand("list-variables"));
            Terminal.Buffer?.DrainPending();
            LogItem emptyLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Warning
            );
            StringAssert.Contains("No variables found", emptyLog.message);
            yield break;
        }

        [UnityTest]
        public IEnumerator ThemeCommandsOperateOnAvailableTheme()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;

            Assert.IsTrue(shell.RunCommand("list-themes"));
            Terminal.Buffer?.DrainPending();
            LogItem listLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Message
            );
            string expectedThemeName = ThemeNameHelper.GetFriendlyThemeName("test-theme");
            StringAssert.Contains(expectedThemeName, listLog.message);

            Assert.IsTrue(shell.RunCommand("get-theme"));
            Terminal.Buffer?.DrainPending();
            LogItem getLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Message
            );
            StringAssert.Contains("Current terminal theme", getLog.message);

            Assert.IsTrue(shell.RunCommand("set-theme test-theme"));
            Terminal.Buffer?.DrainPending();
            LogItem setLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Message
            );
            StringAssert.Contains("test-theme", setLog.message);

            Assert.IsTrue(shell.RunCommand("set-random-theme"));
            Terminal.Buffer?.DrainPending();
            LogItem randomLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Message
            );
            StringAssert.Contains("set theme", randomLog.message);
            yield break;
        }

        [UnityTest]
        public IEnumerator FontCommandsHandleMissingFonts()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;

            Assert.IsTrue(shell.RunCommand("list-fonts"));
            Terminal.Buffer?.DrainPending();
            LogItem listLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Message
            );
            Assert.IsNotNull(listLog);

            Assert.IsTrue(shell.RunCommand("get-font"));
            Terminal.Buffer?.DrainPending();
            LogItem getLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Message
            );
            StringAssert.Contains("null", getLog.message);

            Assert.IsTrue(shell.RunCommand("set-font missing-font"));
            Terminal.Buffer?.DrainPending();
            LogItem warningLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Warning
            );
            StringAssert.Contains("not found", warningLog.message);

            Assert.IsTrue(shell.RunCommand("set-random-font"));
            Terminal.Buffer?.DrainPending();
            LogItem randomLog = GetLastLog(
                Terminal.Buffer,
                item => item.type == TerminalLogType.Warning
            );
            StringAssert.Contains("No fonts available", randomLog.message);
            yield break;
        }

        [UnityTest]
        public IEnumerator HelpCommandProvidesCommandDetails()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;

            int initialCount = Terminal.Buffer.Logs.Count;
            Assert.IsTrue(shell.RunCommand("help"));
            Terminal.Buffer?.DrainPending();
            bool hasUsage = Terminal
                .Buffer.Logs.Skip(initialCount)
                .Any(item =>
                    item.type == TerminalLogType.ShellMessage && item.message.Contains("Usage:")
                );
            Assert.IsTrue(hasUsage);

            initialCount = Terminal.Buffer.Logs.Count;
            Assert.IsTrue(shell.RunCommand("help clear-history"));
            Terminal.Buffer?.DrainPending();
            LogItem specificLog = GetLastLog(
                Terminal.Buffer,
                item =>
                    item.type == TerminalLogType.ShellMessage
                    && item.message.Contains("clear-history")
            );
            StringAssert.Contains("clear-history", specificLog.message);
            yield break;
        }

        [UnityTest]
        public IEnumerator NoOpCommandPersistsInHistory()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;
            CommandHistory history = Terminal.History;

            Assert.IsTrue(shell.RunCommand("no-op"));
            string[] entries = history.GetHistory(false, false).ToArray();
            CollectionAssert.Contains(entries, "no-op");
            yield break;
        }

        [UnityTest]
        public IEnumerator QuitCommandIsDiscoverable()
        {
            yield return RestartTerminal();

            CommandShell shell = Terminal.Shell;
            Assert.IsTrue(shell.Commands.ContainsKey("quit"));
            yield break;
        }
    }
}
