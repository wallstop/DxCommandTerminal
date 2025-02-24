namespace CommandTerminal
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using Attributes;

    // ReSharper disable once UnusedType.Global
    internal static class BuiltInCommands
    {
        [RegisterCommand(Help = "Clear the command console", MaxArgCount = 0)]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        private static void CommandClear(CommandArg[] args)
        {
            Terminal.Buffer?.Clear();
        }

        [RegisterCommand(Help = "Display help information about a command", MaxArgCount = 1)]
        // ReSharper disable once UnusedMember.Local
        private static void CommandHelp(CommandArg[] args)
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
                    Terminal.Log($"{command.Key, -16}: {command.Value.help}");
                }
                return;
            }

            string commandName = args[0].String ?? string.Empty;

            if (!shell.Commands.TryGetValue(commandName, out CommandInfo info))
            {
                shell.IssueErrorMessage($"Command {commandName} could not be found.");
                return;
            }

            if (info.help == null)
            {
                Terminal.Log($"{commandName} does not provide any help documentation.");
            }
            else if (info.hint == null)
            {
                Terminal.Log(info.help);
            }
            else
            {
                Terminal.Log($"{info.help}\nUsage: {info.hint}");
            }
        }

        [RegisterCommand(Help = "Time the execution of a command", MinArgCount = 1)]
        // ReSharper disable once UnusedMember.Local
        private static void CommandTime(CommandArg[] args)
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

        [RegisterCommand(Help = "Output message")]
        // ReSharper disable once UnusedMember.Local
        private static void CommandPrint(CommandArg[] args)
        {
            Terminal.Log(JoinArguments(args));
        }

        [RegisterCommand(Help = "Output the stack trace of the previous message", MaxArgCount = 0)]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        private static void CommandTrace(CommandArg[] args)
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

        [RegisterCommand(Help = "List all variables or set a variable value")]
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once UnusedMember.Local
        private static void CommandSet(CommandArg[] args)
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

            string variableName = args[0].String;

            if (variableName[0] == '$')
            {
                Terminal.Log(
                    TerminalLogType.Warning,
                    $"Warning: Variable name starts with '$', '${variableName}'."
                );
            }

            shell.SetVariable(variableName, JoinArguments(args, 1));
        }

        [RegisterCommand(Help = "No operation")]
        // ReSharper disable once UnusedParameter.Local
        // ReSharper disable once UnusedMember.Local
        private static void CommandNoop(CommandArg[] args)
        {
            // No-op
        }

        [RegisterCommand(Help = "Quit running application", MaxArgCount = 0)]
        // ReSharper disable once UnusedMember.Local
        // ReSharper disable once UnusedParameter.Local
        private static void CommandQuit(CommandArg[] args)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static string JoinArguments(CommandArg[] args, int start = 0)
        {
            StringBuilder sb = new();
            int argLength = args.Length;

            for (int i = start; i < argLength; i++)
            {
                sb.Append(args[i].String);

                if (i < argLength - 1)
                {
                    sb.Append(" ");
                }
            }

            return sb.ToString();
        }
    }
}
