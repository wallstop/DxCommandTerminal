namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Linq;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class CommandHistoryTests
    {
        [TearDown]
        public void TearDown()
        {
            if (TerminalUI.Instance != null)
            {
                Object.Destroy(TerminalUI.Instance.gameObject);
            }
        }

        [Test]
        public void CountReflectsNumberOfEntries()
        {
            CommandHistory history = new(10);
            Assert.AreEqual(0, history.Count, "New history should have count 0");

            history.Push("command1", true, true);
            Assert.AreEqual(1, history.Count, "Count should be 1 after one push");

            history.Push("command2", true, true);
            Assert.AreEqual(2, history.Count, "Count should be 2 after two pushes");

            history.Push("command3", false, false);
            Assert.AreEqual(3, history.Count, "Count should be 3 after three pushes");
        }

        [Test]
        public void ClearEmptiesHistory()
        {
            CommandHistory history = new(10);
            history.Push("command1", true, true);
            history.Push("command2", true, true);
            history.Push("command3", true, true);

            history.Clear();

            Assert.AreEqual(0, history.Count, "Count should be 0 after clear");
            string[] entries = history.GetHistory(false, false).ToArray();
            Assert.AreEqual(0, entries.Length, "GetHistory should return no entries after clear");
        }

        [Test]
        public void ClearReturnsCorrectCount()
        {
            CommandHistory history = new(10);
            Assert.AreEqual(0, history.Clear(), "Clear on empty history should return 0");

            history.Push("command1", true, true);
            history.Push("command2", true, true);
            history.Push("command3", true, true);

            Assert.AreEqual(3, history.Clear(), "Clear should return the number of entries cleared");
        }

        [Test]
        public void ClearOnEmptyHistoryReturnsZero()
        {
            CommandHistory history = new(10);
            int count = history.Clear();
            Assert.AreEqual(0, count, "Clear on empty history should return 0");
            Assert.AreEqual(0, history.Count, "Count should remain 0 after clearing empty history");
        }

        [Test]
        public void ClearResetsNavigationPosition()
        {
            CommandHistory history = new(10);
            history.Push("command1", true, true);
            history.Push("command2", true, true);
            history.Push("command3", true, true);

            // Navigate to verify position is set
            string prev = history.Previous(false);
            Assert.AreEqual("command3", prev, "Previous should return last command before clear");

            history.Clear();

            // After clear, Previous and Next should return empty
            Assert.AreEqual(string.Empty, history.Previous(false), "Previous should return empty after clear");
            Assert.AreEqual(string.Empty, history.Next(false), "Next should return empty after clear");
        }

        [Test]
        public void PushAfterClearWorksCorrectly()
        {
            CommandHistory history = new(10);
            history.Push("old1", true, true);
            history.Push("old2", true, true);

            history.Clear();

            history.Push("new1", true, true);
            Assert.AreEqual(1, history.Count, "Count should be 1 after push following clear");

            string[] entries = history.GetHistory(false, false).ToArray();
            Assert.AreEqual(1, entries.Length, "Should have exactly one entry after push following clear");
            Assert.AreEqual("new1", entries[0], "Entry should be the newly pushed command");
        }

        [Test]
        public void NavigationAfterClearAndPushWorksCorrectly()
        {
            CommandHistory history = new(10);
            history.Push("old1", true, true);
            history.Push("old2", true, true);

            history.Clear();

            history.Push("new1", true, true);
            history.Push("new2", true, true);

            string prev = history.Previous(false);
            Assert.AreEqual("new2", prev, "Previous should return last new command");

            prev = history.Previous(false);
            Assert.AreEqual("new1", prev, "Previous again should return first new command");

            prev = history.Previous(false);
            Assert.AreEqual(string.Empty, prev, "Previous beyond beginning should return empty");

            string next = history.Next(false);
            Assert.AreEqual("new1", next, "Next should return first new command");

            next = history.Next(false);
            Assert.AreEqual("new2", next, "Next should return second new command");

            next = history.Next(false);
            Assert.AreEqual(string.Empty, next, "Next beyond end should return empty");
        }

        [Test]
        public void PushRejectsNullAndWhitespace()
        {
            CommandHistory history = new(10);

            Assert.IsFalse(history.Push(null, true, true), "Push should reject null");
            Assert.IsFalse(history.Push("", true, true), "Push should reject empty string");
            Assert.IsFalse(history.Push("   ", true, true), "Push should reject whitespace-only string");
            Assert.AreEqual(0, history.Count, "Count should remain 0 after rejected pushes");
        }

        [Test]
        public void GetHistoryFiltersSuccess()
        {
            CommandHistory history = new(10);
            history.Push("success1", true, true);
            history.Push("failure1", false, true);
            history.Push("success2", true, true);

            string[] successOnly = history.GetHistory(true, false).ToArray();
            Assert.AreEqual(2, successOnly.Length, "Should have 2 successful entries");
            Assert.IsTrue(successOnly.Contains("success1"), "Should contain success1");
            Assert.IsTrue(successOnly.Contains("success2"), "Should contain success2");
        }

        [Test]
        public void GetHistoryFiltersErrorFree()
        {
            CommandHistory history = new(10);
            history.Push("clean1", true, true);
            history.Push("errored1", true, false);
            history.Push("clean2", true, true);

            string[] errorFreeOnly = history.GetHistory(false, true).ToArray();
            Assert.AreEqual(2, errorFreeOnly.Length, "Should have 2 error-free entries");
            Assert.IsTrue(errorFreeOnly.Contains("clean1"), "Should contain clean1");
            Assert.IsTrue(errorFreeOnly.Contains("clean2"), "Should contain clean2");
        }

        [Test]
        public void CountHandlesCyclicBufferWrap()
        {
            CommandHistory history = new(2);
            history.Push("a", true, true);
            history.Push("b", true, true);
            Assert.AreEqual(2, history.Count, "Count should be 2 at capacity.");
            history.Push("c", true, true);
            Assert.AreEqual(2, history.Count, "Count should remain at capacity after wrap.");
            string[] entries = history.GetHistory(false, false).ToArray();
            Assert.AreEqual(2, entries.Length, "History should contain 2 entries after wrap.");
            Assert.AreEqual("b", entries[0], "First entry should be 'b' after wrap.");
            Assert.AreEqual("c", entries[1], "Second entry should be 'c' after wrap.");
        }

        [Test]
        public void NavigationAfterClearWithSkipSameCommands()
        {
            CommandHistory history = new(10);
            history.Push("cmd1", true, true);
            history.Push("cmd1", true, true);
            history.Push("cmd2", true, true);
            history.Clear();

            Assert.AreEqual(string.Empty, history.Previous(true), "Previous with skip should return empty after Clear.");
            Assert.AreEqual(string.Empty, history.Next(true), "Next with skip should return empty after Clear.");

            // Push duplicates after clear and navigate with skip
            history.Push("cmd1", true, true);
            history.Push("cmd1", true, true);
            history.Push("cmd2", true, true);

            string prev1 = history.Previous(true);
            Assert.AreEqual("cmd2", prev1, "Previous with skip should return 'cmd2'.");
            string prev2 = history.Previous(true);
            Assert.AreEqual("cmd1", prev2, "Previous with skip should skip duplicate and return 'cmd1'.");
        }

        [UnityTest]
        public IEnumerator ClearHistoryCommandResultsInEmptyHistory()
        {
            // clear-history uses AddToHistory = false on its RegisterCommand attribute,
            // ensuring the command itself is not recorded in the history it just cleared.
            yield return TerminalTests.SpawnTerminal(resetStateOnInit: true);

            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell, "Terminal.Shell should not be null after SpawnTerminal");
            CommandHistory history = Terminal.History;
            Assert.IsNotNull(history, "Terminal.History should not be null after SpawnTerminal");

            // Run some commands to populate history
            shell.RunCommand("log test1");
            shell.RunCommand("log test2");
            shell.RunCommand("log test3");

            string[] entries = history.GetHistory(false, false).ToArray();
            Assert.AreEqual(3, entries.Length, "Should have 3 history entries before clear-history");

            // Run clear-history
            shell.RunCommand("clear-history");

            // After fix: history should be completely empty
            entries = history.GetHistory(false, false).ToArray();
            Assert.AreEqual(
                0,
                entries.Length,
                $"History should be empty after clear-history, but contained: {string.Join(", ", entries)}"
            );
            Assert.AreEqual(0, history.Count, "History count should be 0 after clear-history");
        }

        [UnityTest]
        public IEnumerator CommandsAfterClearHistoryWorkNormally()
        {
            yield return TerminalTests.SpawnTerminal(resetStateOnInit: true);

            CommandShell shell = Terminal.Shell;
            Assert.IsNotNull(shell, "Terminal.Shell should not be null after SpawnTerminal");
            CommandHistory history = Terminal.History;
            Assert.IsNotNull(history, "Terminal.History should not be null after SpawnTerminal");

            // Populate, clear, then run more commands
            shell.RunCommand("log before");
            shell.RunCommand("clear-history");

            Assert.AreEqual(0, history.Count, "History should be empty after clear-history");

            shell.RunCommand("log after1");
            shell.RunCommand("log after2");

            string[] entries = history.GetHistory(false, false).ToArray();
            Assert.AreEqual(2, entries.Length, "Should have 2 entries after clear-history followed by 2 commands");
            Assert.IsTrue(entries.Contains("log after1"), "Should contain 'log after1'");
            Assert.IsTrue(entries.Contains("log after2"), "Should contain 'log after2'");
        }
    }
}
