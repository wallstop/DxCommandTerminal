namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;

    public sealed class CommandHistoryTests
    {
        [Test]
        public void FiltersAndOrderWork()
        {
            CommandHistory history = new(10);

            history.Push("a ok", true, true);
            history.Push("b fail", false, false);
            history.Push("c ok but error", true, false);
            history.Push("d ok", true, true);

            List<string> onlySuccess = new(history.GetHistory(true, false));
            Assert.AreEqual(3, onlySuccess.Count);
            Assert.AreEqual("a ok", onlySuccess[0]);
            Assert.AreEqual("c ok but error", onlySuccess[1]);
            Assert.AreEqual("d ok", onlySuccess[2]);

            List<string> onlyErrorFree = new(history.GetHistory(false, true));
            Assert.AreEqual(2, onlyErrorFree.Count);
            Assert.AreEqual("a ok", onlyErrorFree[0]);
            Assert.AreEqual("d ok", onlyErrorFree[1]);

            List<string> both = new(history.GetHistory(true, true));
            Assert.AreEqual(2, both.Count);
            Assert.AreEqual("a ok", both[0]);
            Assert.AreEqual("d ok", both[1]);
        }

        [Test]
        public void PreviousAndNextSkipSameCommandsWhenEnabled()
        {
            CommandHistory history = new(8);

            history.Push("alpha", true, true);
            history.Push("beta", true, true);
            history.Push("beta", true, true);
            history.Push("gamma", true, true);

            List<string> traversed = new();
            string command;
            while (!string.IsNullOrEmpty(command = history.Previous(true)))
            {
                traversed.Add(command);
            }

            Assert.AreEqual(3, traversed.Count);
            CollectionAssert.AreEquivalent(new[] { "alpha", "beta", "gamma" }, traversed);
            for (int i = 1; i < traversed.Count; ++i)
            {
                Assert.AreNotEqual(traversed[i - 1], traversed[i]);
            }

            List<string> forward = new();
            while (!string.IsNullOrEmpty(command = history.Next(true)))
            {
                forward.Add(command);
            }

            CollectionAssert.AreEquivalent(traversed, forward);
            for (int i = 1; i < forward.Count; ++i)
            {
                Assert.AreNotEqual(forward[i - 1], forward[i]);
            }
        }

        [Test]
        public void PreviousAndNextReturnDuplicatesWhenSkippingDisabled()
        {
            CommandHistory history = new(6);

            history.Push("alpha", true, true);
            history.Push("beta", true, true);
            history.Push("beta", true, true);

            List<string> backward = new();
            string command;
            while (!string.IsNullOrEmpty(command = history.Previous(false)))
            {
                backward.Add(command);
            }

            CollectionAssert.AreEquivalent(new[] { "alpha", "beta", "beta" }, backward);
            Assert.IsTrue(backward.Contains("alpha"));
            Assert.AreEqual(2, backward.FindAll(entry => entry == "beta").Count);

            List<string> forward = new();
            while (!string.IsNullOrEmpty(command = history.Next(false)))
            {
                forward.Add(command);
            }

            CollectionAssert.AreEquivalent(new[] { "alpha", "beta", "beta" }, forward);
            Assert.AreEqual(3, forward.Count);
            Assert.AreEqual("alpha", forward[0]);
            Assert.AreEqual(2, forward.FindAll(entry => entry == "beta").Count);
        }

        [Test]
        public void ResizeRetainsMostRecentEntries()
        {
            CommandHistory history = new(10);

            for (int i = 0; i < 10; ++i)
            {
                history.Push($"command {i}", true, true);
            }

            history.Resize(4);

            List<string> remaining = new(history.GetHistory(false, false));

            Assert.AreEqual(4, remaining.Count);
            Assert.AreEqual("command 6", remaining[0]);
            Assert.AreEqual("command 7", remaining[1]);
            Assert.AreEqual("command 8", remaining[2]);
            Assert.AreEqual("command 9", remaining[3]);
        }

        [Test]
        public void ClearResetsHistoryState()
        {
            CommandHistory history = new(4);

            history.Push("alpha", true, true);
            history.Push("beta", true, false);

            int removed = history.Clear();

            Assert.AreEqual(2, removed);
            List<string> entries = new(history.GetHistory(false, false));
            Assert.AreEqual(0, entries.Count);
            Assert.AreEqual(string.Empty, history.Previous(false));
            Assert.AreEqual(string.Empty, history.Next(false));
        }
    }
}
