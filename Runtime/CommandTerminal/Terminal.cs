namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using JetBrains.Annotations;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;
#endif

    public static class Terminal
    {
        public static CommandLog Buffer { get; internal set; }
        public static CommandShell Shell { get; internal set; }
        public static CommandHistory History { get; internal set; }
        public static CommandAutoComplete AutoComplete { get; internal set; }

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
