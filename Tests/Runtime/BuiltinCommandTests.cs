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

        private static bool TryFindLogSince(
            CommandLog buffer,
            int startIndex,
            Func<LogItem, bool> predicate,
            out LogItem result
        )
        {
            Assert.IsNotNull(buffer, "Command log is not initialized.");
            IReadOnlyList<LogItem> logs = buffer.Logs;
            int clampedStart = Mathf.Clamp(startIndex, 0, logs.Count);
            for (int i = clampedStart; i < logs.Count; ++i)
            {
                LogItem entry = logs[i];
                if (predicate == null || predicate(entry))
                {
                    result = entry;
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static IEnumerator RestartTerminal()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);
        }

        [UnityTest]
        public IEnumerator BuiltInCommandsAreRegistered()
        {
            yield return RestartTerminal();

            CommandShell shell = TestRuntimeScope.Shell;
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

            CommandShell shell = TestRuntimeScope.Shell;
            CommandHistory history = TestRuntimeScope.History;
            Assert.IsNotNull(shell);
            Assert.IsNotNull(history);

            Assert.IsTrue(shell.RunCommand("log test-history"));
            TestRuntimeScope.Buffer?.DrainPending();

            string[] suggestions = TestRuntimeScope.AutoComplete.Complete("clear history");
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

            TestRuntimeScope.Log(TerminalLogType.Message, "one");
            TestRuntimeScope.Buffer?.DrainPending();
            Assert.Greater(TestRuntimeScope.Buffer.Logs.Count, 0);

            bool executed = TestRuntimeScope.Shell.RunCommand("clear-console");
            Assert.IsTrue(executed);

            TestRuntimeScope.Buffer?.DrainPending();
            Assert.AreEqual(0, TestRuntimeScope.Buffer.Logs.Count);
            yield break;
        }

        [UnityTest]
        public IEnumerator ClearHistoryCommandPurgesAllLogs()
        {
            yield return RestartTerminal();

            CommandLog buffer = TestRuntimeScope.Buffer;
            Assert.IsNotNull(buffer);

            buffer.Clear();
            TestRuntimeScope.Log(TerminalLogType.Input, "alpha");
            TestRuntimeScope.Log(TerminalLogType.Message, "visible");
            buffer.DrainPending();
            Assert.IsTrue(buffer.Logs.Any(log => log.type == TerminalLogType.Input));
            Assert.IsTrue(buffer.Logs.Any(log => log.type == TerminalLogType.Message));

            CommandShell shell = TestRuntimeScope.Shell;
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

            CommandShell shell = TestRuntimeScope.Shell;
            int initialCount = TestRuntimeScope.Buffer.Logs.Count;

            bool executed = shell.RunCommand("time log-terminal timed-message");
            Assert.IsTrue(executed);

            TestRuntimeScope.Buffer?.DrainPending();
            LogItem timeLog = GetLastLog(
                TestRuntimeScope.Buffer,
                item =>
                    item.type == TerminalLogType.ShellMessage && item.message.StartsWith("Time:")
            );
            StringAssert.StartsWith("Time:", timeLog.message);
            Assert.Greater(TestRuntimeScope.Buffer.Logs.Count, initialCount);
            yield break;
        }

        [UnityTest]
        public IEnumerator TimeScaleCommandUpdatesTimeScale()
        {
            yield return RestartTerminal();

            CommandShell shell = TestRuntimeScope.Shell;
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
                Assert.IsTrue(TestRuntimeScope.Shell.RunCommand("log-terminal captured"));
                TestRuntimeScope.Buffer?.DrainPending();
                LogItem bufferLog = GetLastLog(
                    TestRuntimeScope.Buffer,
                    item => item.type == TerminalLogType.ShellMessage && item.message == "captured"
                );
                Assert.AreEqual("captured", bufferLog.message);

                Assert.IsTrue(TestRuntimeScope.Shell.RunCommand("log unity-log"));
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

            CommandShell shell = TestRuntimeScope.Shell;

            Assert.IsTrue(shell.RunCommand("trace"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem warningLog = GetLastLog(
                TestRuntimeScope.Buffer,
                item => item.type == TerminalLogType.Warning
            );
            StringAssert.Contains("Nothing to trace", warningLog.message);

            TestRuntimeScope.Log(TerminalLogType.Message, "trace-target");
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem previousLog = GetLastLog(TestRuntimeScope.Buffer);
            Assert.IsFalse(string.IsNullOrWhiteSpace(previousLog.stackTrace));

            Assert.IsTrue(shell.RunCommand("trace"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem traceLog = GetLastLog(TestRuntimeScope.Buffer);
            Assert.AreEqual(previousLog.stackTrace, traceLog.message);
            yield break;
        }

        [UnityTest]
        public IEnumerator VariableCommandsManageLifecycle()
        {
            yield return RestartTerminal();

            CommandShell shell = TestRuntimeScope.Shell;

            Assert.IsTrue(shell.RunCommand("set-variable foo \"bar baz\""));
            Assert.IsTrue(shell.Variables.ContainsKey("foo"));

            Assert.IsTrue(shell.RunCommand("get-variable foo"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem getLog = GetLastLog(
                TestRuntimeScope.Buffer,
                item => item.type == TerminalLogType.ShellMessage
            );
            StringAssert.Contains("bar baz", getLog.message);

            Assert.IsTrue(shell.RunCommand("list-variables"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem listLog = GetLastLog(
                TestRuntimeScope.Buffer,
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
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem emptyLog = GetLastLog(
                TestRuntimeScope.Buffer,
                item => item.type == TerminalLogType.Warning
            );
            StringAssert.Contains("No variables found", emptyLog.message);
            yield break;
        }

        [UnityTest]
        public IEnumerator ThemeCommandsOperateOnAvailableTheme()
        {
            yield return RestartTerminal();

            CommandShell shell = TestRuntimeScope.Shell;

            Assert.IsTrue(shell.RunCommand("list-themes"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem listLog = GetLastLog(
                TestRuntimeScope.Buffer,
                item => item.type == TerminalLogType.Message
            );
            string expectedThemeName = ThemeNameHelper.GetFriendlyThemeName("test-theme");
            StringAssert.Contains(expectedThemeName, listLog.message);

            Assert.IsTrue(shell.RunCommand("get-theme"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem getLog = GetLastLog(
                TestRuntimeScope.Buffer,
                item => item.type == TerminalLogType.Message
            );
            StringAssert.Contains("Current terminal theme", getLog.message);

            Assert.IsTrue(shell.RunCommand("set-theme test-theme"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem setLog = GetLastLog(
                TestRuntimeScope.Buffer,
                item => item.type == TerminalLogType.Message
            );
            StringAssert.Contains("test-theme", setLog.message);

            Assert.IsTrue(shell.RunCommand("set-random-theme"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem randomLog = GetLastLog(
                TestRuntimeScope.Buffer,
                item => item.type == TerminalLogType.Message
            );
            StringAssert.Contains("set theme", randomLog.message);
            yield break;
        }

        [UnityTest]
        public IEnumerator FontCommandsHandleMissingFonts()
        {
            yield return RestartTerminal();

            CommandShell shell = TestRuntimeScope.Shell;

            Assert.IsTrue(shell.RunCommand("list-fonts"));
            int startIndex = TestRuntimeScope.Buffer.Logs.Count;
            TestRuntimeScope.Buffer?.DrainPending();
            Assert.IsTrue(
                TryFindLogSince(
                    TestRuntimeScope.Buffer,
                    startIndex,
                    item => item.type == TerminalLogType.Message,
                    out LogItem listLog
                ),
                "Expected log entry after list-fonts command."
            );
            Assert.IsNotNull(listLog);

            Assert.IsTrue(shell.RunCommand("get-font"));
            startIndex = TestRuntimeScope.Buffer.Logs.Count;
            TestRuntimeScope.Buffer?.DrainPending();
            Assert.IsTrue(
                TryFindLogSince(
                    TestRuntimeScope.Buffer,
                    startIndex,
                    item => item.type == TerminalLogType.Message,
                    out LogItem getLog
                ),
                "Expected log entry after get-font command."
            );
            StringAssert.Contains("null", getLog.message);

            Assert.IsTrue(shell.RunCommand("set-font missing-font"));
            startIndex = TestRuntimeScope.Buffer.Logs.Count;
            TestRuntimeScope.Buffer?.DrainPending();
            Assert.IsTrue(
                TryFindLogSince(
                    TestRuntimeScope.Buffer,
                    startIndex,
                    item => item.type == TerminalLogType.Warning,
                    out LogItem warningLog
                ),
                "Expected warning log after set-font missing-font command."
            );
            StringAssert.Contains("not found", warningLog.message);

            Assert.IsTrue(shell.RunCommand("set-random-font"));
            TestRuntimeScope.Buffer?.DrainPending();
            bool hasNoFontsWarning = TestRuntimeScope.Buffer.Logs.Any(item =>
                !string.IsNullOrEmpty(item.message)
                && item.message.IndexOf("No fonts available", StringComparison.OrdinalIgnoreCase)
                    >= 0
            );

            Assert.IsTrue(
                hasNoFontsWarning,
                $"Expected 'No fonts available' warning. Logs: {string.Join(" | ", TestRuntimeScope.Buffer.Logs.Select(log => log.message))}"
            );
            yield break;
        }

        [UnityTest]
        public IEnumerator HelpCommandProvidesCommandDetails()
        {
            yield return RestartTerminal();

            CommandShell shell = TestRuntimeScope.Shell;

            int initialCount = TestRuntimeScope.Buffer.Logs.Count;
            Assert.IsTrue(shell.RunCommand("help"));
            TestRuntimeScope.Buffer?.DrainPending();
            bool hasUsage = Terminal
                .Buffer.Logs.Skip(initialCount)
                .Any(item =>
                    item.type == TerminalLogType.ShellMessage && item.message.Contains("Usage:")
                );
            Assert.IsTrue(hasUsage);

            initialCount = TestRuntimeScope.Buffer.Logs.Count;
            Assert.IsTrue(shell.RunCommand("help clear-history"));
            TestRuntimeScope.Buffer?.DrainPending();
            LogItem specificLog = GetLastLog(
                TestRuntimeScope.Buffer,
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

            CommandShell shell = TestRuntimeScope.Shell;
            CommandHistory history = TestRuntimeScope.History;

            Assert.IsTrue(shell.RunCommand("no-op"));
            string[] entries = history.GetHistory(false, false).ToArray();
            CollectionAssert.Contains(entries, "no-op");
            yield break;
        }

        [UnityTest]
        public IEnumerator QuitCommandIsDiscoverable()
        {
            yield return RestartTerminal();

            CommandShell shell = TestRuntimeScope.Shell;
            Assert.IsTrue(shell.Commands.ContainsKey("quit"));
            yield break;
        }
    }
}
