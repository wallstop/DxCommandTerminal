namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using DataStructures;
    using UnityEngine;

    public sealed class CommandLog
    {
        private const string PackageMarker = "WallstopStudios.DxCommandTerminal";

        private static readonly string JoinSeparator = Environment.NewLine;

        public IReadOnlyList<LogItem> Logs => _logs;
        public int Capacity => _logs.Capacity;
        public long Version => _version;

        public readonly HashSet<TerminalLogType> ignoredLogTypes;

        private readonly CyclicBuffer<LogItem> _logs;

        /*
            Member buffer for stack-trace reduction: one per log, reused per
            write, grown to the longest trace seen, and reclaimed with this
            instance. HandleLog runs on the caller's thread, so no shared
            pool or lease is needed here.
         */
        private StringBuilder _traceBuilder;

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
            string stackTrace = includeStackTrace
                ? ReduceStackTrace(StackTraceUtility.ExtractStackTrace())
                : string.Empty;
            return HandleLog(message, stackTrace, type);
        }

        public bool HandleLog(string message, string stackTrace, TerminalLogType type)
        {
            if (ignoredLogTypes.Contains(type))
            {
                return false;
            }

            _version++;
            LogItem log = new(type, message, stackTrace);
            _logs.Add(log);
            return true;
        }

        public int Clear()
        {
            int logCount = _logs.Count;
            _logs.Clear();
            _version++;
            return logCount;
        }

        public void Resize(int newCapacity)
        {
            if (newCapacity < _logs.Count)
            {
                _version++;
            }
            _logs.Resize(newCapacity);
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
}
