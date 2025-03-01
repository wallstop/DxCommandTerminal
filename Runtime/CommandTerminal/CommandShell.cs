namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Attributes;
    using JetBrains.Annotations;
    using UnityEngine;

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

    public delegate bool CommandArgParser<T>(string input, out T parsed);

    public readonly struct CommandArg
    {
        private static readonly Lazy<MethodInfo> TryGetMethod = new(
            () =>
                typeof(CommandArg)
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(method => method.Name == nameof(TryGet))
                    .FirstOrDefault(method => method.GetParameters().Length == 1)
        );
        private static readonly Dictionary<Type, object> RegisteredParsers = new();
        private static readonly Dictionary<
            Type,
            Dictionary<string, PropertyInfo>
        > StaticProperties = new();
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> ConstFields = new();
        private static readonly Dictionary<Type, object> EnumValues = new();

        // Public to allow custom-mutation, if desired
        public static readonly HashSet<char> Delimiters = new() { ',', ';', ':', '_', '/', '\\' };
        public static readonly HashSet<char> Quotes = new() { '"', '\'' };
        public static readonly HashSet<string> IgnoredValuesForAllTypes = new();
        public static readonly HashSet<Type> DoNotCleanTypes = new()
        {
            typeof(string),
            typeof(char),
            typeof(DateTime),
            typeof(DateTimeOffset),
        };
        public static readonly HashSet<string> IgnoredValuesForComplexTypes = new()
        {
            "(",
            ")",
            "[",
            "]",
            "'",
            "`",
            "|",
            "{",
            "}",
            "<",
            ">",
        };

        public string String { get; }

        public string CleanedString
        {
            get
            {
                string cleanedString = String;
                cleanedString = String.Replace(
                    " ",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase
                );
                cleanedString = IgnoredValuesForAllTypes.Aggregate(
                    cleanedString,
                    (current, ignoredValue) =>
                        current.Replace(
                            ignoredValue,
                            string.Empty,
                            StringComparison.OrdinalIgnoreCase
                        )
                );
                cleanedString = cleanedString.Trim();
                return cleanedString;
            }
        }

        public bool TryGet(Type type, out object parsed)
        {
            // TODO: Convert into delegates and cache for performance
            MethodInfo genericMethod = TryGetMethod.Value;
            if (genericMethod == null)
            {
                parsed = default;
                return false;
            }

            MethodInfo constructed = genericMethod.MakeGenericMethod(type);
            object[] parameters = { null };
            bool success = (bool)constructed.Invoke(this, parameters);
            parsed = parameters[0];
            return success;
        }

        public bool TryGet<T>(out T parsed)
        {
            return TryGet(out parsed, parserOverride: null);
        }

        public bool TryGet<T>(out T parsed, CommandArgParser<T> parserOverride)
        {
            Type type = typeof(T);
            string stringValue = DoNotCleanTypes.Contains(type) ? String : CleanedString;

            if (parserOverride != null)
            {
                return parserOverride(stringValue, out parsed);
            }

            if (TryGetParser(out CommandArgParser<T> parser))
            {
                return parser(stringValue, out parsed);
            }

            if (type == typeof(string))
            {
                parsed = (T)Convert.ChangeType(stringValue, type);
                return true;
            }
            if (TryGetTypeDefined(stringValue, out parsed))
            {
                return true;
            }

            // TODO: Slap into a dictionary of built-in type -> parser mapping
            if (type == typeof(bool))
            {
                return InnerParse<bool>(stringValue, bool.TryParse, out parsed);
            }
            if (type == typeof(float))
            {
                return InnerParse<float>(stringValue, float.TryParse, out parsed);
            }
            if (type == typeof(int))
            {
                return InnerParse<int>(stringValue, int.TryParse, out parsed);
            }
            if (type == typeof(uint))
            {
                return InnerParse<uint>(stringValue, uint.TryParse, out parsed);
            }
            if (type == typeof(long))
            {
                return InnerParse<long>(stringValue, long.TryParse, out parsed);
            }
            if (type == typeof(ulong))
            {
                return InnerParse<ulong>(stringValue, ulong.TryParse, out parsed);
            }
            if (type == typeof(double))
            {
                return InnerParse<double>(stringValue, double.TryParse, out parsed);
            }
            if (type == typeof(short))
            {
                return InnerParse<short>(stringValue, short.TryParse, out parsed);
            }
            if (type == typeof(ushort))
            {
                return InnerParse<ushort>(stringValue, ushort.TryParse, out parsed);
            }
            if (type == typeof(byte))
            {
                return InnerParse<byte>(stringValue, byte.TryParse, out parsed);
            }
            if (type == typeof(sbyte))
            {
                return InnerParse<sbyte>(stringValue, sbyte.TryParse, out parsed);
            }
            if (type == typeof(Guid))
            {
                return InnerParse<Guid>(stringValue, Guid.TryParse, out parsed);
            }
            if (type == typeof(DateTime))
            {
                return InnerParse<DateTime>(stringValue, DateTime.TryParse, out parsed);
            }
            if (type == typeof(DateTimeOffset))
            {
                return InnerParse<DateTimeOffset>(stringValue, DateTimeOffset.TryParse, out parsed);
            }
            if (type == typeof(char))
            {
                return InnerParse<char>(stringValue, char.TryParse, out parsed);
            }
            if (type == typeof(decimal))
            {
                return InnerParse<decimal>(stringValue, decimal.TryParse, out parsed);
            }
            if (type.IsEnum)
            {
                if (Enum.IsDefined(type, stringValue))
                {
                    bool parseOk = Enum.TryParse(type, stringValue, out object parsedObject);
                    if (parseOk)
                    {
                        parsed = (T)Convert.ChangeType(parsedObject, type);
                        return true;
                    }
                }

                if (int.TryParse(stringValue, out int enumIntValue))
                {
                    if (!EnumValues.TryGetValue(type, out object enumValues))
                    {
                        enumValues = Enum.GetValues(type).OfType<T>().ToArray();
                        EnumValues[type] = enumValues;
                    }

                    T[] values = (T[])enumValues;
                    if (0 <= enumIntValue && enumIntValue < values.Length)
                    {
                        parsed = values[enumIntValue];
                        return true;
                    }
                }
            }
            if (type == typeof(Vector2))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y):
                        parsed = (T)Convert.ChangeType(new Vector2(x, y), type);
                        return true;
                    case 3
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z):
                        parsed = (T)Convert.ChangeType((Vector2)new Vector3(x, y, z), type);
                        return true;
                }
            }
            else if (type == typeof(Vector3))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y):
                        parsed = (T)Convert.ChangeType(new Vector3(x, y), type);
                        return true;
                    case 3
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z):
                        parsed = (T)Convert.ChangeType(new Vector3(x, y, z), type);
                        return true;
                }
            }
            else if (type == typeof(Vector4))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y):
                        parsed = (T)Convert.ChangeType(new Vector4(x, y), type);
                        return true;
                    case 3
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z):
                        parsed = (T)Convert.ChangeType(new Vector4(x, y, z), type);
                        return true;
                    case 4
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z)
                            && float.TryParse(split[3], out float w):
                        parsed = (T)Convert.ChangeType(new Vector4(x, y, z, w), type);
                        return true;
                }
            }
            else if (type == typeof(Vector2Int))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when int.TryParse(split[0], out int x) && int.TryParse(split[1], out int y):
                        parsed = (T)Convert.ChangeType(new Vector2Int(x, y), type);
                        return true;
                    case 3
                        when int.TryParse(split[0], out int x)
                            && int.TryParse(split[1], out int y)
                            && int.TryParse(split[2], out int z):
                        parsed = (T)Convert.ChangeType((Vector2Int)new Vector3Int(x, y, z), type);
                        return true;
                }
            }
            else if (type == typeof(Vector3Int))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when int.TryParse(split[0], out int x) && int.TryParse(split[1], out int y):
                        parsed = (T)Convert.ChangeType(new Vector3Int(x, y), type);
                        return true;
                    case 3
                        when int.TryParse(split[0], out int x)
                            && int.TryParse(split[1], out int y)
                            && int.TryParse(split[2], out int z):
                        parsed = (T)Convert.ChangeType(new Vector3Int(x, y, z), type);
                        return true;
                }
            }
            else if (type == typeof(Color))
            {
                string colorString = stringValue;
                if (colorString.StartsWith("RGBA", StringComparison.OrdinalIgnoreCase))
                {
                    colorString = colorString.Replace(
                        "RGBA",
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                string[] split = StripAndSplit(colorString);
                switch (split.Length)
                {
                    case 3
                        when float.TryParse(split[0], out float r)
                            && float.TryParse(split[1], out float g)
                            && float.TryParse(split[2], out float b):
                        parsed = (T)Convert.ChangeType(new Color(r, g, b), type);
                        return true;
                    case 4
                        when float.TryParse(split[0], out float r)
                            && float.TryParse(split[1], out float g)
                            && float.TryParse(split[2], out float b)
                            && float.TryParse(split[3], out float a):
                        parsed = (T)Convert.ChangeType(new Color(r, g, b, a), type);
                        return true;
                }
            }
            else if (type == typeof(Quaternion))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z)
                            && float.TryParse(split[3], out float w):
                        parsed = (T)Convert.ChangeType(new Quaternion(x, y, z, w), type);
                        return true;
                }
            }
            else if (type == typeof(Rect))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when float.TryParse(
                            split[0]
                                .Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                            out float x
                        )
                            && float.TryParse(
                                split[1]
                                    .Replace(
                                        "y:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float y
                            )
                            && float.TryParse(
                                split[2]
                                    .Replace(
                                        "width:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float width
                            )
                            && float.TryParse(
                                split[3]
                                    .Replace(
                                        "height:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float height
                            ):
                        parsed = (T)Convert.ChangeType(new Rect(x, y, width, height), type);
                        return true;
                }
            }
            else if (type == typeof(RectInt))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when int.TryParse(
                            split[0]
                                .Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                            out int x
                        )
                            && int.TryParse(
                                split[1]
                                    .Replace(
                                        "y:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int y
                            )
                            && int.TryParse(
                                split[2]
                                    .Replace(
                                        "width:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int width
                            )
                            && int.TryParse(
                                split[3]
                                    .Replace(
                                        "height:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int height
                            ):
                        parsed = (T)Convert.ChangeType(new RectInt(x, y, width, height), type);
                        return true;
                }
            }

            parsed = default;
            return false;

            static bool InnerParse<TParsed>(
                string input,
                CommandArgParser<TParsed> typedParser,
                out T parsed
            )
            {
                bool parseOk = typedParser(input, out TParsed value);
                if (parseOk)
                {
                    parsed = (T)Convert.ChangeType(value, typeof(T));
                }
                else
                {
                    parsed = default;
                }

                return parseOk;
            }

            static string[] StripAndSplit(string input)
            {
                string strippedInput = input;
                foreach (string ignored in IgnoredValuesForComplexTypes)
                {
                    if (string.IsNullOrEmpty(ignored))
                    {
                        continue;
                    }

                    strippedInput = strippedInput.Replace(
                        ignored,
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                foreach (char delimiter in Delimiters)
                {
                    if (strippedInput.Contains(delimiter))
                    {
                        return strippedInput.Split(delimiter);
                    }
                }

                return new[] { strippedInput };
            }

            static bool TryGetTypeDefined(string input, out T value)
            {
                Type type = typeof(T);
                if (
                    !StaticProperties.TryGetValue(
                        type,
                        out Dictionary<string, PropertyInfo> properties
                    )
                )
                {
                    properties = LoadStaticPropertiesForType<T>();
                    StaticProperties[type] = properties;
                }

                if (properties.TryGetValue(input, out PropertyInfo property))
                {
                    object resolved = property.GetValue(null);
                    value = (T)Convert.ChangeType(resolved, type);
                    return true;
                }

                if (!ConstFields.TryGetValue(type, out Dictionary<string, FieldInfo> fields))
                {
                    fields = LoadStaticFieldsForType<T>();
                    ConstFields[type] = fields;
                }

                if (fields.TryGetValue(input, out FieldInfo field))
                {
                    object resolved = field.GetValue(null);
                    value = (T)Convert.ChangeType(resolved, type);
                    return true;
                }

                value = default;
                return false;
            }
        }

        public CommandArg(string stringValue)
        {
            String = stringValue ?? string.Empty;
        }

        public static bool RegisterParser<T>(CommandArgParser<T> parser, bool force = false)
        {
            Type type = typeof(T);
            if (force)
            {
                RegisteredParsers[type] = parser;
                return true;
            }

            return RegisteredParsers.TryAdd(type, parser);
        }

        public static bool TryGetParser<T>(out CommandArgParser<T> parser)
        {
            if (RegisteredParsers.TryGetValue(typeof(T), out object untypedParser))
            {
                parser = (CommandArgParser<T>)untypedParser;
                return true;
            }

            parser = null;
            return false;
        }

        public static bool UnregisterParser<T>()
        {
            return UnregisterParser(typeof(T));
        }

        public static bool UnregisterParser(Type type)
        {
            return RegisteredParsers.Remove(type);
        }

        public static int UnregisterAllParsers()
        {
            int parserCount = RegisteredParsers.Count;
            RegisteredParsers.Clear();
            return parserCount;
        }

        private static Dictionary<string, PropertyInfo> LoadStaticPropertiesForType<T>()
        {
            Type type = typeof(T);
            return type.GetProperties(BindingFlags.Static | BindingFlags.Public)
                .Where(property => property.PropertyType == type)
                .ToDictionary(
                    property => property.Name,
                    property => property,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private static Dictionary<string, FieldInfo> LoadStaticFieldsForType<T>()
        {
            Type type = typeof(T);
            return type.GetFields(BindingFlags.Static | BindingFlags.Public)
                .Where(field => field.FieldType == type)
                .ToDictionary(
                    field => field.Name,
                    field => field,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        public override string ToString()
        {
            return String;
        }
    }

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

            Assembly[] ourAssembly = { typeof(CommandShell).Assembly };
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
        public void RegisterCommands(
            IEnumerable<string> ignoredCommands = null,
            bool ignoreDefaultCommands = false
        )
        {
            _commands.Clear();
            HashSet<string> ignoredCommandSet = new(
                ignoredCommands ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase
            );
            Dictionary<string, MethodInfo> rejectedCommands = new(StringComparer.OrdinalIgnoreCase);

            foreach (
                (MethodInfo method, RegisterCommandAttribute attribute) in RegisteredCommands.Value
            )
            {
                string commandName = attribute.Name;
                if (ignoredCommandSet.Contains(commandName))
                {
                    continue;
                }

                if (ignoreDefaultCommands && attribute.Default)
                {
                    continue;
                }

                ParameterInfo[] methodsParams = method.GetParameters();
                if (
                    methodsParams.Length != 1
                    || methodsParams[0].ParameterType != typeof(CommandArg[])
                )
                {
                    rejectedCommands.TryAdd(commandName, method);
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
                if (!TryEatArgument(ref remaining, out CommandArg argument))
                {
                    continue;
                }

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
            commandName = commandName.Replace(
                " ",
                string.Empty,
                StringComparison.OrdinalIgnoreCase
            );
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
            commandName = commandName?.Replace(
                " ",
                string.Empty,
                StringComparison.OrdinalIgnoreCase
            );
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

            name = name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
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

            name = name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            _variables[name] = value;
        }

        // ReSharper disable once UnusedMember.Global
        public bool TryGetVariable(string name, out CommandArg variable)
        {
            name =
                name?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
                ?? string.Empty;
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

        private void HandleRejectedCommands(Dictionary<string, MethodInfo> rejectedCommands)
        {
            foreach (KeyValuePair<string, MethodInfo> command in rejectedCommands)
            {
                IssueErrorMessage(
                    $"{command.Key} has an invalid signature. "
                        + $"Expected: {command.Value.Name}(CommandArg[]). "
                        + $"Found: {command.Value.Name}({string.Join(",", command.Value.GetParameters().Select(p => p.ParameterType.Name))})"
                );
            }
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
                    arg = new CommandArg(input);
                    stringValue = string.Empty;
                }
                else
                {
                    // Extract the argument inside the quotes.
                    string input = stringValue.Substring(1, closingQuoteIndex - 1);
                    arg = new CommandArg(input);
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
