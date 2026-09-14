namespace WallstopStudios.DxCommandTerminal.Backend
{
    using JetBrains.Annotations;

    public static class Terminal
    {
        public static CommandLog Buffer
        {
            get => TerminalSession.Current.Buffer;
            internal set => TerminalSession.Current.Buffer = value;
        }

        public static CommandShell Shell
        {
            get => TerminalSession.Current.Shell;
            internal set => TerminalSession.Current.Shell = value;
        }

        public static CommandHistory History
        {
            get => TerminalSession.Current.History;
            internal set => TerminalSession.Current.History = value;
        }

        public static CommandAutoComplete AutoComplete
        {
            get => TerminalSession.Current.AutoComplete;
            internal set => TerminalSession.Current.AutoComplete = value;
        }

        [StringFormatMethod("format")]
        public static bool Log(string format, params object[] parameters)
        {
            return Log(TerminalLogType.ShellMessage, format, parameters);
        }

        [StringFormatMethod("format")]
        public static bool Log(TerminalLogType type, string format, params object[] parameters)
        {
            CommandLog buffer = Buffer;
            if (buffer == null)
            {
                return false;
            }

            string formattedMessage = parameters is { Length: > 0 }
                ? string.Format(format, parameters)
                : format;
            return buffer.HandleLog(formattedMessage, type);
        }
    }
}
