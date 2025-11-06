namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using DataStructures;
    using UnityEngine;

    public enum TerminalLogType
    {
        Error = LogType.Error,
        Assert = LogType.Assert,
        Warning = LogType.Warning,
        Message = LogType.Log,
        Exception = LogType.Exception,
        Input,
        ShellMessage,
    }

    public readonly struct LogItem
    {
        public readonly TerminalLogType type;
        public readonly string message;
        public readonly string stackTrace;

        public LogItem(TerminalLogType type, string message, string stackTrace)
        {
            this.type = type;
            this.message = message ?? string.Empty;
            this.stackTrace = stackTrace ?? string.Empty;
        }
    }

    public sealed class CommandLog
    {
        private const string InternalNamespace = "WallstopStudios.DxCommandTerminal";

        public IReadOnlyList<LogItem> Logs => _logs;
        public int Capacity => _logs.Capacity;
        public long Version => _version;

        public readonly HashSet<TerminalLogType> ignoredLogTypes;
        public readonly HashSet<TerminalLogType> allowedLogTypes;

        private readonly CyclicBuffer<LogItem> _logs;

        private long _version;

        private readonly ConcurrentQueue<(
            string message,
            string stackTrace,
            TerminalLogType type,
            bool includeStackTrace
        )> _pending = new();

        public CommandLog(
            int maxItems,
            IEnumerable<TerminalLogType> blockedLogTypes = null,
            IEnumerable<TerminalLogType> allowedLogTypes = null
        )
        {
            _logs = new CyclicBuffer<LogItem>(maxItems);
            blockedLogTypes ??= Array.Empty<TerminalLogType>();
            this.ignoredLogTypes = new HashSet<TerminalLogType>(blockedLogTypes);
            allowedLogTypes ??= Array.Empty<TerminalLogType>();
            this.allowedLogTypes = new HashSet<TerminalLogType>(allowedLogTypes);
        }

        public bool HandleLog(string message, TerminalLogType type, bool includeStackTrace = true)
        {
            // Main-thread direct path retained for back-compat
            string stackTrace = includeStackTrace ? GetAccurateStackTrace() : string.Empty;
            return HandleLog(message, stackTrace, type);
        }

        private static string GetAccurateStackTrace()
        {
            string fullStackTrace = StackTraceUtility.ExtractStackTrace();
            if (string.IsNullOrWhiteSpace(fullStackTrace))
            {
                return fullStackTrace;
            }
            int length = fullStackTrace.Length;
            int index = 0;
            // Skip the first line (StackTraceUtility includes a leading line)
            while (index < length && fullStackTrace[index] != '\n' && fullStackTrace[index] != '\r')
            {
                index++;
            }
            // Consume newline chars
            while (
                index < length && (fullStackTrace[index] == '\n' || fullStackTrace[index] == '\r')
            )
            {
                index++;
            }

            // Skip frames inside our own namespace for clearer logs
            while (index < length)
            {
                int lineStart = index;
                // Find end of line
                while (
                    index < length && fullStackTrace[index] != '\n' && fullStackTrace[index] != '\r'
                )
                {
                    index++;
                }

                int lineLen = index - lineStart;
                bool isInternal =
                    fullStackTrace.IndexOf(
                        InternalNamespace,
                        lineStart,
                        lineLen,
                        StringComparison.OrdinalIgnoreCase
                    ) >= 0;
                if (!isInternal)
                {
                    // Return from this line onward
                    return fullStackTrace.Substring(lineStart);
                }

                // Move to next line start (skip newline chars)
                while (
                    index < length
                    && (fullStackTrace[index] == '\n' || fullStackTrace[index] == '\r')
                )
                {
                    index++;
                }
            }

            return string.Empty;
        }

        public bool HandleLog(string message, string stackTrace, TerminalLogType type)
        {
            if (!IsLogTypePermitted(type))
            {
                return false;
            }

            _version++;
            LogItem log = new(type, message, stackTrace);
            _logs.Add(log);
            return true;
        }

        public void EnqueueMessage(string message, TerminalLogType type, bool includeStackTrace)
        {
            if (!IsLogTypePermitted(type))
            {
                return;
            }
            _pending.Enqueue((message ?? string.Empty, string.Empty, type, includeStackTrace));
        }

        public void EnqueueUnityLog(string message, string stackTrace, TerminalLogType type)
        {
            if (!IsLogTypePermitted(type))
            {
                return;
            }
            _pending.Enqueue((message ?? string.Empty, stackTrace ?? string.Empty, type, false));
        }

        public int DrainPending()
        {
            int added = 0;
            while (
                _pending.TryDequeue(
                    out (
                        string message,
                        string stackTrace,
                        TerminalLogType type,
                        bool includeStackTrace
                    ) item
                )
            )
            {
                string stack = item.includeStackTrace ? GetAccurateStackTrace() : item.stackTrace;
                if (!IsLogTypePermitted(item.type))
                {
                    continue;
                }
                _version++;
                _logs.Add(new LogItem(item.type, item.message, stack));
                added++;
            }

            return added;
        }

        public int Clear()
        {
            int logCount = _logs.Count;
            _logs.Clear();
            _version++;
            return logCount;
        }

        public int RemoveWhere(Func<LogItem, bool> predicate)
        {
            if (predicate == null)
            {
                return 0;
            }

            int count = _logs.Count;
            if (count == 0)
            {
                return 0;
            }

            List<LogItem> retained = new(count);
            for (int i = 0; i < count; ++i)
            {
                LogItem entry = _logs[i];
                if (!predicate(entry))
                {
                    retained.Add(entry);
                }
            }

            if (retained.Count == count)
            {
                return 0;
            }

            _logs.Clear();
            for (int i = 0; i < retained.Count; ++i)
            {
                _logs.Add(retained[i]);
            }
            _version++;
            return count - retained.Count;
        }

        public void Resize(int newCapacity)
        {
            if (newCapacity < _logs.Count)
            {
                _version++;
            }
            _logs.Resize(newCapacity);
        }

        private bool IsLogTypePermitted(TerminalLogType type)
        {
            if (ignoredLogTypes.Contains(type))
            {
                return false;
            }

            if (allowedLogTypes.Count > 0 && !allowedLogTypes.Contains(type))
            {
                return false;
            }

            return true;
        }
    }
}
