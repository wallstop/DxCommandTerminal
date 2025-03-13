namespace CommandTerminal
{
    using System.Collections.Generic;
    using System.Diagnostics;
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
        public IReadOnlyList<LogItem> Logs => _logs;
        public int Capacity => _logs.Capacity;

        public readonly HashSet<TerminalLogType> ignoredLogTypes;

        private readonly CyclicBuffer<LogItem> _logs;

        public CommandLog(int maxItems, IEnumerable<TerminalLogType> ignoredLogTypes = null)
        {
            _logs = new CyclicBuffer<LogItem>(maxItems);
            this.ignoredLogTypes = new HashSet<TerminalLogType>(
                ignoredLogTypes ?? Enumerable.Empty<TerminalLogType>()
            );
        }

        public bool HandleLog(string message, TerminalLogType type, bool includeStackTrace = false)
        {
            string stackTrace;
            if (includeStackTrace)
            {
                StackTrace stack = new();
                stackTrace = stack.ToString();
            }
            else
            {
                stackTrace = string.Empty;
            }
            return HandleLog(message, stackTrace, type);
        }

        public bool HandleLog(string message, string stackTrace, TerminalLogType type)
        {
            if (ignoredLogTypes.Contains(type))
            {
                return false;
            }

            LogItem log = new(type, message, stackTrace);
            _logs.Add(log);
            return true;
        }

        public int Clear()
        {
            int logCount = _logs.Count;
            _logs.Clear();
            return logCount;
        }

        public void Resize(int newCapacity)
        {
            _logs.Resize(newCapacity);
        }
    }
}
