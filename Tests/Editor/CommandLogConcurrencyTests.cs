namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Backend;
    using NUnit.Framework;

    /*
        The log buffer is written from whichever thread produced a log entry -
        Unity's threaded log callback, or a user logging off the main thread -
        and read from the main thread every frame. A background-thread
        Debug.Log racing that read is an IndexOutOfRangeException on a frame,
        from inside BoundsCheck, with nothing in the message naming threads.
    */
    public sealed class CommandLogConcurrencyTests
    {
        private const int Capacity = 256;

        private const int WriterIterations = 200_000;

        private const string FillPrefix = "fill-";

        private static readonly string[] Messages =
        {
            "alpha",
            "beta",
            "gamma with a longer body to walk",
            "delta",
        };

        private static Thread StartFlood(
            CommandLog log,
            ManualResetEventSlim start,
            int iterations,
            IReadOnlyList<string> messages,
            Action<Exception> onFailure,
            ManualResetEventSlim done = null
        )
        {
            Thread thread = new(() =>
            {
                start.Wait();
                try
                {
                    for (int i = 0; i < iterations; ++i)
                    {
                        log.HandleLog(
                            messages[i % messages.Count],
                            string.Empty,
                            TerminalLogType.Message
                        );
                    }
                }
                catch (Exception exception)
                {
                    onFailure(exception);
                }
                finally
                {
                    done?.Set();
                }
            });
            thread.Start();
            start.Set();
            return thread;
        }

        private static void Fill(CommandLog log, int count)
        {
            for (int i = 0; i < count; ++i)
            {
                log.HandleLog($"{FillPrefix}{i}", string.Empty, TerminalLogType.Message);
            }
        }

        private static void ReadEveryEntry(CommandLog log)
        {
            IReadOnlyList<LogItem> logs = log.Logs;
            int count = logs.Count;
            for (int i = 0; i < count; ++i)
            {
                Assert.That(logs[i].message, Is.Not.Null, "Sanity: the read must land");
            }
        }

        private static bool WasWritten(string message)
        {
            if (message.StartsWith(FillPrefix, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (string candidate in Messages)
            {
                if (string.Equals(candidate, message, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /*
            A flood from a background thread against a concurrent read. The
            reader is the shape the terminal uses every frame: read the count,
            then index every entry. Before the fix the unsynchronized
            read-modify-writes in CyclicBuffer.Add and CommandLog's version
            counter let the two disagree, and BoundsCheck threw.
         */
        [Test]
        public void BackgroundLogFloodDoesNotThrowAgainstAConcurrentRead()
        {
            CommandLog log = new(Capacity);
            Fill(log, Capacity);

            using ManualResetEventSlim start = new(false);
            Exception writerFailure = null;

            Thread writer = StartFlood(
                log,
                start,
                WriterIterations,
                Messages,
                exception =>
                {
                    writerFailure = exception;
                }
            );

            try
            {
                ReadEveryEntry(log);
            }
            finally
            {
                writer.Join();
            }

            Assert.That(
                writerFailure == null,
                $"Background log writes must not throw: {writerFailure}"
            );
        }

        /*
            Every entry the reader sees has to be one the writer actually
            wrote, in a whole state. A read torn across a write is not a
            cosmetic glitch: the terminal renders the message and the trace
            as one line, so a half-updated item is a line that never existed.
         */
        [Test]
        public void EntriesReadDuringAFloodAreWholeAndWereWritten()
        {
            CommandLog log = new(Capacity);
            Fill(log, Capacity);

            using ManualResetEventSlim start = new(false);
            using ManualResetEventSlim done = new(false);

            Thread writer = StartFlood(log, start, WriterIterations, Messages, _ => { }, done);

            try
            {
                while (!done.IsSet)
                {
                    foreach (LogItem entry in log.Logs)
                    {
                        Assert.AreEqual(
                            TerminalLogType.Message,
                            entry.type,
                            "A read entry must not be a partially written struct"
                        );
                        Assert.IsTrue(
                            WasWritten(entry.message),
                            $"A read entry carried a message the writer never wrote: '{entry.message}'"
                        );
                        Assert.AreEqual(
                            string.Empty,
                            entry.stackTrace,
                            "A read entry must not pair one write's message with another's trace"
                        );
                    }
                }
            }
            finally
            {
                writer.Join();
            }
        }

        /*
            The reader's own view has to stay coherent: the count it reads and
            the entries it indexes have to describe the same window. Count only
            ever grows under a concurrent write, so a stale read is harmless -
            a count that shrinks is not.
         */
        [Test]
        public void CountNeverGoesBackwardsUnderAConcurrentFlood()
        {
            CommandLog log = new(Capacity);
            Fill(log, Capacity);

            using ManualResetEventSlim start = new(false);
            using ManualResetEventSlim done = new(false);

            Thread writer = StartFlood(log, start, WriterIterations, Messages, _ => { }, done);

            int highest = 0;
            try
            {
                while (!done.IsSet)
                {
                    int count = log.Logs.Count;
                    Assert.That(
                        highest <= count,
                        $"The log count went backwards: {highest} then {count}"
                    );
                    highest = count;
                }
            }
            finally
            {
                writer.Join();
            }

            Assert.AreEqual(Capacity, highest, "Sanity: the buffer must have filled");
        }

        /*
            The version is what tells the terminal and the palette that output
            arrived. A lost update reads as "nothing was written", so a
            concurrent flood must leave it exactly one greater per entry.
         */
        [Test]
        public void VersionCountsEveryEntryWrittenDuringAConcurrentFlood()
        {
            const int writes = 5_000;
            const int writerCount = 4;

            CommandLog log = new(Capacity);
            long before = log.Version;

            using ManualResetEventSlim start = new(false);
            Exception failure = null;
            Thread[] threads = new Thread[writerCount];
            for (int w = 0; w < writerCount; ++w)
            {
                threads[w] = new Thread(() =>
                {
                    start.Wait();
                    try
                    {
                        for (int i = 0; i < writes; ++i)
                        {
                            log.HandleLog("concurrent", string.Empty, TerminalLogType.Message);
                        }
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref failure, exception, null);
                    }
                });
                threads[w].Start();
            }

            start.Set();
            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.That(failure == null, $"Concurrent writes must not throw: {failure}");
            Assert.AreEqual(
                (long)writes * writerCount,
                log.Version - before,
                "Every write must bump the version exactly once"
            );
        }

        /*
            Two threads reducing a trace at once share one member
            StringBuilder, so one thread's Clear erases the other's partial
            output and the two interleave their appends. The result is a stack
            trace naming frames from neither call - a line a developer cannot
            act on. Each thread reduces a trace carrying its own marker, so
            any foreign marker in the output is a cross-thread splice.
         */
        [Test]
        public void ConcurrentTraceReductionDoesNotSpliceTwoTraces()
        {
            CommandLog log = new(Capacity);

            using ManualResetEventSlim start = new(false);
            string first = null;
            string second = null;

            Thread a = StartReducer(log, start, "ALPHA", value => first = value);
            Thread b = StartReducer(log, start, "BETA", value => second = value);

            a.Join();
            b.Join();

            Assert.That(first != null, "Sanity: the first reduction must return");
            Assert.That(second != null, "Sanity: the second reduction must return");
            Assert.IsTrue(
                ContainsOnly(first, "ALPHA"),
                $"A reduction carried another thread's frames: {first}"
            );
            Assert.IsTrue(
                ContainsOnly(second, "BETA"),
                $"A reduction carried another thread's frames: {second}"
            );
        }

        /*
            Resize and Clear rewrite the ring's position, count, and backing
            list at once. A logging thread running while the main thread
            reconfigures the buffer - which is what applying a changed
            inspector setting does - sees half of that. The count and the
            entries have to stay a window that agrees.
         */
        [Test]
        public void ResizeAndClearDuringAFloodLeaveAReadableWindow()
        {
            CommandLog log = new(Capacity);
            Fill(log, Capacity);

            using ManualResetEventSlim start = new(false);
            using ManualResetEventSlim done = new(false);

            Thread writer = StartFlood(log, start, WriterIterations, Messages, _ => { }, done);

            try
            {
                while (!done.IsSet)
                {
                    int capacity = 8 + (Environment.TickCount & 7);
                    log.Resize(capacity);
                    log.Clear();
                    log.Resize(Capacity);

                    LogItem[] window = new LogItem[Capacity];
                    int count = log.CopyTo(window);
                    for (int i = 0; i < count; ++i)
                    {
                        Assert.That(
                            window[i].message,
                            Is.Not.Null,
                            "A resized or cleared window must still be readable"
                        );
                    }
                }
            }
            finally
            {
                writer.Join();
            }
        }

        /*
            The snapshot is the read primitive the terminal's per-frame refresh
            uses: one count and the entries that count describes, taken
            together. It has to agree with the indexer, including after the ring
            wraps and after a resize.
         */
        [Test]
        public void SnapshotMatchesTheIndexerAcrossWrapAndResize()
        {
            CommandLog log = new(4);
            AssertSnapshotMatchesIndexer(log, "empty");

            Fill(log, 4);
            AssertSnapshotMatchesIndexer(log, "exactly full");

            for (int i = 0; i < 6; ++i)
            {
                log.HandleLog($"wrap-{i}", string.Empty, TerminalLogType.Message);
            }

            AssertSnapshotMatchesIndexer(log, "wrapped");

            log.Resize(8);
            AssertSnapshotMatchesIndexer(log, "grown");

            log.Resize(2);
            AssertSnapshotMatchesIndexer(log, "shrunk");

            log.Clear();
            AssertSnapshotMatchesIndexer(log, "cleared");
        }

        /*
            Reduces a trace carrying a caller frame named `marker`, after the
            package's own frames the real reduction drops. The marker is what
            must survive; a foreign one proves two threads shared a buffer.
         */
        private Thread StartReducer(
            CommandLog log,
            ManualResetEventSlim start,
            string marker,
            Action<string> onResult
        )
        {
            Thread thread = new(() =>
            {
                start.Wait();
                string trace = string.Join(
                    Environment.NewLine,
                    "WallstopStudios.DxCommandTerminal.CommandLog:HandleLog()",
                    "WallstopStudios.DxCommandTerminal.Terminal:Log()",
                    $"GamePlay:{marker}Frame()"
                );
                onResult(log.ReduceStackTrace(trace));
            });
            thread.Start();
            start.Set();
            return thread;
        }

        private bool ContainsOnly(string reduced, string marker)
        {
            string[] lines = reduced.Split(Environment.NewLine);
            foreach (string line in lines)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.Contains("Frame()", StringComparison.Ordinal))
                {
                    if (!line.Contains(marker, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void AssertSnapshotMatchesIndexer(CommandLog log, string stage)
        {
            LogItem[] snapshot = new LogItem[Math.Max(1, log.Capacity)];
            int count = log.CopyTo(snapshot);

            IReadOnlyList<LogItem> logs = log.Logs;
            Assert.AreEqual(logs.Count, count, $"The snapshot count is the buffer count ({stage})");

            for (int i = 0; i < count; ++i)
            {
                Assert.AreEqual(
                    logs[i].message,
                    snapshot[i].message,
                    $"The snapshot must match the indexer at {i} ({stage})"
                );
            }
        }
    }
}
