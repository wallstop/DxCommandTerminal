namespace WallstopStudios.DxCommandTerminal.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using UI;

    /*
        Pins the log window's read contract and the memo that keeps an idle
        console from repeating it: an unchanged frame answers from the
        previous pass without a buffer copy or a search pass, and any change
        a reader can observe - a write, a clear, a resize, a query edit, a
        different buffer - misses (#222). The rendered lines the memo hands
        back must be exactly what a fresh read of the same state would copy,
        or an idle console would draw a log it never read.
     */
    public sealed class LogWindowCacheTests
    {
        private CommandLog _buffer;

        private LogFilter _filter;

        private LogWindowCache _cache;

        private static string Names(LogItem[] rendered, int count)
        {
            List<string> messages = new(count);
            for (int i = 0; i < count; ++i)
            {
                messages.Add(rendered[i].message);
            }

            return string.Join(", ", messages);
        }

        [SetUp]
        public void SetUp()
        {
            _buffer = new CommandLog(64);
            _filter = new LogFilter();
            _cache = new LogWindowCache(_filter);
        }

        [TearDown]
        public void TearDown()
        {
            _filter.Clear();
        }

        [Test]
        public void ReadCopiesWindowOldestFirst()
        {
            Write("one", "two", "three");

            int count = _cache.Read(_buffer, out LogItem[] rendered);

            Assert.AreEqual(3, count, $"Count for [{Names(rendered, count)}]");
            Assert.AreEqual("one", rendered[0].message, "Oldest entry reads first");
            Assert.AreEqual("two", rendered[1].message);
            Assert.AreEqual("three", rendered[2].message);
            Assert.IsFalse(_cache.LastReadReused, "The first read reads the buffer");
        }

        [Test]
        public void UnchangedReadReusesPreviousPass()
        {
            Write("one", "two");
            _cache.Read(_buffer, out LogItem[] first);

            int count = _cache.Read(_buffer, out LogItem[] reused);

            Assert.IsTrue(
                _cache.LastReadReused,
                "A read over an unchanged buffer and query must answer from the memo"
            );
            Assert.AreSame(first, reused, "The memo hands back the previous pass's array");
            Assert.AreEqual(2, count);
            Assert.AreEqual("one", reused[0].message);
            Assert.AreEqual("two", reused[1].message);
        }

        [Test]
        public void WriteInvalidatesTheMemo()
        {
            Write("one");
            _cache.Read(_buffer, out _);

            Write("two");
            int count = _cache.Read(_buffer, out LogItem[] rendered);

            Assert.IsFalse(_cache.LastReadReused, "A log write must invalidate the memo");
            Assert.AreEqual(2, count, $"Count after the write: [{Names(rendered, count)}]");
            Assert.AreEqual("two", rendered[1].message, "The new entry is the newest line");
        }

        [Test]
        public void ClearInvalidatesTheMemo()
        {
            Write("one");
            _cache.Read(_buffer, out _);

            _buffer.Clear();
            int count = _cache.Read(_buffer, out _);

            Assert.IsFalse(_cache.LastReadReused, "A clear must invalidate the memo");
            Assert.AreEqual(0, count, "A cleared buffer reads as an empty window");
        }

        [Test]
        public void FilteredReadKeepsOnlyMatchingLines()
        {
            Write("hit one", "miss", "hit two");
            Assert.IsTrue(_filter.SetQuery("hit"));

            int count = _cache.Read(_buffer, out LogItem[] rendered);

            Assert.AreEqual(2, count, $"Kept lines: [{Names(rendered, count)}]");
            Assert.AreEqual("hit one", rendered[0].message);
            Assert.AreEqual("hit two", rendered[1].message);
            Assert.AreEqual(2, _filter.MatchCount, "The search reports what it kept");
            Assert.AreEqual(3, _filter.TotalCount, "The search reports what it ranged over");
        }

        [Test]
        public void UnchangedQueryReusesFilteredRead()
        {
            Write("hit", "miss");
            Assert.IsTrue(_filter.SetQuery("hit"));
            _cache.Read(_buffer, out LogItem[] first);

            int count = _cache.Read(_buffer, out LogItem[] reused);

            Assert.IsTrue(
                _cache.LastReadReused,
                "An unchanged query over an unchanged buffer must skip the search pass"
            );
            Assert.AreSame(first, reused);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void QueryChangeInvalidatesTheMemo()
        {
            Write("alpha", "beta");
            Assert.IsTrue(_filter.SetQuery("alpha"));
            _cache.Read(_buffer, out _);

            Assert.IsTrue(_filter.SetQuery("beta"));
            int count = _cache.Read(_buffer, out LogItem[] rendered);

            Assert.IsFalse(_cache.LastReadReused, "A query edit must invalidate the memo");
            Assert.AreEqual(1, count, $"Kept lines for 'beta': [{Names(rendered, count)}]");
            Assert.AreEqual("beta", rendered[0].message);
        }

        [Test]
        public void EqualContentQueryResetStillRereads()
        {
            /*
                Re-setting the same query changes no window content, but the
                filter resets its counts and position on the set: the re-run
                restores them, and a memo hit here would answer "3 of 0"
                (TerminalUI's find reply reads the counts the Apply wrote).
             */
            Write("hit one", "miss", "hit two");
            Assert.IsTrue(_filter.SetQuery("hit"));
            _cache.Read(_buffer, out _);

            Assert.IsTrue(_filter.SetQuery("hit"));
            int count = _cache.Read(_buffer, out LogItem[] rendered);

            Assert.IsFalse(_cache.LastReadReused, "A re-set query must re-run the search");
            Assert.AreEqual(2, count, $"Kept lines: [{Names(rendered, count)}]");
            Assert.AreEqual(2, _filter.MatchCount, "The re-run restores the kept count");
            Assert.AreEqual(3, _filter.TotalCount, "The re-run restores the ranged-over count");
            Assert.AreEqual(1, _filter.CurrentMatch, "The re-run restores the position");
        }

        [Test]
        public void ClearedFilterRereadsWholeWindow()
        {
            Write("one", "two");
            Assert.IsTrue(_filter.SetQuery("one"));
            _cache.Read(_buffer, out _);

            _filter.Clear();
            int count = _cache.Read(_buffer, out LogItem[] rendered);

            Assert.IsFalse(_cache.LastReadReused, "Clearing the search must invalidate the memo");
            Assert.AreEqual(2, count, $"Lines after the clear: [{Names(rendered, count)}]");
        }

        [Test]
        public void CapacityChangeInvalidatesTheMemo()
        {
            Write("one");
            _cache.Read(_buffer, out _);

            /*
                A grow that drops no entry bumps no version, so the capacity
                is what tells the memo the read moved; a shrink below the
                entry count bumps the version as well.
             */
            _buffer.Resize(128);
            int grownCount = _cache.Read(_buffer, out LogItem[] grown);
            Assert.IsFalse(_cache.LastReadReused, "A capacity change must invalidate the memo");
            Assert.AreEqual(1, grownCount);
            Assert.AreEqual("one", grown[0].message);

            _buffer.Resize(1);
            int shrunkCount = _cache.Read(_buffer, out LogItem[] shrunk);
            Assert.IsFalse(_cache.LastReadReused, "The shrink must invalidate the memo too");
            Assert.AreEqual(
                1,
                shrunkCount,
                $"Newest entry survives a shrink to its own count: [{Names(shrunk, shrunkCount)}]"
            );
        }

        [Test]
        public void DifferentBufferDoesNotReuse()
        {
            Write("one");
            _cache.Read(_buffer, out _);

            CommandLog other = new(64);
            other.HandleLog("two", TerminalLogType.Message);

            int count = _cache.Read(other, out LogItem[] rendered);

            Assert.IsFalse(
                _cache.LastReadReused,
                "A different buffer instance must miss even with the same version and capacity"
            );
            Assert.AreEqual(1, count);
            Assert.AreEqual("two", rendered[0].message);
        }

        [Test]
        public void ReusedFilteredReadSurvivesWindowArrayGrowth()
        {
            /*
                The memo holds the filtered array across a buffer capacity
                change that regrew the unfiltered window: the regrowth
                re-filters into a new array, and the next unchanged read must
                hand back THAT array at the count it kept - not a third read
                of an unchanged buffer.
             */
            Write("hit", "miss");
            Assert.IsTrue(_filter.SetQuery("hit"));
            _cache.Read(_buffer, out _);

            _buffer.Resize(256);
            _cache.Read(_buffer, out LogItem[] regrown);
            Assert.IsFalse(_cache.LastReadReused, "The capacity change re-read");
            Assert.AreEqual("hit", regrown[0].message, "The regrown read kept the match");

            int count = _cache.Read(_buffer, out LogItem[] reused);

            Assert.IsTrue(_cache.LastReadReused, "The state is unchanged since the last read");
            Assert.AreSame(regrown, reused, "The reused array is the regrown filtered one");
            Assert.AreEqual(1, count);
        }

        private void Write(params string[] messages)
        {
            foreach (string message in messages)
            {
                Assert.IsTrue(
                    _buffer.HandleLog(message, TerminalLogType.Message),
                    $"Sanity: '{message}' was written, not filtered"
                );
            }
        }
    }
}
