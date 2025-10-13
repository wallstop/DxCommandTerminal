namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
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

        private readonly CyclicBuffer<LogItem> _logs;

        private long _version;

        private readonly ConcurrentQueue<(
            string message,
            string stackTrace,
            TerminalLogType type,
            bool includeStackTrace
        )> _pending = new();

        public CommandLog(int maxItems, IEnumerable<TerminalLogType> ignoredLogTypes = null)
        {
            _logs = new CyclicBuffer<LogItem>(maxItems);
            this.ignoredLogTypes = new HashSet<TerminalLogType>(
                ignoredLogTypes ?? Enumerable.Empty<TerminalLogType>()
            );
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
            if (ignoredLogTypes.Contains(type))
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
            if (ignoredLogTypes.Contains(type))
            {
                return;
            }
            _pending.Enqueue((message ?? string.Empty, string.Empty, type, includeStackTrace));
        }

        public void EnqueueUnityLog(string message, string stackTrace, TerminalLogType type)
        {
            if (ignoredLogTypes.Contains(type))
            {
                return;
            }
            _pending.Enqueue((message ?? string.Empty, stackTrace ?? string.Empty, type, false));
        }

        public int DrainPending()
        {
            int added = 0;
            while (_pending.TryDequeue(out var item))
            {
                string stack = item.includeStackTrace ? GetAccurateStackTrace() : item.stackTrace;
                if (ignoredLogTypes.Contains(item.type))
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

        public void Resize(int newCapacity)
        {
            if (newCapacity < _logs.Count)
            {
                _version++;
            }
            _logs.Resize(newCapacity);
        }
    }
}
