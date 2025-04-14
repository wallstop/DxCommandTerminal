namespace CommandTerminal
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using Attributes;

    // ReSharper disable once UnusedType.Global
    public static class BuiltInCommands
    {
        [RegisterCommand(
            Name = "clear",
            Help = "Clear the command console",
            MaxArgCount = 0,
            Default = true
        )]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        public static void CommandClear(CommandArg[] args)
        {
            Terminal.Buffer?.Clear();
        }

        [RegisterCommand(
            Name = "clear-history",
            Help = "Clear the command console's history",
            MaxArgCount = 0,
            Default = true
        )]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        public static void CommandClearHistory(CommandArg[] args)
        {
            Terminal.History?.Clear();
        }

        [RegisterCommand(
            Name = "help",
            Help = "Display help information about a command",
            MaxArgCount = 1,
            Default = true
        )]
        // ReSharper disable once UnusedMember.Local
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
            Name = "time",
            Help = "Time the execution of a command",
            MinArgCount = 1,
            Default = true
        )]
        // ReSharper disable once UnusedMember.Local
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
            Name = "terminal-log",
            Help = "Output message via Terminal.Log",
            Default = true
        )]
        // ReSharper disable once UnusedMember.Local
        public static void CommandPrint(CommandArg[] args)
        {
            Terminal.Log(JoinArguments(args));
        }

        [RegisterCommand(Name = "log", Help = "Output message via Debug.Log", Default = true)]
        // ReSharper disable once UnusedMember.Local
        public static void CommandLog(CommandArg[] args)
        {
            UnityEngine.Debug.Log(JoinArguments(args));
        }

        [RegisterCommand(
            Name = "trace",
            Help = "Output the stack trace of the previous message",
            MaxArgCount = 0,
            Default = true
        )]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
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
                Terminal.Log("Nothing to trace.");
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
            Name = "set",
            Help = "List all variables or set a variable value",
            Default = true
        )]
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once UnusedMember.Local
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

        [RegisterCommand(Name = "no-op", Help = "No operation", Default = true)]
        // ReSharper disable once UnusedParameter.Local
        // ReSharper disable once UnusedMember.Local
        public static void CommandNoOperation(CommandArg[] args)
        {
            // No-op
        }

        [RegisterCommand(
            Name = "quit",
            Help = "Quit running application",
            MaxArgCount = 0,
            Default = true
        )]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
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
            StringBuilder sb = new();
            for (int i = start; i < args.Length; i++)
            {
                sb.Append(args[i].contents);

                if (i < args.Length - 1)
                {
                    sb.Append(" ");
                }
            }

            return sb.ToString();
        }
    }
}
