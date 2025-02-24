namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Attributes;
    using JetBrains.Annotations;

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

    public readonly struct CommandArg
    {
        public string String { get; }

        // ReSharper disable once UnusedMember.Global
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

        // ReSharper disable once UnusedMember.Global
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

        // ReSharper disable once UnusedMember.Global
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

        // ReSharper disable once UnusedMember.Global
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

        public CommandArg(string stringValue)
        {
            String = stringValue;
        }

        public override string ToString()
        {
            return String;
        }

        private void TypeError([CallerMemberName] string expectedType = null)
        {
            Terminal.Shell?.IssueErrorMessage(
                $"Incorrect type for {String}, expected <{expectedType}>"
            );
        }
    }

    public sealed class CommandShell
    {
        private static readonly Lazy<(MethodInfo, RegisterCommandAttribute)[]> RegisteredCommands =
            new(() =>
            {
                List<(MethodInfo, RegisterCommandAttribute)> commands = new();
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
                                    StringComparison.OrdinalIgnoreCase
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

                        attribute.NormalizeName(method);
                        commands.Add((method, attribute));
                    }
                }

                return commands.ToArray();
            });
        public IReadOnlyDictionary<string, CommandInfo> Commands => _commands;

        public IReadOnlyDictionary<string, CommandArg> Variables => _variables;

        public bool HasErrors => _errorMessages.Any();

        private readonly Dictionary<string, CommandInfo> _commands = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, CommandArg> _variables = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly List<CommandArg> _arguments = new(); // Cache for performance

        private readonly Queue<string> _errorMessages = new();

        public bool TryConsumeErrorMessage(out string errorMessage)
        {
            return _errorMessages.TryDequeue(out errorMessage);
        }

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
            Dictionary<string, CommandInfo> rejectedCommands = new(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (
                (MethodInfo method, RegisterCommandAttribute attribute) in RegisteredCommands.Value
            )
            {
                string commandName = attribute.Name;
                ParameterInfo[] methodsParams = method.GetParameters();

                if (
                    methodsParams.Length != 1
                    || methodsParams[0].ParameterType != typeof(CommandArg[])
                    || ignoredCommandSet.Contains(commandName)
                )
                {
                    // Method does not match expected Action signature,
                    // this could be a command that has a FrontCommand method to handle its arguments.
                    rejectedCommands.TryAdd(
                        commandName,
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

            HandleRejectedCommands(rejectedCommands);
        }

        /// <summary>
        /// Parses an input line into a command and runs that command.
        /// </summary>
        public void RunCommand(string line)
        {
            string remaining = line;
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
                    string variableName = argument.String.Substring(1);

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

            string commandName = _arguments[0].String ?? string.Empty;
            commandName = commandName.Replace(" ", string.Empty);
            _arguments.RemoveAt(0); // Remove command name from arguments

            if (!_commands.ContainsKey(commandName))
            {
                IssueErrorMessage($"Command {commandName} could not be found");
                return;
            }

            RunCommand(commandName, _arguments.ToArray());
        }

        public void RunCommand(string commandName, CommandArg[] arguments)
        {
            commandName = commandName?.Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(commandName))
            {
                IssueErrorMessage($"Invalid command name '{commandName}'");
                return;
            }

            if (!_commands.TryGetValue(commandName, out CommandInfo command))
            {
                IssueErrorMessage($"Command {commandName} not found");
                return;
            }

            int argCount = arguments.Length;
            string errorMessage = null;
            int requiredArg = 0;

            if (argCount < command.minArgCount)
            {
                errorMessage = command.minArgCount == command.maxArgCount ? "exactly" : "at least";
                requiredArg = command.minArgCount;
            }
            else if (command.maxArgCount >= 0 && argCount > command.maxArgCount)
            {
                // Do not check max allowed number of arguments if it is -1
                errorMessage = command.minArgCount == command.maxArgCount ? "exactly" : "at most";
                requiredArg = command.maxArgCount;
            }

            if (errorMessage != null)
            {
                string pluralFix = requiredArg == 1 ? "" : "s";

                string invalidMessage =
                    $"{commandName} requires {errorMessage} {requiredArg} argument{pluralFix}";
                if (!string.IsNullOrWhiteSpace(command.hint))
                {
                    invalidMessage += $"\n    -> Usage: {command.hint}";
                }
                _errorMessages.Enqueue(invalidMessage);

                return;
            }

            command.proc?.Invoke(arguments);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public void AddCommand(string name, CommandInfo info)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Command Name: {name}");
                return;
            }

            name = name.Replace(" ", string.Empty);
            if (!_commands.TryAdd(name, info))
            {
                IssueErrorMessage($"Command {name} is already defined.");
            }
        }

        // ReSharper disable once MemberCanBePrivate.Global
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
            value ??= string.Empty;
            SetVariable(name, new CommandArg(value));
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public void SetVariable(string name, CommandArg value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Variable Name: {name}");
                return;
            }

            name = name.Replace(" ", string.Empty);
            _variables[name] = value;
        }

        // ReSharper disable once UnusedMember.Global
        public bool TryGetVariable(string name, out CommandArg variable)
        {
            name = name?.Replace(" ", string.Empty) ?? string.Empty;
            return _variables.TryGetValue(name, out variable);
        }

        [StringFormatMethod("format")]
        public void IssueErrorMessage(string format, params object[] message)
        {
            string formattedMessage =
                (message is { Length: > 0 } ? string.Format(format, message) : format)
                ?? string.Empty;
            _errorMessages.Enqueue(formattedMessage);
        }

        private void HandleRejectedCommands(Dictionary<string, CommandInfo> rejectedCommands)
        {
            foreach (KeyValuePair<string, CommandInfo> command in rejectedCommands)
            {
                if (_commands.TryGetValue(command.Key, out CommandInfo existingCommand))
                {
                    _commands[command.Key] = new CommandInfo(
                        existingCommand.proc,
                        command.Value.minArgCount,
                        command.Value.maxArgCount,
                        command.Value.help,
                        command.Value.hint
                    );
                }
                else
                {
                    IssueErrorMessage($"{command.Key} is missing a front command.");
                }
            }
        }

        private static CommandInfo CommandFromParamInfo(ParameterInfo[] parameters, string help)
        {
            int optionalArgs = parameters.Count(param => param.IsOptional);

            return new CommandInfo(
                null,
                parameters.Length - optionalArgs,
                parameters.Length,
                help,
                string.Empty
            );
        }

        private static CommandArg EatArgument(ref string stringValue)
        {
            int spaceIndex = stringValue.IndexOf(' ');

            if (spaceIndex >= 0)
            {
                CommandArg arg = new(stringValue.Substring(0, spaceIndex));
                stringValue =
                    spaceIndex == stringValue.Length - 1
                        ? string.Empty
                        : stringValue.Substring(spaceIndex + 1); // Remaining
                return arg;
            }
            else
            {
                CommandArg arg = new(stringValue);
                stringValue = string.Empty;
                return arg;
            }
        }
    }
}
