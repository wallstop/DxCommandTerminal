namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using DataStructures;
    using Helper;
    using UnityEngine;

    public sealed class CommandLog
    {
        private const string PackageMarker = "WallstopStudios.DxCommandTerminal";

        private static readonly string JoinSeparator = Environment.NewLine;

        /*
            The obsolete read shape: a consumer reads Count and then indexes
            as two separate reads, and a log written from another thread - or
            a Clear or a shrinking Resize on the same one - can move the
            window between them, which misses entries or throws. The surface
            stays functional; CopyTo is the supported read and every in-repo
            reader uses it.
         */
        [Obsolete(
            "Count-then-index over the live ring can throw under a concurrent log, clear, or resize."
                + " Copy the window with CopyTo for a one-call consistent read."
        )]
        public IReadOnlyList<LogItem> Logs => _logs;
        public int Capacity => _logs.Capacity;

        /*
            How many entries the visible window holds, read under the ring's
            lock. A diagnostic count, not a CopyTo sizing contract: the
            window can grow between this read and a copy, so size a
            destination for the largest capacity the session configures.
         */
        public int Count => _logs.Count;

        /*
            The version is what tells a reader that output arrived, and the
            palette turns the delta into how many entries a run produced. It is
            bumped and read from whichever thread logged, so a plain long
            increment loses updates to a concurrent write: on a 32-bit player
            the read tears as well. Interlocked is the whole fix, and it costs
            no lock on the read path.
         */
        public long Version => Interlocked.Read(ref _version);

        /*
            Which entries capture a caller stack trace. All keeps the
            pre-existing behavior; ErrorsAndWarnings and Disabled skip Unity's
            ExtractStackTrace for routine entries, the dominant per-log cost.

            A cost knob, not a correctness guard: a write reads it once to
            decide whether that entry pays extraction, so a mode that lands
            mid-write only changes what the next entry pays. The change that
            has to be atomic with the ignore filter goes through ApplyFilter.
         */
        internal TerminalStackTraceMode StackTraceMode
        {
            get => _stackTraceMode;
            set => _stackTraceMode = value;
        }

        /*
            HandleLog runs on the caller's thread - Unity's threaded log
            callback, or a user logging off the main thread - while the
            terminal reads the buffer every frame. The ring, its version, and
            its ignore filter are therefore all shared mutable state guarded
            by the ring's own lock, and the stack-trace reduction keeps its
            own, because it is entered before the ring lock is taken and
            nesting the two would order them for no gain.
         */
        private readonly CyclicBuffer<LogItem> _logs;

        /*
            The ignore filter, guarded by the ring's lock: the write path
            reads it inside the critical section that owns the write, and
            every change goes through ApplyFilter under the same lock.
         */
        private readonly HashSet<TerminalLogType> _ignoredLogTypes;

        /*
            Member buffer for stack-trace reduction: one per log, reused per
            write, grown to the longest trace seen, and reclaimed with this
            instance. HandleLog runs on the caller's thread, so no shared
            pool or lease is needed here - but two threads reducing at once
            would Clear each other's partial output, so the buffer has its own
            lock. It is never held across the caller's ExtractStackTrace.
         */
        private StringBuilder _traceBuilder;

        private readonly object _traceBuilderLock = new();

        private long _version;

        private TerminalStackTraceMode _stackTraceMode = TerminalStackTraceMode.All;

        public CommandLog(int maxItems, IEnumerable<TerminalLogType> ignoredLogTypes = null)
        {
            _logs = new CyclicBuffer<LogItem>(maxItems);
            _ignoredLogTypes = new HashSet<TerminalLogType>(
                ignoredLogTypes ?? Array.Empty<TerminalLogType>()
            );
        }

        public bool HandleLog(string message, TerminalLogType type, bool includeStackTrace = true)
        {
            if (!includeStackTrace || !CapturesStackTrace(type))
            {
                return HandleLog(message, string.Empty, type);
            }

            /*
                Extracted before the reduction so the expensive walk is not
                inside the lock, and outside the ring lock entirely so a slow
                trace never blocks another thread's write.
             */
            string stackTrace = ReduceStackTrace(StackTraceUtility.ExtractStackTrace());
            return HandleLog(message, stackTrace, type);
        }

        public bool HandleLog(string message, string stackTrace, TerminalLogType type)
        {
            /*
                The cheap rejection first, so an ignored type pays a set
                lookup and never a sanitize walk. The check runs again inside
                the write's critical section below, which is what closes the
                slip-through: between the two locks a filter change could
                land, and the old two-read shape then wrote one entry the
                filter was already dropping.
             */
            if (IsIgnored(type))
            {
                return false;
            }

            /*
                Sanitizing walks the whole message and, for anything needing
                an escape, rents a process-global string builder. It reads only
                its arguments, so it runs outside the lock: holding the ring
                across two string walks would let a worker thread's large stack
                trace stall the terminal's next frame for its duration, and a
                clean message returns the same reference, so the write itself
                stays allocation-free.

                Every source funnels here - Terminal.Log, the Unity log
                callback, and direct callers - so this is the one place log
                text is normalized.
             */
            LogItem log = new(
                type,
                LogTextSanitizer.Sanitize(message),
                CapturesStackTrace(type) ? LogTextSanitizer.Sanitize(stackTrace) : string.Empty
            );

            lock (_logs.SyncRoot)
            {
                /*
                    The filter is re-checked in the critical section that
                    owns the write, so the final decision is atomic with the
                    version bump and the ring write: an entry the filter
                    drops can no more land than it can half-land. The window
                    this closes - a filter change landing between the cheap
                    check above and this lock - is a few instructions of
                    pure string work with no reachable hook, so no test can
                    interpose there; the concurrent-filter storm in
                    CommandLogConcurrencyTests pins the observable contract
                    (an applied filter empties the flood) and the rest is
                    this paragraph.
                 */
                if (_ignoredLogTypes.Contains(type))
                {
                    return false;
                }

                /*
                    The version bump and the ring write share this lock, so a
                    reader never sees a version claiming an entry the ring does
                    not hold. An entry a later writer overwrote is gone, as it
                    was before: the version is a change signal, not a receipt.
                 */
                Interlocked.Increment(ref _version);
                _logs.Add(log);
            }

            return true;
        }

        public int Clear()
        {
            lock (_logs.SyncRoot)
            {
                int logCount = _logs.Count;
                _logs.Clear();
                Interlocked.Increment(ref _version);
                return logCount;
            }
        }

        public void Resize(int newCapacity)
        {
            lock (_logs.SyncRoot)
            {
                if (newCapacity < _logs.Count)
                {
                    Interlocked.Increment(ref _version);
                }

                _logs.Resize(newCapacity);
            }
        }

        /*
            Copies the newest entry as one consistent read. False when the
            window is empty. The read never throws: the count, the bounds,
            and the entry come out of one critical section, which is what a
            count-then-index read over Logs cannot promise.
         */
        public bool TryGetLast(out LogItem last)
        {
            lock (_logs.SyncRoot)
            {
                int count = _logs.Count;
                if (count == 0)
                {
                    last = default;
                    return false;
                }

                last = _logs[count - 1];
                return true;
            }
        }

        /*
            Copies the visible window, oldest first, into a caller-owned array
            and returns how many entries it wrote. One call is one consistent
            view; reading Logs counts and then indexes as two separate reads,
            which a background log or a resize can move underneath. A
            destination shorter than the window truncates to its oldest
            entries, so size it for the largest buffer the session configures.
         */
        public int CopyTo(LogItem[] destination)
        {
            lock (_logs.SyncRoot)
            {
                return _logs.CopyTo(destination);
            }
        }

        /*
            Whether the filter drops this type. The read shares the ring's
            lock, so it cannot see a half-applied filter.
         */
        internal bool IsIgnored(TerminalLogType type)
        {
            lock (_logs.SyncRoot)
            {
                return _ignoredLogTypes.Contains(type);
            }
        }

        /*
            A snapshot of the ignore filter, copied under the ring's lock, so
            a diagnostic reader sees one applied state instead of a set a
            concurrent apply is mid-way through replacing.
         */
        internal TerminalLogType[] GetIgnoredLogTypes()
        {
            lock (_logs.SyncRoot)
            {
                TerminalLogType[] snapshot = new TerminalLogType[_ignoredLogTypes.Count];
                _ignoredLogTypes.CopyTo(snapshot);
                return snapshot;
            }
        }

        /*
            Replaces the stack-trace mode and the ignore filter as one step
            under the ring's lock. Clear followed by UnionWith is two states,
            and a logging thread reading the set between them sees a filter
            that is briefly empty - it would capture types the developer
            asked to drop. Owning both here is what lets the class claim the
            ring lock guards them.
         */
        internal void ApplyFilter(TerminalStackTraceMode mode, IReadOnlyList<TerminalLogType> types)
        {
            lock (_logs.SyncRoot)
            {
                _stackTraceMode = mode;
                _ignoredLogTypes.Clear();
                _ignoredLogTypes.UnionWith(types ?? Array.Empty<TerminalLogType>());
            }
        }

        /*
            Reduces a full Unity stack trace to the caller's frames: drops
            line 0 (this call site) and every following line naming this
            package, joining the kept lines with Environment.NewLine. One
            index walk into the member builder instead of Split + Join, so
            a log write allocates only the result string. Null, empty, and
            whitespace inputs pass through unchanged.
         */
        internal string ReduceStackTrace(string fullStackTrace)
        {
            if (string.IsNullOrWhiteSpace(fullStackTrace))
            {
                return fullStackTrace;
            }

            lock (_traceBuilderLock)
            {
                _traceBuilder ??= new StringBuilder(fullStackTrace.Length + 16);
                _traceBuilder.Clear();
                StringBuilder builder = _traceBuilder;

                int length = fullStackTrace.Length;
                int lineStart = 0;
                int lineNumber = 0;
                bool skipping = true;
                bool keptAny = false;
                while (lineStart <= length)
                {
                    int lineEnd = lineStart;
                    while (lineEnd < length)
                    {
                        char lineCharacter = fullStackTrace[lineEnd];
                        if (lineCharacter == '\r' || lineCharacter == '\n')
                        {
                            break;
                        }

                        ++lineEnd;
                    }

                    if (skipping && 0 < lineNumber)
                    {
                        int markerIndex = fullStackTrace.IndexOf(
                            PackageMarker,
                            lineStart,
                            lineEnd - lineStart,
                            StringComparison.OrdinalIgnoreCase
                        );
                        skipping = 0 <= markerIndex;
                    }

                    if (!skipping)
                    {
                        if (keptAny)
                        {
                            builder.Append(JoinSeparator);
                        }

                        builder.Append(fullStackTrace, lineStart, lineEnd - lineStart);
                        keptAny = true;
                    }

                    ++lineNumber;
                    if (length <= lineEnd)
                    {
                        break;
                    }

                    lineStart = lineEnd + 1;
                    if (
                        fullStackTrace[lineEnd] == '\r'
                        && lineStart < length
                        && fullStackTrace[lineStart] == '\n'
                    )
                    {
                        ++lineStart;
                    }
                }

                return builder.ToString();
            }
        }

        /*
            The only modes that filter or skip capture. Everything else (the
            obsolete zero sentinel a stale serialized asset can hold, and any
            future member) keeps the legacy capture-all behavior, so unknown
            values never silently drop diagnostics.
         */
        private bool CapturesStackTrace(TerminalLogType type)
        {
            switch (_stackTraceMode)
            {
                case TerminalStackTraceMode.Disabled:
                    return false;
                case TerminalStackTraceMode.ErrorsAndWarnings:
                    return type
                        is TerminalLogType.Error
                            or TerminalLogType.Assert
                            or TerminalLogType.Exception
                            or TerminalLogType.Warning;
                default:
                    return true;
            }
        }
    }
}
