namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;

    public sealed class CommandLogTests
    {
        [Test]
        public void HandleLogRespectsIgnoredTypes()
        {
            CommandLog log = new(4, new[] { TerminalLogType.Warning });

            bool handled = log.HandleLog("ignored", TerminalLogType.Warning);

            Assert.IsFalse(handled);
            Assert.AreEqual(0, log.Logs.Count);
        }

        [Test]
        public void DrainPendingProcessesQueuedMessagesInOrder()
        {
            CommandLog log = new(8);

            log.EnqueueMessage("first", TerminalLogType.Message, includeStackTrace: false);
            log.EnqueueUnityLog("second", "stack", TerminalLogType.Error);

            int added = log.DrainPending();

            Assert.AreEqual(2, added);
            Assert.AreEqual(2, log.Logs.Count);
            Assert.AreEqual("first", log.Logs[0].message);
            Assert.AreEqual(string.Empty, log.Logs[0].stackTrace);
            Assert.AreEqual("second", log.Logs[1].message);
            Assert.AreEqual("stack", log.Logs[1].stackTrace);
            Assert.AreEqual(TerminalLogType.Error, log.Logs[1].type);
        }

        [Test]
        public void ResizeTrimsOldestEntriesAndKeepsOrder()
        {
            CommandLog log = new(6);

            for (int i = 0; i < 6; ++i)
            {
                log.HandleLog($"log {i}", TerminalLogType.Message);
            }

            log.Resize(3);

            Assert.AreEqual(3, log.Logs.Count);
            List<string> messages = log.Logs.Select(item => item.message).ToList();
            CollectionAssert.AreEqual(new[] { "log 3", "log 4", "log 5" }, messages);
        }

        [Test]
        public void ClearEmptiesBufferAndReturnsRemovedCount()
        {
            CommandLog log = new(5);

            log.HandleLog("a", TerminalLogType.Message);
            log.HandleLog("b", TerminalLogType.Warning);

            int removed = log.Clear();

            Assert.AreEqual(2, removed);
            Assert.AreEqual(0, log.Logs.Count);
            Assert.AreEqual(0, log.DrainPending());
        }
    }
}
