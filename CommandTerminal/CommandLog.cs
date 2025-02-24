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
            this.message = message ?? string.Empty;
            this.stackTrace = stackTrace ?? string.Empty;
        }
    }

    public sealed class CommandLog
    {
        public IReadOnlyList<LogItem> Logs => _logs;

        private readonly List<LogItem> _logs = new();
        private readonly int _maxItems;

        public CommandLog(int maxItems)
        {
            _maxItems = maxItems < 0 ? 0 : maxItems;
        }

        public bool HandleLog(string message, TerminalLogType type)
        {
            return HandleLog(message, string.Empty, type);
        }

        public bool HandleLog(string message, string stackTrace, TerminalLogType type)
        {
            if (Terminal.IgnoredLogTypes?.Contains(type) == true)
            {
                return false;
            }

            LogItem log = new(type, message, stackTrace);

            _logs.Add(log);

            if (_logs.Count > _maxItems)
            {
                _logs.RemoveRange(0, _logs.Count - _maxItems);
            }

            return true;
        }

        public int Clear()
        {
            int logCount = _logs.Count;
            _logs.Clear();
            return logCount;
        }
    }
}
