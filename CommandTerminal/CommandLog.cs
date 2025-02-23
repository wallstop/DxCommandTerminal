namespace CommandTerminal
{
    using System.Collections.Generic;
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
            this.message = message;
            this.stackTrace = stackTrace;
        }
    }

    public sealed class CommandLog
    {
        private readonly List<LogItem> _logs = new();
        private readonly int _maxItems;

        public IReadOnlyList<LogItem> Logs => _logs;

        public CommandLog(int maxItems)
        {
            _maxItems = maxItems;
        }

        public void HandleLog(string message, TerminalLogType type)
        {
            HandleLog(message, string.Empty, type);
        }

        public void HandleLog(string message, string stackTrace, TerminalLogType type)
        {
            LogItem log = new(type, message, stackTrace);

            _logs.Add(log);

            if (_logs.Count > _maxItems)
            {
                _logs.RemoveRange(0, _logs.Count - _maxItems);
            }
        }

        public void Clear()
        {
            _logs.Clear();
        }
    }
}
