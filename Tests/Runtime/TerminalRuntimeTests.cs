namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;

    public sealed class TerminalRuntimeTests
    {
        [Test]
        public void ConfigureCreatesRuntimeComponents()
        {
            TerminalRuntime runtime = new TerminalRuntime();
            TerminalRuntimeUpdateResult result = runtime.Configure(
                CreateSettings(),
                forceReset: true
            );

            Assert.IsNotNull(runtime.Log);
            Assert.IsNotNull(runtime.History);
            Assert.IsNotNull(runtime.Shell);
            Assert.IsNotNull(runtime.AutoComplete);

            Assert.IsTrue(result.LogRecreated);
            Assert.IsTrue(result.HistoryRecreated);
            Assert.IsTrue(result.ShellRecreated);
            Assert.IsTrue(result.AutoCompleteRecreated);
        }

        [Test]
        public void ConfigureWithoutForceReusesExistingInstances()
        {
            TerminalRuntime runtime = new TerminalRuntime();
            runtime.Configure(CreateSettings(), forceReset: true);

            CommandLog initialLog = runtime.Log;
            CommandHistory initialHistory = runtime.History;
            CommandShell initialShell = runtime.Shell;
            CommandAutoComplete initialAutoComplete = runtime.AutoComplete;

            TerminalRuntimeUpdateResult result = runtime.Configure(
                CreateSettings(),
                forceReset: false
            );

            Assert.AreSame(initialLog, runtime.Log);
            Assert.AreSame(initialHistory, runtime.History);
            Assert.AreSame(initialShell, runtime.Shell);
            Assert.AreSame(initialAutoComplete, runtime.AutoComplete);

            Assert.IsFalse(result.LogRecreated);
            Assert.IsFalse(result.HistoryRecreated);
            Assert.IsFalse(result.ShellRecreated);
            Assert.IsFalse(result.AutoCompleteRecreated);
        }

        [Test]
        public void ConfigureDetectsCommandConfigurationChanges()
        {
            TerminalRuntime runtime = new TerminalRuntime();
            runtime.Configure(CreateSettings(), forceReset: true);

            TerminalRuntimeUpdateResult updated = runtime.Configure(
                CreateSettings(disabledCommands: new[] { "help" }),
                forceReset: false
            );

            Assert.IsTrue(updated.CommandsRefreshed);
            Assert.IsTrue(runtime.Shell.IgnoredCommands.Contains("help"));
        }

        [Test]
        public void LogMessageAppendsToCommandLog()
        {
            TerminalRuntime runtime = new TerminalRuntime();
            runtime.Configure(CreateSettings(), forceReset: true);

            bool logged = runtime.LogMessage(
                TerminalLogType.Message,
                "hello {0}",
                "world"
            );

            Assert.IsTrue(logged);

            CommandLog log = runtime.Log;
            Assert.IsNotNull(log);
            log.DrainPending();

            Assert.AreEqual("hello world", log.Logs.Last().message);
        }

        [Test]
        public void ConfigureWithoutChangesAvoidsAllocations()
        {
            TerminalRuntime runtime = new TerminalRuntime();
            TerminalRuntimeSettings settings = CreateSettings();
            runtime.Configure(settings, forceReset: true);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetAllocatedBytesForCurrentThread();
            TerminalRuntimeUpdateResult result = runtime.Configure(settings, forceReset: false);
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.IsFalse(result.RuntimeReset);
            Assert.AreEqual(before, after, "Configure should not allocate after warm up.");
        }

        private static TerminalRuntimeSettings CreateSettings(
            int logCapacity = 32,
            int historyCapacity = 16,
            IReadOnlyList<TerminalLogType> ignoredLogTypes = null,
            IReadOnlyList<string> disabledCommands = null,
            bool ignoreDefaultCommands = false
        )
        {
            return new TerminalRuntimeSettings(
                logCapacity,
                historyCapacity,
                ignoredLogTypes ?? Array.Empty<TerminalLogType>(),
                disabledCommands ?? Array.Empty<string>(),
                ignoreDefaultCommands
            );
        }
    }
}
