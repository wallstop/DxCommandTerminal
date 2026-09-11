namespace WallstopStudios.DxCommandTerminal.Backend
{
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
}
