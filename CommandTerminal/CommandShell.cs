namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Attributes;

    public readonly struct CommandInfo
    {
        public readonly Action<CommandArg[]> proc;
        public readonly int minArgCount;
        public readonly int maxArgCount;
        public readonly string help;
        public readonly string hint;

        public CommandInfo(
            Action<CommandArg[]> proc,
            int minArgCount,
            int maxArgCount,
            string help,
            string hint
        )
        {
            this.proc = proc;
            this.maxArgCount = maxArgCount;
            this.minArgCount = minArgCount;
            this.help = help;
            this.hint = hint;
        }
    }

    public struct CommandArg
    {
        public string String { get; set; }

        public int Int
        {
            get
            {
                if (int.TryParse(String, out int intValue))
                {
                    return intValue;
                }

                TypeError();
                return 0;
            }
        }

        public float Float
        {
            get
            {
                if (float.TryParse(String, out float floatValue))
                {
                    return floatValue;
                }

                TypeError();
                return 0;
            }
        }

        public bool Bool
        {
            get
            {
                if (bool.TryParse(String, out bool boolValue))
                {
                    return boolValue;
                }

                TypeError();
                return false;
            }
        }

        public T Enum<T>()
            where T : struct, Enum
        {
            if (System.Enum.TryParse(String, out T enumValue))
            {
                return enumValue;
            }

            TypeError();
            return default;
        }

        public override string ToString()
        {
            return String;
        }

        private void TypeError([CallerMemberName] string expectedType = null)
        {
            Terminal.Shell.IssueErrorMessage(
                "Incorrect type for {0}, expected <{1}>",
                String,
                expectedType
            );
        }
    }

    public sealed class CommandShell
    {
        private readonly Dictionary<string, CommandInfo> _commands = new();
        private readonly Dictionary<string, CommandArg> _variables = new();
        private readonly List<CommandArg> _arguments = new(); // Cache for performance

        public string IssuedErrorMessage { get; private set; }

        public IReadOnlyDictionary<string, CommandInfo> Commands => _commands;

        public IReadOnlyDictionary<string, CommandArg> Variables => _variables;

        /// <summary>
        /// Uses reflection to find all RegisterCommand attributes
        /// and adds them to the commands dictionary.
        /// </summary>
        public void RegisterCommands(IEnumerable<string> ignoredCommands = null)
        {
            HashSet<string> ignoredCommandSet = new(
                ignoredCommands ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase
            );
            Dictionary<string, CommandInfo> rejectedCommands = new();
            const BindingFlags methodFlags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (
                Type type in AppDomain
                    .CurrentDomain.GetAssemblies()
                    .SelectMany(assembly => assembly.GetTypes())
            )
            {
                foreach (MethodInfo method in type.GetMethods(methodFlags))
                {
                    if (
                        Attribute.GetCustomAttribute(method, typeof(RegisterCommandAttribute))
                        is not RegisterCommandAttribute attribute
                    )
                    {
                        if (
                            method.Name.StartsWith(
                                "FRONTCOMMAND",
                                StringComparison.CurrentCultureIgnoreCase
                            )
                        )
                        {
                            // Front-end Command methods don't implement RegisterCommand, use default attribute
                            attribute = new RegisterCommandAttribute();
                        }
                        else
                        {
                            continue;
                        }
                    }

                    ParameterInfo[] methodsParams = method.GetParameters();

                    string commandName = InferFrontCommandName(method.Name);

                    // Use the method's name as the command's name
                    commandName = attribute.Name ?? InferCommandName(commandName ?? method.Name);

                    if (
                        methodsParams.Length != 1
                        || methodsParams[0].ParameterType != typeof(CommandArg[])
                        || ignoredCommandSet.Contains(commandName)
                    )
                    {
                        // Method does not match expected Action signature,
                        // this could be a command that has a FrontCommand method to handle its arguments.
                        rejectedCommands.Add(
                            commandName.ToUpper(),
                            CommandFromParamInfo(methodsParams, attribute.Help)
                        );
                        continue;
                    }

                    // Convert MethodInfo to Action.
                    // This is essentially allows us to store a reference to the method,
                    // which makes calling the method significantly more performant than using MethodInfo.Invoke().
                    Action<CommandArg[]> proc =
                        (Action<CommandArg[]>)
                            Delegate.CreateDelegate(typeof(Action<CommandArg[]>), method);
                    AddCommand(
                        commandName,
                        proc,
                        attribute.MinArgCount,
                        attribute.MaxArgCount,
                        attribute.Help,
                        attribute.Hint
                    );
                }
            }
            HandleRejectedCommands(rejectedCommands);
        }

        /// <summary>
        /// Parses an input line into a command and runs that command.
        /// </summary>
        public void RunCommand(string line)
        {
            string remaining = line;
            IssuedErrorMessage = null;
            _arguments.Clear();

            while (!string.IsNullOrWhiteSpace(remaining))
            {
                CommandArg argument = EatArgument(ref remaining);

                if (string.IsNullOrWhiteSpace(argument.String))
                {
                    continue;
                }

                if (argument.String[0] == '$')
                {
                    string variableName = argument.String.Substring(1).ToUpper();

                    if (_variables.TryGetValue(variableName, out CommandArg variable))
                    {
                        // Replace variable argument if it's defined
                        argument = variable;
                    }
                }
                _arguments.Add(argument);
            }

            if (_arguments.Count == 0)
            {
                // Nothing to run
                return;
            }

            string commandName = _arguments[0].String.ToUpper();
            _arguments.RemoveAt(0); // Remove command name from arguments

            if (!_commands.ContainsKey(commandName))
            {
                IssueErrorMessage("Command {0} could not be found", commandName);
                return;
            }

            RunCommand(commandName, _arguments.ToArray());
        }

        public void RunCommand(string commandName, CommandArg[] arguments)
        {
            CommandInfo command = _commands[commandName];
            int argCount = arguments.Length;
            string errorMessage = null;
            int required_arg = 0;

            if (argCount < command.minArgCount)
            {
                errorMessage = command.minArgCount == command.maxArgCount ? "exactly" : "at least";
                required_arg = command.minArgCount;
            }
            else if (command.maxArgCount > -1 && argCount > command.maxArgCount)
            {
                // Do not check max allowed number of arguments if it is -1
                errorMessage = command.minArgCount == command.maxArgCount ? "exactly" : "at most";
                required_arg = command.maxArgCount;
            }

            if (errorMessage != null)
            {
                string pluralFix = required_arg == 1 ? "" : "s";

                IssueErrorMessage(
                    "{0} requires {1} {2} argument{3}",
                    commandName,
                    errorMessage,
                    required_arg,
                    pluralFix
                );

                if (!string.IsNullOrWhiteSpace(command.hint))
                {
                    IssuedErrorMessage += $"\n    -> Usage: {command.hint}";
                }

                return;
            }

            command.proc(arguments);
        }

        public void AddCommand(string name, CommandInfo info)
        {
            name = name.ToUpper();

            if (!_commands.TryAdd(name, info))
            {
                IssueErrorMessage("Command {0} is already defined.", name);
            }
        }

        public void AddCommand(
            string name,
            Action<CommandArg[]> proc,
            int minArgs = 0,
            int maxArgs = -1,
            string help = "",
            string hint = null
        )
        {
            CommandInfo info = new(proc, minArgs, maxArgs, help, hint);
            AddCommand(name, info);
        }

        public void SetVariable(string name, string value)
        {
            SetVariable(name, new CommandArg() { String = value });
        }

        public void SetVariable(string name, CommandArg value)
        {
            name = name.ToUpper();
            _variables[name] = value;
        }

        public CommandArg GetVariable(string name)
        {
            name = name.ToUpper();

            if (_variables.TryGetValue(name, out CommandArg variable))
            {
                return variable;
            }

            IssueErrorMessage("No variable named {0}", name);
            return new CommandArg();
        }

        public void IssueErrorMessage(string format, params object[] message)
        {
            IssuedErrorMessage = string.Format(format, message);
        }

        private static string InferCommandName(string methodName)
        {
            int index = methodName.IndexOf("COMMAND", StringComparison.CurrentCultureIgnoreCase);

            // Method is prefixed, suffixed with, or contains "COMMAND".
            string commandName = index >= 0 ? methodName.Remove(index, 7) : methodName;

            return commandName;
        }

        private static string InferFrontCommandName(string methodName)
        {
            int index = methodName.IndexOf("FRONT", StringComparison.CurrentCultureIgnoreCase);
            return index >= 0 ? methodName.Remove(index, 5) : null;
        }

        private void HandleRejectedCommands(Dictionary<string, CommandInfo> rejectedCommands)
        {
            foreach (KeyValuePair<string, CommandInfo> command in rejectedCommands)
            {
                if (_commands.ContainsKey(command.Key))
                {
                    _commands[command.Key] = new CommandInfo(
                        _commands[command.Key].proc,
                        command.Value.minArgCount,
                        command.Value.maxArgCount,
                        command.Value.help,
                        command.Value.hint
                    );
                }
                else
                {
                    IssueErrorMessage("{0} is missing a front command.", command);
                }
            }
        }

        private static CommandInfo CommandFromParamInfo(ParameterInfo[] parameters, string help)
        {
            int optionalArgs = 0;

            foreach (ParameterInfo param in parameters)
            {
                if (param.IsOptional)
                {
                    optionalArgs += 1;
                }
            }

            return new CommandInfo(
                null,
                parameters.Length - optionalArgs,
                parameters.Length,
                help,
                string.Empty
            );
        }

        private static CommandArg EatArgument(ref string s)
        {
            CommandArg arg = new();
            int spaceIndex = s.IndexOf(' ');

            if (spaceIndex >= 0)
            {
                arg.String = s.Substring(0, spaceIndex);
                s = s.Substring(spaceIndex + 1); // Remaining
            }
            else
            {
                arg.String = s;
                s = string.Empty;
            }

            return arg;
        }
    }
}
