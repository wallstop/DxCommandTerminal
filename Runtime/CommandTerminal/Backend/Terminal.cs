namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;
    using JetBrains.Annotations;
    using UnityEngine;

    public static class Terminal
    {
        public static CommandLog Buffer { get; internal set; }
        public static CommandShell Shell { get; internal set; }
        public static CommandHistory History { get; internal set; }
        public static CommandAutoComplete AutoComplete { get; internal set; }

        public static IReadOnlyList<Font> AvailableFonts { get; internal set; } = new List<Font>();

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
