namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using Attributes;
    using Themes;
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

            StringBuilder.Clear();
            List<string> names = terminal._themePack._themeNames;
            for (int i = 0; i < names.Count; ++i)
            {
                if (i > 0)
                {
                    StringBuilder.Append(BulkSeparator);
                }
                StringBuilder.Append(ThemeNameHelper.GetFriendlyThemeName(names[i]));
            }
            string themes = StringBuilder.ToString();
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

            StringBuilder.Clear();
            List<Font> fonts = terminal._fontPack._fonts;
            for (int i = 0; i < fonts.Count; ++i)
            {
                if (i > 0)
                {
                    StringBuilder.Append(BulkSeparator);
                }
                Font f = fonts[i];
                StringBuilder.Append(f != null ? f.name : string.Empty);
            }
            string themes = StringBuilder.ToString();
            Terminal.Log(TerminalLogType.Message, themes);
        }

        [RegisterCommand(
            isDefault: true,
            Name = "set-theme",
            Help = "Sets the current Terminal UI theme",
            MinArgCount = 1,
            MaxArgCount = 1,
            Hint = "set-theme <theme>"
        )]
        [CommandCompleter(typeof(Completers.ThemeArgumentCompleter))]
        public static void CommandSetTheme(CommandArg[] args)
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
                return;
            }

            string theme = args[0].contents;
            string friendlyThemeName = ThemeNameHelper.GetFriendlyThemeName(theme);

            if (
                string.Equals(
                    friendlyThemeName,
                    terminal.CurrentFriendlyTheme,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                Terminal.Log(TerminalLogType.Message, $"Theme '{theme}' is already set.");
                return;
            }

            int newThemeIndex = -1;
            foreach (string themeName in ThemeNameHelper.GetPossibleThemeNames(theme))
            {
                newThemeIndex = terminal._themePack._themeNames.FindIndex(existingTheme =>
                    string.Equals(existingTheme, themeName, StringComparison.OrdinalIgnoreCase)
                );
                if (0 <= newThemeIndex)
                {
                    break;
                }
            }

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
            Name = "set-font",
            Help = "Sets the current Terminal UI font",
            MinArgCount = 1,
            MaxArgCount = 1,
            Hint = "set-font <font>"
        )]
        [CommandCompleter(typeof(Completers.FontArgumentCompleter))]
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

            string fontName = args[0].contents ?? string.Empty;
            if (string.IsNullOrWhiteSpace(fontName))
            {
                Terminal.Log(TerminalLogType.Warning, "Invalid font name.");
                return;
            }

            Font font = null;
            foreach (Font existingFont in terminal._fontPack._fonts)
            {
                if (
                    existingFont != null
                    && string.Equals(
                        existingFont.name,
                        fontName,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    font = existingFont;
                    break;
                }
            }

            if (font == null)
            {
                Terminal.Log(TerminalLogType.Warning, $"Font '{fontName}' not found.");
                return;
            }

            terminal.SetFont(font);
            Terminal.Log(TerminalLogType.Message, $"Font '{font.name}' set.");
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
                $"Current terminal theme is '{terminal.CurrentFriendlyTheme}'."
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

        // [RegisterCommand(
        //     isDefault: true,
        //     Name = "set-font",
        //     Help = "Sets the current Terminal UI font",
        //     MinArgCount = 1,
        //     MaxArgCount = 1
        // )]
        // public static void CommandSetFont(CommandArg[] args)
        // {
        //     TerminalUI terminal = TerminalUI.Instance;
        //     if (terminal == null)
        //     {
        //         Terminal.Log(TerminalLogType.Warning, "No Terminal UI found.");
        //         return;
        //     }
        //
        //     if (terminal._fontPack == null)
        //     {
        //         Terminal.Log(TerminalLogType.Warning, "No font pack found.");
        //         return;
        //     }
        //
        //     string fontName = args[0].contents;
        //
        //     int newFontIndex = terminal._fontPack._fonts.FindIndex(font =>
        //         string.Equals(font.name, fontName, StringComparison.OrdinalIgnoreCase)
        //     );
        //     if (newFontIndex < 0)
        //     {
        //         Terminal.Log(TerminalLogType.Warning, $"Font '{fontName}' not found.");
        //         return;
        //     }
        //
        //     Font font = terminal._fontPack._fonts[newFontIndex];
        //
        //     terminal.SetFont(font);
        //     Terminal.Log(TerminalLogType.Message, $"Font '{font.name}' set.");
        // }

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
            if (font == null)
            {
                Terminal.Log(TerminalLogType.Warning, "No fonts available to select.");
                return;
            }
            Terminal.Log(
                TerminalLogType.Message,
                $"Randomly selected and set font to '{font.name}'."
            );
        }

        [RegisterCommand(
            isDefault: true,
            Name = "clear-console",
            Help = "Clear the command console",
            MaxArgCount = 0
        )]
        public static void CommandClearConsole(CommandArg[] args)
        {
            Terminal.Buffer?.Clear();
        }

        [RegisterCommand(
            isDefault: true,
            Name = "clear-history",
            Help = "Clear the command console's history",
            MaxArgCount = 0,
            IncludeInHistory = false
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
                    string name = command.Key;
                    CommandInfo info = command.Value;
                    string usage = BuildUsage(name, info.minArgCount, info.maxArgCount, info.hint);
                    string helpLine =
                        $"{name.ToUpperInvariant(), -16}: {info.help}\n    -> {usage}";
                    Terminal.Log(helpLine);
                    UnityEngine.Debug.Log(helpLine);
                }
                return;
            }
            else
            {
                string commandName = args[0].contents ?? string.Empty;

                if (!shell.Commands.TryGetValue(commandName, out CommandInfo info))
                {
                    shell.IssueErrorMessage($"Command {commandName} could not be found.");
                    return;
                }

                string usageLine = BuildUsage(
                    commandName,
                    info.minArgCount,
                    info.maxArgCount,
                    info.hint
                );
                if (string.IsNullOrWhiteSpace(info.help))
                {
                    string line = $"{commandName}: {usageLine}";
                    Terminal.Log(line);
                    UnityEngine.Debug.Log(line);
                }
                else
                {
                    string line = $"{info.help}\n{usageLine}";
                    Terminal.Log(line);
                    UnityEngine.Debug.Log(line);
                }
            }
        }

        private static string BuildUsage(string name, int minArgs, int maxArgs, string hint)
        {
            if (!string.IsNullOrWhiteSpace(hint))
            {
                return $"Usage: {hint}";
            }

            StringBuilder.Clear();
            StringBuilder.Append("Usage: ");
            StringBuilder.Append(name);
            if (minArgs <= 0 && (maxArgs == 0 || maxArgs < 0))
            {
                return StringBuilder.ToString();
            }

            int max = maxArgs < 0 ? minArgs : Math.Max(minArgs, maxArgs);
            for (int i = 1; i <= minArgs; ++i)
            {
                StringBuilder.Append(' ');
                StringBuilder.Append('<');
                StringBuilder.Append("arg");
                StringBuilder.Append(i);
                StringBuilder.Append('>');
            }
            for (int i = minArgs + 1; i <= max; ++i)
            {
                StringBuilder.Append(' ');
                StringBuilder.Append('[');
                StringBuilder.Append("arg");
                StringBuilder.Append(i);
                StringBuilder.Append(']');
            }
            if (maxArgs < 0)
            {
                StringBuilder.Append(' ');
                StringBuilder.Append("[args...]");
            }

            return StringBuilder.ToString();
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
            Name = "time-scale",
            Help = "Sets Time.timeScale",
            MinArgCount = 1,
            MaxArgCount = 1
        )]
        public static void CommandTimeScale(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            CommandArg arg = args[0];
            if (!arg.TryGet(out float timeScale))
            {
                Terminal.Log(TerminalLogType.Warning, $"Invalid time scale {arg}.");
                return;
            }

            Time.timeScale = timeScale;
        }

        [RegisterCommand(
            isDefault: true,
            Name = "log-terminal",
            Help = "Output message via Terminal.Log"
        )]
        public static void CommandLogTerminal(CommandArg[] args)
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

            if (string.IsNullOrWhiteSpace(logItem.stackTrace))
            {
                Terminal.Log(
                    logItem.message.EndsWith(" (no trace)", StringComparison.OrdinalIgnoreCase)
                        ? logItem.message
                        : $"{logItem.message} (no trace)"
                );
            }
            else
            {
                Terminal.Log(logItem.stackTrace);
            }
        }

        [RegisterCommand(
            isDefault: true,
            Name = "clear-variable",
            Help = "Clears a variable value",
            MinArgCount = 1,
            MaxArgCount = 1
        )]
        public static void CommandClearVariable(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            string variableName = args[0].contents;
            bool cleared = shell.ClearVariable(variableName);
            if (cleared)
            {
                Terminal.Log($"Variable '{variableName}' cleared successfully.");
            }
            else
            {
                Terminal.Log(
                    TerminalLogType.Warning,
                    $"Warning: Variable '{variableName}' not found."
                );
            }
        }

        [RegisterCommand(
            isDefault: true,
            Name = "clear-all-variables",
            Help = "Clears all variable values",
            MinArgCount = 0,
            MaxArgCount = 0
        )]
        public static void CommandClearAllVariable(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            int variableCount = shell.Variables.Count;
            List<string> variableNames = new List<string>(shell.Variables.Keys);
            foreach (string variable in variableNames)
            {
                shell.ClearVariable(variable);
            }

            Terminal.Log(
                variableCount == 0
                    ? "No variables found - nothing to clear."
                    : $"Cleared {variableCount} variables."
            );
        }

        [RegisterCommand(
            isDefault: true,
            Name = "set-variable",
            Help = "Sets a variable value",
            MinArgCount = 2,
            MaxArgCount = 2
        )]
        public static void CommandSetVariable(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            string variableName = args[0].contents;

            if (string.IsNullOrWhiteSpace(variableName) || variableName.StartsWith('$'))
            {
                Terminal.Log(
                    TerminalLogType.Warning,
                    $"Warning: Possibly invalid variable name '{variableName}'."
                );
            }

            string variableValue = JoinArguments(args, 1);
            bool set = shell.SetVariable(variableName, variableValue);
            if (set)
            {
                Terminal.Log($"Variable '{variableName}' set to '{variableValue}' successfully.");
            }
            else if (shell.Variables.TryGetValue(variableName, out CommandArg existingVariable))
            {
                Terminal.Log(
                    TerminalLogType.Warning,
                    $"Variable '{variableName}' failed to set. Existing value: {existingVariable}."
                );
            }
            else
            {
                Terminal.Log(
                    TerminalLogType.Warning,
                    $"Variable '{variableName}' failed to set. No existing value found."
                );
            }
        }

        [RegisterCommand(
            isDefault: true,
            Name = "get-variable",
            Help = "Gets a variable value",
            MinArgCount = 1,
            MaxArgCount = 1
        )]
        public static void CommandGetVariable(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            string variableName = args[0].contents;

            if (shell.Variables.TryGetValue(variableName, out CommandArg variable))
            {
                Terminal.Log($"Variable '{variableName}' is set to '{variable}'.");
            }
            else
            {
                Terminal.Log(TerminalLogType.Warning, $"Variable '{variableName}' not found.");
            }
        }

        [RegisterCommand(
            isDefault: true,
            Name = "list-variables",
            Help = "Gets all variables and their associated values",
            MinArgCount = 0,
            MaxArgCount = 0
        )]
        public static void CommandGetAllVariables(CommandArg[] args)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            if (shell.Variables.Count == 0)
            {
                Terminal.Log(TerminalLogType.Warning, "No variables found.");
                return;
            }

            foreach (KeyValuePair<string, CommandArg> entry in shell.Variables)
            {
                Terminal.Log($"Variable '{entry.Key}' is set to '{entry.Value}'.");
            }
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
