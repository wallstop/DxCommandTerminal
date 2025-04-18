namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Attributes;
    using JetBrains.Annotations;
    using UnityEngine;

    public sealed class CommandShell
    {
        public static readonly Lazy<(
            MethodInfo method,
            RegisterCommandAttribute attribute
        )[]> RegisteredCommands = new(() =>
        {
            List<(MethodInfo, RegisterCommandAttribute)> commands = new();
            const BindingFlags methodFlags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            Assembly[] ourAssembly = { typeof(BuiltInCommands).Assembly };
            foreach (
                Type type in AppDomain
                    .CurrentDomain.GetAssemblies()
                    /*
                        Force our assembly to be processed last so user commands,
                        if they conflict with in-built ones, are always registered first.
                     */
                    .Except(ourAssembly)
                    .Concat(ourAssembly)
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
                        continue;
                    }

                    attribute.NormalizeName(method);
                    commands.Add((method, attribute));
                }
            }

            return commands.ToArray();
        });
        public IReadOnlyDictionary<string, CommandInfo> Commands => _commands;
        public IReadOnlyDictionary<string, CommandArg> Variables => _variables;
        public ImmutableHashSet<string> AutoRegisteredCommands => _immutableAutoRegisteredCommands;
        public ImmutableHashSet<string> IgnoredCommands => _immutableIgnoredCommands;
        public bool IgnoringDefaultCommands { get; private set; }

        public bool HasErrors => 0 < _errorMessages.Count;

        private readonly CommandHistory _history;

        private readonly SortedDictionary<string, CommandInfo> _commands = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly SortedDictionary<string, CommandArg> _variables = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly List<CommandArg> _arguments = new(); // Cache for performance
        private readonly Queue<string> _errorMessages = new();
        private readonly StringBuilder _commandBuilder = new();
        private readonly HashSet<string> _ignoredCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _autoRegisteredCommands = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly SortedDictionary<string, MethodInfo> _rejectedCommands = new(
            StringComparer.OrdinalIgnoreCase
        );

        private ImmutableHashSet<string> _immutableAutoRegisteredCommands =
            ImmutableHashSet<string>.Empty;
        private ImmutableHashSet<string> _immutableIgnoredCommands = ImmutableHashSet<string>.Empty;

        public CommandShell(CommandHistory history)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
        }

        public bool TryConsumeErrorMessage(out string errorMessage)
        {
            return _errorMessages.TryDequeue(out errorMessage);
        }

        public int ClearAllCommands()
        {
            return ClearAutoRegisteredCommands() + ClearCustomCommands();
        }

        public int ClearCustomCommands()
        {
            int count = _commands.Count;
            _commands.Clear();
            return count;
        }

        //public bool RemoveCommand

        public int ClearAutoRegisteredCommands()
        {
            int count = _autoRegisteredCommands.Count;
            foreach (string command in _autoRegisteredCommands)
            {
                _commands.Remove(command);
            }
            _autoRegisteredCommands.Clear();
            _immutableAutoRegisteredCommands = ImmutableHashSet<string>.Empty;
            return count;
        }

        public void InitializeAutoRegisteredCommands(
            IEnumerable<string> ignoredCommands = null,
            bool ignoreDefaultCommands = false
        )
        {
            IgnoringDefaultCommands = ignoreDefaultCommands;
            ClearAutoRegisteredCommands();
            _ignoredCommands.Clear();
            _ignoredCommands.UnionWith(ignoredCommands ?? Enumerable.Empty<string>());
            foreach (string ignoredCommand in _ignoredCommands)
            {
                _commands.Remove(ignoredCommand);
            }
            _immutableIgnoredCommands = _ignoredCommands.ToImmutableHashSet(
                StringComparer.OrdinalIgnoreCase
            );
            _rejectedCommands.Clear();

            foreach (
                (MethodInfo method, RegisterCommandAttribute attribute) in RegisteredCommands.Value
            )
            {
                string commandName = attribute.Name;
                if (_ignoredCommands.Contains(commandName))
                {
                    continue;
                }

                if (ignoreDefaultCommands && attribute.Default)
                {
                    continue;
                }

                if (attribute.EditorOnly && !Application.isEditor)
                {
                    continue;
                }

                if (attribute.DevelopmentOnly && !Application.isEditor && !Debug.isDebugBuild)
                {
                    continue;
                }

                ParameterInfo[] methodsParams = method.GetParameters();
                if (
                    methodsParams.Length != 1
                    || methodsParams[0].ParameterType != typeof(CommandArg[])
                )
                {
                    _rejectedCommands.TryAdd(commandName, method);
                    continue;
                }

                // Perf boost, much cheaper than running reflection on invoking the method
                Action<CommandArg[]> proc =
                    (Action<CommandArg[]>)
                        Delegate.CreateDelegate(typeof(Action<CommandArg[]>), method);
                bool success = AddCommand(
                    commandName,
                    proc,
                    minArgs: attribute.MinArgCount,
                    maxArgs: attribute.MaxArgCount,
                    help: attribute.Help,
                    hint: attribute.Hint
                );
                if (success)
                {
                    _autoRegisteredCommands.Add(commandName);
                }
            }

            _immutableAutoRegisteredCommands = _autoRegisteredCommands.ToImmutableHashSet(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (KeyValuePair<string, MethodInfo> command in _rejectedCommands)
            {
                IssueErrorMessage(
                    $"{command.Key} has an invalid signature. "
                        + $"Expected: {command.Value.Name}(CommandArg[]). "
                        + $"Found: {command.Value.Name}({string.Join(",", command.Value.GetParameters().Select(p => p.ParameterType.Name))})"
                );
            }
        }

        /// <summary>
        /// Parses an input line into a command and runs that command.
        /// </summary>
        public bool RunCommand(string line)
        {
            string remaining = line;
            _arguments.Clear();

            while (!string.IsNullOrWhiteSpace(remaining))
            {
                if (!TryEatArgument(ref remaining, out CommandArg argument))
                {
                    continue;
                }

                string argumentString = argument.contents;
                if (argument.endQuote == null)
                {
                    if (string.IsNullOrWhiteSpace(argumentString))
                    {
                        continue;
                    }
                    if (argumentString.StartsWith('$'))
                    {
                        string variableName = argumentString[1..];

                        if (_variables.TryGetValue(variableName, out CommandArg variable))
                        {
                            // Replace variable argument if it's defined
                            argument = variable;
                        }
                    }
                }

                _arguments.Add(argument);
            }

            if (_arguments.Count == 0)
            {
                _history.Push(line, false, true);
                return false;
            }

            string commandName = _arguments[0].contents ?? string.Empty;
            // Remove command name from arguments
            _arguments.RemoveAt(0);

            return RunCommand(
                commandName,
                _arguments.Count == 0 ? Array.Empty<CommandArg>() : _arguments.ToArray()
            );
        }

        public bool RunCommand(string commandName, CommandArg[] arguments)
        {
            _commandBuilder.Clear();
            _commandBuilder.Append(commandName);
            if (arguments.Length != 0)
            {
                _commandBuilder.Append(' ');
            }

            for (int i = 0; i < arguments.Length; ++i)
            {
                CommandArg argument = arguments[i];
                if (argument.startQuote != null)
                {
                    _commandBuilder.Append(argument.startQuote.Value);
                }
                _commandBuilder.Append(argument.contents);
                if (argument.endQuote != null)
                {
                    _commandBuilder.Append(argument.endQuote.Value);
                }

                if (i != arguments.Length - 1)
                {
                    _commandBuilder.Append(' ');
                }
            }

            string line = _commandBuilder.ToString();

            if (string.IsNullOrWhiteSpace(commandName))
            {
                IssueErrorMessage($"Invalid command name '{commandName}'");
                // Don't log empty commands
                return false;
            }

            if (commandName.Contains(' '))
            {
                commandName = commandName.Replace(
                    " ",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase
                );
            }

            if (!_commands.TryGetValue(commandName, out CommandInfo command))
            {
                IssueErrorMessage($"Command {commandName} not found");
                _history.Push(line, false, false);
                return false;
            }

            int argCount = arguments.Length;
            string errorMessage = null;
            int requiredArg = 0;

            if (argCount < command.minArgCount)
            {
                errorMessage = command.minArgCount == command.maxArgCount ? "exactly" : "at least";
                requiredArg = command.minArgCount;
            }
            else if (0 <= command.maxArgCount && command.maxArgCount < argCount)
            {
                // Do not check max allowed number of arguments if it is -1
                errorMessage = command.minArgCount == command.maxArgCount ? "exactly" : "at most";
                requiredArg = command.maxArgCount;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                string pluralFix = requiredArg == 1 ? "" : "s";

                string invalidMessage =
                    $"{commandName} requires {errorMessage} {requiredArg} argument{pluralFix}";
                if (!string.IsNullOrWhiteSpace(command.hint))
                {
                    invalidMessage += $"\n    -> Usage: {command.hint}";
                }
                _errorMessages.Enqueue(invalidMessage);
                _history.Push(line, false, false);
                return false;
            }

            int errorCount = _errorMessages.Count;
            command.proc?.Invoke(arguments);
            _history.Push(line, true, errorCount == _errorMessages.Count);
            return true;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool AddCommand(string name, CommandInfo info)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Command Name: {name}");
                return false;
            }

            if (name.Contains(' '))
            {
                name = name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (!_commands.TryAdd(name, info))
            {
                IssueErrorMessage($"Command {name} is already defined.");
                return false;
            }

            return true;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool AddCommand(
            string name,
            Action<CommandArg[]> proc,
            int minArgs = 0,
            int maxArgs = -1,
            string help = "",
            string hint = null
        )
        {
            CommandInfo info = new(proc, minArgs, maxArgs, help, hint);
            return AddCommand(name, info);
        }

        public bool SetVariable(string name, string value)
        {
            value ??= string.Empty;
            return SetVariable(name, new CommandArg(value));
        }

        public bool ClearVariable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Variable Name: {name}");
                return false;
            }

            if (name.Contains(' '))
            {
                name = name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            return _variables.Remove(name);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool SetVariable(string name, CommandArg value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Variable Name: {name}");
                return false;
            }

            if (name.Contains(' '))
            {
                name = name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            _variables[name] = value;
            return true;
        }

        // ReSharper disable once UnusedMember.Global
        public bool TryGetVariable(string name, out CommandArg variable)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                variable = default;
                return false;
            }

            if (name.Contains(' '))
            {
                name =
                    name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
                    ?? string.Empty;
            }

            return _variables.TryGetValue(name, out variable);
        }

        [StringFormatMethod("format")]
        public void IssueErrorMessage(string format, params object[] parameters)
        {
            string formattedMessage =
                (parameters is { Length: > 0 } ? string.Format(format, parameters) : format)
                ?? string.Empty;
            _errorMessages.Enqueue(formattedMessage);
        }

        public static bool TryEatArgument(ref string stringValue, out CommandArg arg)
        {
            stringValue = stringValue.TrimStart();
            if (stringValue.Length == 0)
            {
                arg = default;
                return false;
            }

            char firstChar = stringValue[0];
            if (CommandArg.Quotes.Contains(firstChar))
            {
                int closingQuoteIndex = -1;

                // Find the matching closing quote.
                for (int i = 1; i < stringValue.Length; ++i)
                {
                    if (stringValue[i] == firstChar)
                    {
                        closingQuoteIndex = i;
                        break;
                    }
                }

                if (closingQuoteIndex < 0)
                {
                    // No closing quote was found; consume the rest of the string (excluding the opening quote).
                    string input = stringValue.Substring(1);
                    arg = new CommandArg(input, startQuote: firstChar);
                    stringValue = string.Empty;
                }
                else
                {
                    // Extract the argument inside the quotes.
                    string input = stringValue.Substring(1, closingQuoteIndex - 1);
                    arg = new CommandArg(input, startQuote: firstChar, endQuote: firstChar);
                    // Remove the parsed argument (including the quotes) from the input.
                    stringValue = stringValue.Substring(closingQuoteIndex + 1);
                }
            }
            else
            {
                // Unquoted argument: find the next space.
                int spaceIndex = stringValue.IndexOf(' ');
                if (spaceIndex < 0)
                {
                    arg = new CommandArg(stringValue);
                    stringValue = string.Empty;
                }
                else
                {
                    string input = stringValue.Substring(0, spaceIndex);
                    arg = new CommandArg(input);
                    stringValue = stringValue.Substring(spaceIndex + 1);
                }
            }

            return true;
        }
    }
}
