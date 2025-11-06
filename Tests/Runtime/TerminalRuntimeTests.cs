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
            TerminalRuntime runtime = new();
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
            TerminalRuntime runtime = new();
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
            TerminalRuntime runtime = new();
            runtime.Configure(CreateSettings(), forceReset: true);

            TerminalRuntimeUpdateResult updated = runtime.Configure(
                CreateSettings(blockedCommands: new[] { "help" }),
                forceReset: false
            );

            Assert.IsTrue(updated.CommandsRefreshed);
            Assert.IsTrue(runtime.Shell.IgnoredCommands.Contains("help"));
        }

        [Test]
        public void ConfigureHonorsCommandAllowList()
        {
            TerminalRuntime runtime = new();
            runtime.Configure(CreateSettings(allowedCommands: new[] { "help" }), forceReset: true);

            CollectionAssert.AreEquivalent(new[] { "help" }, runtime.Shell.AutoRegisteredCommands);
        }

        [Test]
        public void ConfigureHonorsLogAllowList()
        {
            TerminalRuntime runtime = new();
            runtime.Configure(
                CreateSettings(allowedLogTypes: new[] { TerminalLogType.Message }),
                forceReset: true
            );

            CommandLog log = runtime.Log;
            log.HandleLog("allowed", TerminalLogType.Message);
            log.HandleLog("blocked", TerminalLogType.Warning);
            log.DrainPending();

            Assert.IsTrue(log.allowedLogTypes.Contains(TerminalLogType.Message));
            Assert.IsFalse(log.allowedLogTypes.Contains(TerminalLogType.Warning));
            Assert.IsTrue(log.Logs.All(entry => entry.type == TerminalLogType.Message));
        }

        [Test]
        public void LogMessageAppendsToCommandLog()
        {
            TerminalRuntime runtime = new();
            runtime.Configure(CreateSettings(), forceReset: true);

            bool logged = runtime.LogMessage(TerminalLogType.Message, "hello {0}", "world");

            Assert.IsTrue(logged);

            CommandLog log = runtime.Log;
            Assert.IsNotNull(log);
            log.DrainPending();

            Assert.AreEqual("hello world", log.Logs.Last().message);
        }

        [Test]
        public void ConfigureWithoutChangesAvoidsAllocations()
        {
            TerminalRuntime runtime = new();
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
            IReadOnlyList<TerminalLogType> blockedLogTypes = null,
            IReadOnlyList<TerminalLogType> allowedLogTypes = null,
            IReadOnlyList<string> blockedCommands = null,
            IReadOnlyList<string> allowedCommands = null,
            bool includeDefaultCommands = true
        )
        {
            return new TerminalRuntimeSettings(
                logCapacity,
                historyCapacity,
                blockedLogTypes ?? Array.Empty<TerminalLogType>(),
                allowedLogTypes ?? Array.Empty<TerminalLogType>(),
                blockedCommands ?? Array.Empty<string>(),
                allowedCommands ?? Array.Empty<string>(),
                includeDefaultCommands
            );
        }
    }
}
