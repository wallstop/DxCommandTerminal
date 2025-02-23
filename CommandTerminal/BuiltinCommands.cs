namespace CommandTerminal
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using Attributes;

    internal static class RegisterCommands
    {
        [RegisterCommand(Help = "Clear the command console", MaxArgCount = 0)]
        internal static void CommandClear(CommandArg[] args)
        {
            Terminal.Buffer.Clear();
        }

        [RegisterCommand(Help = "Display help information about a command", MaxArgCount = 1)]
        internal static void CommandHelp(CommandArg[] args)
        {
            if (args.Length == 0)
            {
                foreach (KeyValuePair<string, CommandInfo> command in Terminal.Shell.Commands)
                {
                    Terminal.Log("{0}: {1}", command.Key.PadRight(16), command.Value.help);
                }
                return;
            }

            string commandName = args[0].String.ToUpper();

            if (!Terminal.Shell.Commands.TryGetValue(commandName, out CommandInfo info))
            {
                Terminal.Shell.IssueErrorMessage("Command {0} could not be found.", commandName);
                return;
            }

            if (info.help == null)
            {
                Terminal.Log("{0} does not provide any help documentation.", commandName);
            }
            else if (info.hint == null)
            {
                Terminal.Log(info.help);
            }
            else
            {
                Terminal.Log("{0}\nUsage: {1}", info.help, info.hint);
            }
        }

        [RegisterCommand(Help = "Time the execution of a command", MinArgCount = 1)]
        internal static void CommandTime(CommandArg[] args)
        {
            Stopwatch sw = Stopwatch.StartNew();

            Terminal.Shell.RunCommand(JoinArguments(args));

            sw.Stop();
            Terminal.Log("Time: {0}ms", (double)sw.ElapsedMilliseconds);
        }

        [RegisterCommand(Help = "Output message")]
        static void CommandPrint(CommandArg[] args)
        {
            Terminal.Log(JoinArguments(args));
        }

#if DEBUG
        [RegisterCommand(Help = "Output the stack trace of the previous message", MaxArgCount = 0)]
        internal static void CommandTrace(CommandArg[] args)
        {
            int logCount = Terminal.Buffer.Logs.Count;

            if (logCount - 2 < 0)
            {
                Terminal.Log("Nothing to trace.");
                return;
            }

            LogItem logItem = Terminal.Buffer.Logs[logCount - 2];

            if (string.IsNullOrWhiteSpace(logItem.stackTrace))
            {
                Terminal.Log("{0} (no trace)", logItem.message);
            }
            else
            {
                Terminal.Log(logItem.stackTrace);
            }
        }
#endif

        [RegisterCommand(Help = "List all variables or set a variable value")]
        internal static void CommandSet(CommandArg[] args)
        {
            if (args.Length == 0)
            {
                foreach (KeyValuePair<string, CommandArg> kv in Terminal.Shell.Variables)
                {
                    Terminal.Log("{0}: {1}", kv.Key.PadRight(16), kv.Value);
                }
                return;
            }

            string variableName = args[0].String;

            if (variableName[0] == '$')
            {
                Terminal.Log(
                    TerminalLogType.Warning,
                    "Warning: Variable name starts with '$', '${0}'.",
                    variableName
                );
            }

            Terminal.Shell.SetVariable(variableName, JoinArguments(args, 1));
        }

        [RegisterCommand(Help = "No operation")]
        internal static void CommandNoop(CommandArg[] args) { }

        [RegisterCommand(Help = "Quit running application", MaxArgCount = 0)]
        internal static void CommandQuit(CommandArg[] args)
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
