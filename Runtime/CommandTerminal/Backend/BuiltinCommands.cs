namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text;
    using Attributes;
    using UI;
    using UnityEngine;

    // ReSharper disable once UnusedType.Global
    public static class BuiltInCommands
    {
        private const string BulkSeparator = "    ";

        private static readonly StringBuilder StringBuilder = new();
        private static readonly System.Random Random = new();

        [RegisterCommand(
            isDefault: true,
            Name = "list-themes",
            Help = "Lists all currently available themes",
            MaxArgCount = 0
        )]
        public static void CommandListThemes(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            string themes = string.Join(BulkSeparator, terminal._loadedThemes);
            Terminal.Log(TerminalLogType.Message, themes);
        }

        [RegisterCommand(
            isDefault: true,
            Name = "list-fonts",
            Help = "Lists all currently available fonts",
            MaxArgCount = 0
        )]
        public static void CommandListFonts(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            string themes = string.Join(
                BulkSeparator,
                terminal._loadedFonts.Select(font => font.name)
            );
            Terminal.Log(TerminalLogType.Message, themes);
        }

        [RegisterCommand(
            isDefault: true,
            Name = "set-theme",
            Help = "Sets the current Terminal UI theme",
            MinArgCount = 1,
            MaxArgCount = 1
        )]
        public static void CommandSetTheme(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            string theme = args[0].contents;

            if (string.Equals(theme, terminal._currentTheme, StringComparison.OrdinalIgnoreCase))
            {
                Terminal.Log(TerminalLogType.Message, $"Theme '{theme}' is already set.");
                return;
            }

            int newThemeIndex = terminal._loadedThemes.IndexOf(theme);
            if (newThemeIndex < 0)
            {
                Terminal.Log(TerminalLogType.Warning, $"Theme '{theme}' not found.");
                return;
            }

            terminal.SetTheme(theme, persist: true);
            Terminal.Log(TerminalLogType.Message, $"Theme '{theme}' set.");
        }

        [RegisterCommand(
            isDefault: true,
            Name = "get-theme",
            Help = "Gets the current Terminal UI theme"
        )]
        public static void CommandGetTheme(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            Terminal.Log(
                TerminalLogType.Message,
                $"Current terminal theme is '{terminal._currentTheme}'."
            );
        }

        [RegisterCommand(
            isDefault: true,
            Name = "get-font",
            Help = "Gets the current Terminal UI font"
        )]
        public static void CommandGetFont(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            Terminal.Log(
                TerminalLogType.Message,
                $"Current terminal font is '{(terminal._font == null ? "null" : terminal._font.name)}'."
            );
        }

        [RegisterCommand(
            isDefault: true,
            Name = "set-random-theme",
            Help = "Sets the current Terminal UI theme to a randomly selected one",
            MinArgCount = 0,
            MaxArgCount = 0
        )]
        public static void CommandSetRandomTheme(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            if (terminal._loadedThemes.Count == 0)
            {
                Terminal.Log(TerminalLogType.Warning, "No themes found.");
                return;
            }

            int currentThemeIndex = terminal._loadedThemes.IndexOf(terminal._currentTheme);

            int newThemeIndex;
            do
            {
                newThemeIndex = Random.Next(terminal._loadedThemes.Count);
            } while (currentThemeIndex == newThemeIndex && terminal._loadedThemes.Count != 1);

            string theme = terminal._loadedThemes[newThemeIndex];

            terminal.SetTheme(theme, persist: true);
            Terminal.Log(TerminalLogType.Message, $"Randomly selected and set Theme '{theme}'.");
        }

        [RegisterCommand(
            isDefault: true,
            Name = "set-font",
            Help = "Sets the current Terminal UI font",
            MinArgCount = 1,
            MaxArgCount = 1
        )]
        public static void CommandSetFont(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            string fontName = args[0].contents;

            int newFontIndex = terminal._loadedFonts.FindIndex(font =>
                string.Equals(font.name, fontName, StringComparison.OrdinalIgnoreCase)
            );
            if (newFontIndex < 0)
            {
                Terminal.Log(TerminalLogType.Warning, $"Font '{fontName}' not found.");
                return;
            }

            Font font = terminal._loadedFonts[newFontIndex];

            terminal.SetFont(font, persist: true);
            Terminal.Log(TerminalLogType.Message, $"Font '{font.name}' set.");
        }

        [RegisterCommand(
            isDefault: true,
            Name = "set-random-font",
            Help = "Sets the current Terminal UI font to a randomly selected one",
            MinArgCount = 0,
            MaxArgCount = 0
        )]
        public static void CommandSetRandomFont(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            int currentFontIndex = terminal._loadedFonts.IndexOf(terminal._font);

            int newFontIndex;
            do
            {
                newFontIndex = Random.Next(terminal._loadedFonts.Count);
            } while (currentFontIndex == newFontIndex && terminal._loadedFonts.Count != 1);

            Font font = terminal._loadedFonts[newFontIndex];
            terminal.SetFont(font, persist: true);
            Terminal.Log(TerminalLogType.Message, $"Randomly selected and set Font '{font.name}'.");
        }

        [RegisterCommand(
            isDefault: true,
            Name = "clear",
            Help = "Clear the command console",
            MaxArgCount = 0
        )]
        public static void CommandClear(CommandArg[] args)
        {
            Terminal.Buffer?.Clear();
        }

        [RegisterCommand(
            isDefault: true,
            Name = "clear-history",
            Help = "Clear the command console's history",
            MaxArgCount = 0
        )]
        public static void CommandClearHistory(CommandArg[] args)
        {
            Terminal.History?.Clear();
        }

        [RegisterCommand(
            isDefault: true,
            Name = "help",
            Help = "Display help information about a command",
            MaxArgCount = 1
        )]
        public static void CommandHelp(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            if (args.Length == 0)
            {
                foreach (KeyValuePair<string, CommandInfo> command in shell.Commands)
                {
                    Terminal.Log($"{command.Key.ToUpperInvariant(), -16}: {command.Value.help}");
                }
                return;
            }

            string commandName = args[0].contents ?? string.Empty;

            if (!shell.Commands.TryGetValue(commandName, out CommandInfo info))
            {
                shell.IssueErrorMessage($"Command {commandName} could not be found.");
                return;
            }

            if (string.IsNullOrWhiteSpace(info.help))
            {
                Terminal.Log($"{commandName} does not provide any help documentation.");
            }
            else if (string.IsNullOrWhiteSpace(info.hint))
            {
                Terminal.Log(info.help);
            }
            else
            {
                Terminal.Log($"{info.help}\nUsage: {info.hint}");
            }
        }

        [RegisterCommand(
            isDefault: true,
            Name = "time",
            Help = "Time the execution of a command",
            MinArgCount = 1
        )]
        public static void CommandTime(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();
            shell.RunCommand(JoinArguments(args));
            sw.Stop();
            Terminal.Log($"Time: {sw.ElapsedMilliseconds}ms");
        }

        [RegisterCommand(
            isDefault: true,
            Name = "terminal-log",
            Help = "Output message via Terminal.Log"
        )]
        public static void CommandPrint(CommandArg[] args)
        {
            Terminal.Log(JoinArguments(args));
        }

        [RegisterCommand(isDefault: true, Name = "log", Help = "Output message via Debug.Log")]
        public static void CommandLog(CommandArg[] args)
        {
            UnityEngine.Debug.Log(JoinArguments(args));
        }

        [RegisterCommand(
            isDefault: true,
            Name = "trace",
            Help = "Output the stack trace of the previous message",
            MaxArgCount = 0
        )]
        public static void CommandTrace(CommandArg[] args)
        {
            CommandLog buffer = Terminal.Buffer;
            if (buffer == null)
            {
                return;
            }

            int logCount = buffer.Logs.Count;

            if (logCount - 2 < 0)
            {
                Terminal.Log(TerminalLogType.Warning, "Nothing to trace.");
                return;
            }

            LogItem logItem = buffer.Logs[logCount - 2];

            Terminal.Log(
                string.IsNullOrWhiteSpace(logItem.stackTrace)
                    ? $"{logItem.message} (no trace)"
                    : logItem.stackTrace
            );
        }

        [RegisterCommand(
            isDefault: true,
            Name = "set",
            Help = "List all variables or set a variable value"
        )]
        public static void CommandSet(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            if (args.Length == 0)
            {
                foreach (KeyValuePair<string, CommandArg> kv in shell.Variables)
                {
                    Terminal.Log($"{kv.Key, -16}: {kv.Value}");
                }
                return;
            }

            string variableName = args[0].contents;

            if (variableName[0] == '$')
            {
                Terminal.Log(
                    TerminalLogType.Warning,
                    $"Warning: Variable name starts with '$', '${variableName}'."
                );
            }

            shell.SetVariable(variableName, JoinArguments(args, 1));
        }

        [RegisterCommand(isDefault: true, Name = "no-op", Help = "No operation")]
        public static void CommandNoOperation(CommandArg[] args)
        {
            // No-op
        }

        [RegisterCommand(
            isDefault: true,
            Name = "quit",
            Help = "Quit running application",
            MaxArgCount = 0
        )]
        public static void CommandQuit(CommandArg[] args)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }

        private static string JoinArguments(CommandArg[] args, int start = 0)
        {
            StringBuilder.Clear();
            for (int i = start; i < args.Length; i++)
            {
                StringBuilder.Append(args[i].contents);

                if (i < args.Length - 1)
                {
                    StringBuilder.Append(' ');
                }
            }

            return StringBuilder.ToString();
        }
    }
}
