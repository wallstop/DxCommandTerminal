namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;

    /*
        Allocation characterization for the issue #108 standard-operations
        inventory. Zero-allocation claims run through the control-validated
        instrument; the DetectsAllocations characterizations document the
        known, deliberate allocations of the legacy paths so a future
        optimization must consciously update them. Companion timing evidence
        lives in StandardOperationsBenchmarkTests.
    */
    public sealed class StandardOperationsAllocationTests
    {
        private const int HistoryCapacity = 64;
        private const int LogCapacity = 16;
        private const int CommandCount = 32;

        private const string CasedCommandName = "BenchCasedCmd";
        private const string TypedPrefix = "bench";

        private readonly List<string> _completionBuffer = new(CommandCount + 1);

        private CommandAutoComplete _autoComplete;
        private CommandHistory _history;
        private CommandLog _log;
        private CommandShell _shell;

        private static void HandleNoop(CommandArg[] arguments) { }

        [SetUp]
        public void SetUp()
        {
            _history = new CommandHistory(HistoryCapacity);
            _log = new CommandLog(LogCapacity);
            _shell = new CommandShell(_history);
            _autoComplete = new CommandAutoComplete(_history, _shell);
            _completionBuffer.Clear();
            _history.Push("bench existing", true, true);
        }

        [Test]
        public void TypingCompletionWithLowercaseCommandsIsAllocationFree()
        {
            for (int i = 0; i < CommandCount; ++i)
            {
                _shell.AddCommand($"bench-cmd-{i:D4}", HandleNoop);
            }

            AllocationAssertions.AssertZeroAllocations(
                "typing completion (lowercase commands)",
                () => _autoComplete.Complete(TypedPrefix, _completionBuffer)
            );
            Assert.LessOrEqual(
                CommandCount,
                _completionBuffer.Count,
                "Sanity: every lowercase command should complete"
            );
        }

        [Test]
        public void TypingCompletionWithCasedCommandsIsAllocationFree()
        {
            _shell.AddCommand(CasedCommandName, HandleNoop);

            AllocationAssertions.AssertZeroAllocations(
                "typing completion (cased command name)",
                () => _autoComplete.Complete(TypedPrefix, _completionBuffer)
            );
            Assert.LessOrEqual(
                1,
                _completionBuffer.Count,
                "Sanity: the cased command should complete"
            );
        }

        [Test]
        public void LogWriteWithoutStackTraceIsAllocationFree()
        {
            FillLogToCapacity();

            AllocationAssertions.AssertZeroAllocations(
                "log write (empty stack trace)",
                () => _log.HandleLog("bench message", string.Empty, TerminalLogType.ShellMessage)
            );
            Assert.AreEqual(
                LogCapacity,
                _log.Logs.Count,
                "Sanity: the write must land in the wrapped buffer"
            );
        }

        [Test]
        public void LogWriteWithStackTraceAllocates()
        {
            FillLogToCapacity();

            /*
                Documented, not pinned to zero: stack-trace extraction (and
                its split/join cleanup) is the deliberate cost of attributing
                a direct Terminal.Log call to its caller.
             */
            AllocationAssertions.AssertDetectsAllocation(
                "log write (stack-trace extraction)",
                () => _log.HandleLog("bench message", TerminalLogType.ShellMessage)
            );
        }

        [Test]
        public void TextCommandExecutionAllocatesByDesign()
        {
            _shell.AddCommand("bench-cmd", HandleNoop, 1);

            /*
                Documented, not pinned to zero: tokenization materializes one
                substring per token and the history push builds its line
                string. The borrowed-view no-history path is pinned
                allocation-free in DispatchAllocationTests.
             */
            AllocationAssertions.AssertDetectsAllocation(
                "text command execution (parse + history line)",
                () => _shell.RunCommand("bench-cmd 5")
            );
            Assert.Less(0, _history.Count, "Sanity: execution must push history");
        }

        private void FillLogToCapacity()
        {
            for (int i = 0; i < LogCapacity; ++i)
            {
                _log.HandleLog($"bench fill {i}", string.Empty, TerminalLogType.ShellMessage);
            }
        }
    }
}
