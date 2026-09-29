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

        public IReadOnlyList<LogItem> Logs => _logs;
        public int Capacity => _logs.Capacity;

        /*
            The version is what tells a reader that output arrived, and the
            palette turns the delta into how many entries a run produced. It is
            bumped and read from whichever thread logged, so a plain long
            increment loses updates to a concurrent write: on a 32-bit player
            the read tears as well. Interlocked is the whole fix, and it costs
            no lock on the read path.
         */
        public long Version => Interlocked.Read(ref _version);

        public readonly HashSet<TerminalLogType> ignoredLogTypes;

        /*
            Which entries capture a caller stack trace. All keeps the
            pre-existing behavior; ErrorsAndWarnings and Disabled skip Unity's
            ExtractStackTrace for routine entries, the dominant per-log cost.
         */
        public TerminalStackTraceMode stackTraceMode = TerminalStackTraceMode.All;

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

        public CommandLog(int maxItems, IEnumerable<TerminalLogType> ignoredLogTypes = null)
        {
            _logs = new CyclicBuffer<LogItem>(maxItems);
            this.ignoredLogTypes = new HashSet<TerminalLogType>(
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
            lock (_logs.SyncRoot)
            {
                if (ignoredLogTypes.Contains(type))
                {
                    return false;
                }

                if (!CapturesStackTrace(type))
                {
                    stackTrace = string.Empty;
                }

                /*
                    Every source funnels here - Terminal.Log, the Unity log
                    callback, and direct callers - so this is the one place log
                    text is normalized. A clean message returns the same
                    reference and the write stays allocation-free.

                    The version bump and the ring write share this lock, so a
                    reader never sees a version claiming an entry the ring
                    does not hold. An entry a later writer overwrote is gone,
                    as it was before: the version is a change signal, not a
                    receipt.
                 */
                LogItem log = new(
                    type,
                    LogTextSanitizer.Sanitize(message),
                    LogTextSanitizer.Sanitize(stackTrace)
                );
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
            Replaces the ignore filter as one step. Clear followed by UnionWith
            is two, and a logging thread reading the set between them sees a
            filter that is briefly empty - it would capture types the
            developer asked to drop.
         */
        internal void SetIgnoredLogTypes(IReadOnlyList<TerminalLogType> types)
        {
            lock (_logs.SyncRoot)
            {
                ignoredLogTypes.Clear();
                ignoredLogTypes.UnionWith(types ?? Array.Empty<TerminalLogType>());
            }
        }

        /*
            Copies the visible window into a caller-owned array and returns how
            many entries it wrote. One call is one consistent view: a count read
            and the entries indexed under it separately could be moved
            underneath by a background log, which is the read side of the
            thread race this buffer is guarded against.
         */
        internal int CopyTo(LogItem[] destination)
        {
            lock (_logs.SyncRoot)
            {
                return _logs.CopyTo(destination);
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
            switch (stackTraceMode)
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
