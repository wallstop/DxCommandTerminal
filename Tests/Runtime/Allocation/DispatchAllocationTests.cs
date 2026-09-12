namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;

    /// <summary>
    ///     Allocation pins for the designated warmed hot paths from the T03
    ///     operation inventory: borrowed-view dispatch with history disabled,
    ///     history traversal, history copy into a preexisting buffer, and
    ///     fixed-capacity buffer wrap. Every pin warms its path first and is
    ///     control-validated by the assertions.
    /// </summary>
    public sealed class DispatchAllocationTests
    {
        private const int HistoryCapacity = 32;

        private CommandShell _shell;
        private CommandHistory _history;
        private int _invocations;

        private static List<string> DistinctHistoryEntries()
        {
            List<string> entries = new(HistoryCapacity);
            for (int index = 0; index < HistoryCapacity; ++index)
            {
                entries.Add($"entry-{index:D2}");
            }

            return entries;
        }

        [SetUp]
        public void SetUp()
        {
            _history = new CommandHistory(HistoryCapacity);
            _shell = new CommandShell(_history);
            _invocations = 0;
            CommandDefinition definition = new()
            {
                Name = "alloc-free",
                MinArgCount = 1,
                MaxArgCount = 1,
                AddToHistory = false,
                Handler = HandleAllocFree,
            };
            Assert.IsTrue(
                _shell.AddCommand(definition),
                "The allocation-free command should register"
            );
        }

        [Test]
        public void BorrowedViewDispatchWithHistoryDisabledIsAllocationFree()
        {
            List<CommandArg> arguments = new() { new CommandArg("41") };
            CommandExecutionContext context = new(CommandExecutionContexts.Player);
            Action dispatch = () => _shell.RunCommand(context, "alloc-free", arguments);

            AllocationAssertions.AssertZeroAllocations("borrowed-view dispatch", dispatch);
            Assert.AreEqual(
                AllocationAssertions.DefaultWarmupIterations + 1,
                _invocations,
                "The handler must run once per measured invocation"
            );
        }

        [Test]
        public void HistoryTraversalIsAllocationFree()
        {
            FillHistory(DistinctHistoryEntries());
            Assert.AreEqual(
                "entry-31",
                _history.Previous(false),
                "Sanity: traversal must return entries, not degenerate empties"
            );

            Action traverse = () =>
            {
                _history.Next(true);
                _history.Previous(true);
            };

            AllocationAssertions.AssertZeroAllocations("history traversal", traverse);
        }

        [Test]
        public void HistoryCopyToPreexistingBufferIsAllocationFree()
        {
            FillHistory(DistinctHistoryEntries());
            List<string> buffer = new(HistoryCapacity);

            Action copy = () => _history.CopyHistory(false, false, buffer);
            AllocationAssertions.AssertZeroAllocations("history copy", copy);
            Assert.AreEqual(
                HistoryCapacity,
                buffer.Count,
                "Sanity: the copy must be complete after the measured window"
            );
        }

        [Test]
        public void FixedCapacityHistoryWrapPushIsAllocationFree()
        {
            FillHistory(DistinctHistoryEntries());
            string wrappedEntry = "wrap-entry";

            Action push = () => _history.Push(wrappedEntry, true, true);
            AllocationAssertions.AssertZeroAllocations("fixed-capacity wrap push", push);
            Assert.AreEqual(
                HistoryCapacity,
                _history.Count,
                "Sanity: wrap pushes must land in the wrapped buffer, not grow it"
            );
        }

        private void FillHistory(List<string> entries)
        {
            foreach (string entry in entries)
            {
                Assert.IsTrue(_history.Push(entry, true, true), $"History should accept {entry}");
            }
        }

        private void HandleAllocFree(
            CommandExecutionContext context,
            BorrowedCommandArguments arguments
        )
        {
            ++_invocations;
        }
    }
}
