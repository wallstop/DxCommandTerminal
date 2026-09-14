namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;

    public sealed class TerminalSessionTests
    {
        /*
           Dynamically derived from RegisteredCommands so the tests stay in sync
           with any additions/removals of default commands in BuiltinCommands.cs.
         */
        private static readonly string[] KnownDefaultCommands = CommandShell
            .RegisteredCommands.Value.Where(tuple => tuple.attribute.Default)
            .Select(tuple => tuple.attribute.Name)
            .ToArray();

        private CommandLog _originalBuffer;
        private CommandShell _originalShell;
        private CommandHistory _originalHistory;
        private CommandAutoComplete _originalAutoComplete;

        private static TerminalSession.Config Config(
            int logBufferSize = 64,
            int historyBufferSize = 64,
            IReadOnlyList<TerminalLogType> ignoredLogTypes = null,
            IReadOnlyList<string> disabledCommands = null,
            bool ignoreDefaultCommands = false
        )
        {
            return new TerminalSession.Config(
                logBufferSize,
                historyBufferSize,
                ignoredLogTypes,
                disabledCommands,
                ignoreDefaultCommands
            );
        }

        [SetUp]
        public void SetUp()
        {
            _originalBuffer = Terminal.Buffer;
            _originalShell = Terminal.Shell;
            _originalHistory = Terminal.History;
            _originalAutoComplete = Terminal.AutoComplete;
        }

        [TearDown]
        public void TearDown()
        {
            Terminal.Buffer = _originalBuffer;
            Terminal.Shell = _originalShell;
            Terminal.History = _originalHistory;
            Terminal.AutoComplete = _originalAutoComplete;
        }

        [Test]
        public void ApplyCreatesAllBackendsWhenNull()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: false);

            Assert.IsNotNull(session.Buffer, "Buffer should be created when null");
            Assert.IsNotNull(session.History, "History should be created when null");
            Assert.IsNotNull(session.Shell, "Shell should be created when null");
            Assert.IsNotNull(session.AutoComplete, "AutoComplete should be created when null");
            Assert.AreEqual(
                64,
                session.Buffer.Capacity,
                "Buffer capacity should match the configured log buffer size"
            );
            Assert.AreEqual(
                64,
                session.History.Capacity,
                "History capacity should match the configured history buffer size"
            );
        }

        [Test]
        public void ApplyClampsInvalidBufferSizesToZero()
        {
            TerminalSession session = new();
            session.Apply(Config(logBufferSize: -5, historyBufferSize: -1), force: false);

            Assert.AreEqual(
                0,
                session.Buffer.Capacity,
                "A negative log buffer size must clamp to zero"
            );
            Assert.AreEqual(
                0,
                session.History.Capacity,
                "A negative history buffer size must clamp to zero"
            );
        }

        [Test]
        public void ApplyIsIdempotentForUnchangedConfig()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);
            session.Shell.EnsureAutoCommandsRegistered();

            CommandLog buffer = session.Buffer;
            CommandHistory history = session.History;
            CommandShell shell = session.Shell;
            CommandAutoComplete autoComplete = session.AutoComplete;

            bool reconfigured = session.Apply(Config(), force: false);

            Assert.IsFalse(
                reconfigured,
                "An unchanged configuration must not reconfigure the shell"
            );
            Assert.AreSame(buffer, session.Buffer, "Buffer should be reused");
            Assert.AreSame(history, session.History, "History should be reused");
            Assert.AreSame(shell, session.Shell, "Shell should be reused");
            Assert.AreSame(autoComplete, session.AutoComplete, "AutoComplete should be reused");
        }

        [TestCase(64, 32)]
        [TestCase(32, 128)]
        public void ApplyResizesBufferCapacity(int initialSize, int newSize)
        {
            TerminalSession session = new();
            session.Apply(Config(logBufferSize: initialSize), force: true);
            CommandLog buffer = session.Buffer;

            session.Apply(Config(logBufferSize: newSize), force: false);

            Assert.AreSame(buffer, session.Buffer, "Buffer should be resized, not recreated");
            Assert.AreEqual(newSize, session.Buffer.Capacity);
        }

        [TestCase(64, 32)]
        [TestCase(32, 128)]
        public void ApplyResizesHistoryCapacity(int initialSize, int newSize)
        {
            TerminalSession session = new();
            session.Apply(Config(historyBufferSize: initialSize), force: true);
            CommandHistory history = session.History;

            session.Apply(Config(historyBufferSize: newSize), force: false);

            Assert.AreSame(history, session.History, "History should be resized, not recreated");
            Assert.AreEqual(newSize, session.History.Capacity);
        }

        [Test]
        public void ApplySyncsIgnoredLogTypesOnSameBuffer()
        {
            TerminalSession session = new();
            session.Apply(Config(ignoredLogTypes: new[] { TerminalLogType.Error }), force: true);
            CommandLog buffer = session.Buffer;
            Assert.IsFalse(
                buffer.HandleLog("dropped", TerminalLogType.Error),
                "Sanity: an ignored type must be dropped while configured"
            );
            Assert.AreEqual(0, buffer.Logs.Count);

            session.Apply(Config(ignoredLogTypes: new[] { TerminalLogType.Warning }), force: false);

            Assert.AreSame(buffer, session.Buffer, "Buffer should be reused when syncing filters");
            Assert.IsTrue(
                buffer.HandleLog("kept", TerminalLogType.Error),
                "A previously ignored type should be accepted after resync"
            );
            Assert.IsFalse(
                buffer.HandleLog("dropped", TerminalLogType.Warning),
                "A newly ignored type must be dropped after resync"
            );
        }

        [Test]
        public void ApplyTreatsNullListsAsEmpty()
        {
            TerminalSession session = new();
            session.Apply(
                Config(
                    ignoredLogTypes: new[] { TerminalLogType.Error },
                    disabledCommands: new[] { "help" }
                ),
                force: true
            );
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsFalse(
                session.Buffer.HandleLog("dropped", TerminalLogType.Error),
                "Sanity: the ignored type must be dropped while configured"
            );
            Assert.IsTrue(
                session.Shell.IgnoredCommands.Contains("help"),
                "Sanity: the disabled command must be ignored while configured"
            );

            session.Apply(Config(), force: false);

            Assert.IsTrue(
                session.Buffer.HandleLog("kept", TerminalLogType.Error),
                "A null ignored list must clear the buffer's ignored types"
            );
            Assert.IsEmpty(
                session.Shell.IgnoredCommands,
                "A null disabled list must clear the shell's ignored commands"
            );
        }

        [Test]
        public void ApplyReconfiguresShellWhenDisabledCommandsChange()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsTrue(
                session.Shell.Commands.ContainsKey("log"),
                "Sanity: 'log' should be registered before being disabled"
            );

            bool reconfigured = session.Apply(
                Config(disabledCommands: new[] { "log" }),
                force: false
            );

            Assert.IsTrue(reconfigured, "Changing disabled commands must reconfigure the shell");
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsFalse(
                session.Shell.Commands.ContainsKey("log"),
                "A disabled command must not be registered"
            );
            Assert.IsTrue(
                session.Shell.IgnoredCommands.Contains("log"),
                "The disabled command should be reported in IgnoredCommands"
            );

            reconfigured = session.Apply(Config(), force: false);

            Assert.IsTrue(reconfigured, "Clearing disabled commands must reconfigure the shell");
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsTrue(
                session.Shell.Commands.ContainsKey("log"),
                "A re-enabled command should be registered again"
            );
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ApplyAppliesIgnoreDefaultCommands(bool ignoreDefaultCommands)
        {
            TerminalSession session = new();
            session.Apply(Config(ignoreDefaultCommands: ignoreDefaultCommands), force: true);
            session.Shell.EnsureAutoCommandsRegistered();

            Assert.AreEqual(
                ignoreDefaultCommands,
                session.Shell.IgnoringDefaultCommands,
                "Shell.IgnoringDefaultCommands should match the applied configuration"
            );
            Assert.IsNotEmpty(
                KnownDefaultCommands,
                "Sanity: expected at least one default command"
            );
            foreach (string command in KnownDefaultCommands)
            {
                Assert.AreEqual(
                    !ignoreDefaultCommands,
                    session.Shell.Commands.ContainsKey(command),
                    $"Default command '{command}' should {(ignoreDefaultCommands ? "not " : string.Empty)}be registered"
                );
            }
        }

        [Test]
        public void ApplyForceRecreatesAllBackends()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);
            session.Shell.EnsureAutoCommandsRegistered();
            session.Buffer.HandleLog("before", TerminalLogType.ShellMessage);

            CommandLog buffer = session.Buffer;
            CommandHistory history = session.History;
            CommandShell shell = session.Shell;
            CommandAutoComplete autoComplete = session.AutoComplete;

            bool reconfigured = session.Apply(Config(), force: true);

            Assert.IsTrue(
                reconfigured,
                "Force must reconfigure the recreated shell so its auto-command "
                    + "configuration is applied"
            );
            Assert.AreNotSame(buffer, session.Buffer, "Force must recreate the buffer");
            Assert.AreNotSame(history, session.History, "Force must recreate the history");
            Assert.AreNotSame(shell, session.Shell, "Force must recreate the shell");
            Assert.AreNotSame(
                autoComplete,
                session.AutoComplete,
                "Force must recreate the auto-complete"
            );
            Assert.AreEqual(
                0,
                session.Buffer.Logs.Count,
                "A recreated buffer must not retain the previous buffer's logs"
            );
        }

        [Test]
        public void ApplyPreservesHistoryWhenReconfiguringShell()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);
            session.Shell.EnsureAutoCommandsRegistered();
            Assert.IsTrue(
                session.Shell.RunCommand("log"),
                "Sanity: 'log' should run to create a history entry"
            );
            CommandHistory history = session.History;
            string[] events = session
                .History.GetHistory(onlySuccess: true, onlyErrorFree: true)
                .ToArray();
            Assert.IsNotEmpty(events, "Sanity: expected a history entry after running 'log'");

            session.Apply(Config(disabledCommands: new[] { "log" }), force: false);

            Assert.AreSame(
                history,
                session.History,
                "A shell reconfiguration must preserve the history instance"
            );
            string[] currentEvents = session
                .History.GetHistory(onlySuccess: true, onlyErrorFree: true)
                .ToArray();
            Assert.AreEqual(
                events.Length,
                currentEvents.Length,
                "History entries must survive a shell reconfiguration"
            );
            for (int i = 0; i < events.Length; ++i)
            {
                Assert.AreEqual(events[i], currentEvents[i], $"History event {i} wasn't the same!");
            }
        }

        [Test]
        public void ApplyDefersDiscoveryUntilFirstCommandRequest()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);

            Assert.IsFalse(
                session.Shell.AutoCommandsRegistered,
                "Apply must not walk the command catalog; registration is deferred"
            );

            session.Shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(
                session.Shell.AutoCommandsRegistered,
                "The first explicit readiness request should complete registration"
            );
            Assert.IsNotEmpty(
                session.Shell.AutoRegisteredCommands,
                "Deferred registration should surface auto commands"
            );
        }

        [Test]
        public void FacadeDelegatesToCurrentSession()
        {
            CommandLog buffer = new(32, null);
            CommandHistory history = new(32);
            CommandShell shell = new(history);
            CommandAutoComplete autoComplete = new(history, shell);

            Terminal.Buffer = buffer;
            Terminal.History = history;
            Terminal.Shell = shell;
            Terminal.AutoComplete = autoComplete;

            Assert.AreSame(buffer, Terminal.Buffer, "Terminal.Buffer should round-trip");
            Assert.AreSame(history, Terminal.History, "Terminal.History should round-trip");
            Assert.AreSame(shell, Terminal.Shell, "Terminal.Shell should round-trip");
            Assert.AreSame(
                autoComplete,
                Terminal.AutoComplete,
                "Terminal.AutoComplete should round-trip"
            );
            Assert.AreSame(
                buffer,
                TerminalSession.Current.Buffer,
                "Terminal.Buffer should delegate to the current session"
            );
            Assert.AreSame(
                history,
                TerminalSession.Current.History,
                "Terminal.History should delegate to the current session"
            );
            Assert.AreSame(
                shell,
                TerminalSession.Current.Shell,
                "Terminal.Shell should delegate to the current session"
            );
            Assert.AreSame(
                autoComplete,
                TerminalSession.Current.AutoComplete,
                "Terminal.AutoComplete should delegate to the current session"
            );
        }

        [Test]
        public void ApplyReconfiguresShellWhenAutoCommandsNeverRegistered()
        {
            TerminalSession session = new();
            session.Apply(Config(), force: true);

            bool reconfigured = session.Apply(Config(), force: false);

            Assert.IsTrue(
                reconfigured,
                "A shell with pending registration must reapply its auto-command "
                    + "configuration on every refresh"
            );
        }
    }
}
