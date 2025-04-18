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

            if (terminal._themePack == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No theme pack found.");
                return;
            }

            string themes = string.Join(BulkSeparator, terminal._themePack._themeNames);
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

            if (terminal._fontPack == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No font pack found.");
                return;
            }

            string themes = string.Join(
                BulkSeparator,
                terminal._fontPack._fonts.Select(font => font.name)
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

            if (string.Equals(theme, terminal.CurrentTheme, StringComparison.OrdinalIgnoreCase))
            {
                Terminal.Log(TerminalLogType.Message, $"Theme '{theme}' is already set.");
                return;
            }

            int newThemeIndex = terminal._themePack._themeNames.IndexOf(theme);
            if (newThemeIndex < 0)
            {
                Terminal.Log(TerminalLogType.Warning, $"Theme '{theme}' not found.");
                return;
            }

            terminal.SetTheme(theme);
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
                $"Current terminal theme is '{terminal.CurrentTheme}'."
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

            Font currentFont = terminal.CurrentFont;
            Terminal.Log(
                TerminalLogType.Message,
                $"Current terminal font is '{(currentFont == null ? "null" : currentFont.name)}'."
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

            string newTheme = terminal.SetRandomTheme();
            Terminal.Log(
                TerminalLogType.Message,
                $"Randomly selected and set theme to '{newTheme}'."
            );
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

            if (terminal._fontPack == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No font pack found.");
                return;
            }

            string fontName = args[0].contents;

            int newFontIndex = terminal._fontPack._fonts.FindIndex(font =>
                string.Equals(font.name, fontName, StringComparison.OrdinalIgnoreCase)
            );
            if (newFontIndex < 0)
            {
                Terminal.Log(TerminalLogType.Warning, $"Font '{fontName}' not found.");
                return;
            }

            Font font = terminal._fontPack._fonts[newFontIndex];

            terminal.SetFont(font);
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

            Font font = terminal.SetRandomFont();
            Terminal.Log(
                TerminalLogType.Message,
                $"Randomly selected and set font to '{font.name}'."
            );
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
