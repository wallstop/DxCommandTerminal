namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using Attributes;
    using DataStructures;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

    public sealed class CommandShell
    {
        private const string CatalogTypeName =
            "WallstopStudios.DxCommandTerminal.Generated.CommandCatalog";

        private const string CatalogCollectMethodName = "Collect";

        private const BindingFlags CatalogBindingFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags ReflectedMethodBindingFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /*
            Generated catalogs expose `public static void Collect(
            List<CommandCatalogEntry> entries)`. Probes and bindings are cached
            per assembly for the lifetime of the domain; the weak table keeps
            unloaded-collectible editor assemblies collectible.
         */
        private static readonly ConditionalWeakTable<
            Assembly,
            StrongBox<Action<List<CommandCatalogEntry>>>
        > CatalogCollectors =
            new ConditionalWeakTable<Assembly, StrongBox<Action<List<CommandCatalogEntry>>>>();

        public static readonly Lazy<(
            MethodInfo method,
            RegisterCommandAttribute attribute
        )[]> RegisteredCommands = new(() =>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Stopwatch stopwatch = Stopwatch.StartNew();
#endif
            List<(MethodInfo, RegisterCommandAttribute)> commands = new();

            Assembly ourAssembly = typeof(BuiltInCommands).Assembly;
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<Assembly> scanCandidates = CollectScanCandidates(loadedAssemblies, ourAssembly);

            for (int i = 0; i < scanCandidates.Count; i++)
            {
                CollectReflectedCommands(scanCandidates[i], commands);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                $"[DxCommandTerminal] Discovered {commands.Count} auto-registered commands in "
                    + $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms (scanned {scanCandidates.Count} of "
                    + $"{loadedAssemblies.Length} loaded assemblies)"
            );
#endif

            return commands.ToArray();
        });

        private readonly List<CommandArg> _arguments = new(); // Cache for performance

        private readonly HashSet<string> _autoRegisteredCommands = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly StringBuilder _commandBuilder = new();

        private readonly SortedDictionary<string, CommandInfo> _commands = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly Queue<string> _errorMessages = new();

        private readonly CommandHistory _history;
        private readonly HashSet<string> _ignoredCommands = new(StringComparer.OrdinalIgnoreCase);

        private readonly SortedDictionary<string, MethodInfo> _rejectedCommands = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly SortedDictionary<string, CommandArg> _variables = new(
            StringComparer.OrdinalIgnoreCase
        );

        public CommandShell(CommandHistory history)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
        }

        public IReadOnlyDictionary<string, CommandInfo> Commands => _commands;
        public IReadOnlyDictionary<string, CommandArg> Variables => _variables;

        public ReadOnlyHashSet<string> AutoRegisteredCommands { get; private set; } =
            ReadOnlyHashSet<string>.Empty;

        public ReadOnlyHashSet<string> IgnoredCommands { get; private set; } =
            ReadOnlyHashSet<string>.Empty;

        public bool IgnoringDefaultCommands { get; private set; }

        public bool HasErrors => 0 < _errorMessages.Count;

        // Internal for test coverage of the discovery filter (see
        // WallstopStudios.DxCommandTerminal.Tests.Runtime).
        internal static bool MayContainCommands(Assembly assembly, AssemblyName self)
        {
            if (assembly.IsDynamic)
            {
                return false;
            }

            try
            {
                AssemblyName[] referencedAssemblies = assembly.GetReferencedAssemblies();
                for (int i = 0; i < referencedAssemblies.Length; i++)
                {
                    if (AssemblyName.ReferenceMatchesDefinition(referencedAssemblies[i], self))
                    {
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // Metadata reads can fail for exotic assemblies; scanning them
                // is cheaper than silently dropping commands they might carry.
                return true;
            }

            return false;
        }

        /*
            Only assemblies that reference this one can contain
            RegisterCommandAttribute, so everything else is skipped without
            loading a single type. Our assembly is processed last so user
            commands, if they conflict with in-built ones, are always
            registered first.
         */
        private static List<Assembly> CollectScanCandidates(
            Assembly[] loadedAssemblies,
            Assembly ourAssembly
        )
        {
            AssemblyName self = ourAssembly.GetName();
            List<Assembly> scanCandidates = new(loadedAssemblies.Length);
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                Assembly assembly = loadedAssemblies[i];
                try
                {
                    if (AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), self))
                    {
                        continue;
                    }

                    if (MayContainCommands(assembly, self))
                    {
                        scanCandidates.Add(assembly);
                    }
                }
                catch (Exception)
                {
                    // Classification must never be able to fail discovery; if
                    // an assembly cannot be classified, scan it.
                    scanCandidates.Add(assembly);
                }
            }

            scanCandidates.Add(ourAssembly);
            return scanCandidates;
        }

        private static void CollectReflectedCommands(
            Assembly assembly,
            List<(MethodInfo method, RegisterCommandAttribute attribute)> commands
        )
        {
            if (!TryGetScanTypes(assembly, out Type[] types))
            {
                return;
            }

            for (int j = 0; j < types.Length; j++)
            {
                Type type = types[j];
                if (type == null)
                {
                    continue;
                }

                try
                {
                    foreach (MethodInfo method in type.GetMethods(ReflectedMethodBindingFlags))
                    {
                        try
                        {
                            if (!method.IsDefined(typeof(RegisterCommandAttribute), false))
                            {
                                continue;
                            }

                            if (
                                Attribute.GetCustomAttribute(
                                    method,
                                    typeof(RegisterCommandAttribute)
                                )
                                is not RegisterCommandAttribute attribute
                            )
                            {
                                continue;
                            }

                            attribute.NormalizeName(method);
                            commands.Add((method, attribute));
                        }
                        catch (Exception e)
                        {
                            Debug.LogError(
                                $"Failed to resolve method {method.Name} of type {type.FullName} with exception {e}"
                            );
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"Failed to resolve methods for type {type.FullName} with exception {e}"
                    );
                }
            }
        }

        /*
            One generated command registration, shared by the generated-catalog
            path and the reflection compatibility path so both apply identical
            filtering, validation, and diagnostics.
         */
        private readonly struct AutoCommand
        {
            public readonly string Name;
            public readonly string MethodName;
            public readonly int MinArgCount;
            public readonly int MaxArgCount;
            public readonly string Help;
            public readonly string Hint;
            public readonly bool AddToHistory;
            public readonly bool EditorOnly;
            public readonly bool DevelopmentOnly;
            public readonly bool IsDefault;

            // Non-null for valid (CommandArg[]) signatures. Null marks a
            // rejected command; diagnostics then come from MethodAccessor.
            public readonly Func<Action<CommandArg[]>> Binder;

            public readonly Func<MethodInfo> MethodAccessor;

            private AutoCommand(
                string name,
                string methodName,
                int minArgCount,
                int maxArgCount,
                string help,
                string hint,
                bool addToHistory,
                bool editorOnly,
                bool developmentOnly,
                bool isDefault,
                Func<Action<CommandArg[]>> binder,
                Func<MethodInfo> methodAccessor
            )
            {
                Name = name;
                MethodName = methodName;
                MinArgCount = minArgCount;
                MaxArgCount = maxArgCount;
                Help = help;
                Hint = hint;
                AddToHistory = addToHistory;
                EditorOnly = editorOnly;
                DevelopmentOnly = developmentOnly;
                IsDefault = isDefault;
                Binder = binder;
                MethodAccessor = methodAccessor;
            }

            public static AutoCommand FromCatalog(CommandCatalogEntry entry)
            {
                return new AutoCommand(
                    entry.Name,
                    entry.MethodName,
                    entry.MinArgCount,
                    entry.MaxArgCount,
                    entry.Help,
                    entry.Hint,
                    entry.AddToHistory,
                    entry.EditorOnly,
                    entry.DevelopmentOnly,
                    entry.IsDefault,
                    entry.Binder,
                    entry.MethodAccessor
                );
            }

            public static AutoCommand FromReflected(
                MethodInfo method,
                RegisterCommandAttribute attribute
            )
            {
                bool valid = IsValidSignature(method);
                return new AutoCommand(
                    attribute.Name,
                    method.Name,
                    attribute.MinArgCount,
                    attribute.MaxArgCount,
                    attribute.Help,
                    attribute.Hint,
                    attribute.AddToHistory,
                    attribute.EditorOnly,
                    attribute.DevelopmentOnly,
                    attribute.Default,
                    valid
                        ? () =>
                            (Action<CommandArg[]>)
                                Delegate.CreateDelegate(typeof(Action<CommandArg[]>), method)
                        : null,
                    () => method
                );
            }

            private static bool IsValidSignature(MethodInfo method)
            {
                ParameterInfo[] methodParams = method.GetParameters();
                return methodParams.Length == 1
                    && methodParams[0].ParameterType == typeof(CommandArg[]);
            }
        }

        /*
            Probes the assembly for its generated `CommandCatalog` and binds its
            Collect method. One targeted metadata lookup per assembly; results
            are cached per assembly for the domain.
         */
        private static bool TryGetCatalogCollector(
            Assembly assembly,
            out Action<List<CommandCatalogEntry>> collector
        )
        {
            if (
                CatalogCollectors.TryGetValue(
                    assembly,
                    out StrongBox<Action<List<CommandCatalogEntry>>> box
                )
            )
            {
                collector = box.Value;
                return collector != null;
            }

            collector = BindCatalogCollector(assembly);
            CatalogCollectors.AddOrUpdate(
                assembly,
                new StrongBox<Action<List<CommandCatalogEntry>>>(collector)
            );
            return collector != null;
        }

        private static Action<List<CommandCatalogEntry>> BindCatalogCollector(Assembly assembly)
        {
            Type catalogType;
            try
            {
                catalogType = assembly.GetType(CatalogTypeName, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Failed to probe for a generated command catalog in "
                        + $"{assembly.GetName().Name}: {e.Message}"
                );
                return null;
            }

            if (catalogType == null)
            {
                return null;
            }

            MethodInfo collectMethod;
            try
            {
                collectMethod = catalogType.GetMethod(
                    CatalogCollectMethodName,
                    CatalogBindingFlags,
                    null,
                    new Type[] { typeof(List<CommandCatalogEntry>) },
                    null
                );
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Found a generated command catalog in "
                        + $"{assembly.GetName().Name} but failed to bind it: {e.Message}"
                );
                return null;
            }

            if (collectMethod == null || collectMethod.ReturnType != typeof(void))
            {
                return null;
            }

            try
            {
                return (Action<List<CommandCatalogEntry>>)
                    Delegate.CreateDelegate(
                        typeof(Action<List<CommandCatalogEntry>>),
                        collectMethod
                    );
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Found a generated command catalog in "
                        + $"{assembly.GetName().Name} but failed to bind it: {e.Message}"
                );
                return null;
            }
        }

        /*
            Collects auto-registered commands for one assembly: from its
            generated catalog when the generator produced one, otherwise from
            the reflection compatibility walk. Never walks types for an
            assembly that produced a non-empty catalog. Returns whether the
            catalog path was used.
         */
        private static bool CollectAutoCommands(Assembly assembly, List<AutoCommand> commands)
        {
            if (!TryGetCatalogCollector(assembly, out Action<List<CommandCatalogEntry>> collector))
            {
                CollectReflectedAutoCommands(assembly, commands);
                return false;
            }

            List<CommandCatalogEntry> entries = new();
            bool collected = false;
            try
            {
                collector(entries);
                collected = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Generated command catalog in "
                        + $"{assembly.GetName().Name} failed to collect: {e.Message}; "
                        + $"falling back to reflection discovery"
                );
            }

            if (collected && 0 < entries.Count)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    commands.Add(AutoCommand.FromCatalog(entries[i]));
                }

                return true;
            }

            // An empty or failed catalog is not proof that the assembly has no
            // commands; the reflection walk below stays compatible with it.
            CollectReflectedAutoCommands(assembly, commands);
            return false;
        }

        private static void CollectReflectedAutoCommands(
            Assembly assembly,
            List<AutoCommand> commands
        )
        {
            List<(MethodInfo method, RegisterCommandAttribute attribute)> reflected = new();
            CollectReflectedCommands(assembly, reflected);
            for (int i = 0; i < reflected.Count; i++)
            {
                commands.Add(
                    AutoCommand.FromReflected(reflected[i].method, reflected[i].attribute)
                );
            }
        }

        private static bool TryGetScanTypes(Assembly assembly, out Type[] types)
        {
            try
            {
                types = assembly.GetTypes();
                return true;
            }
            catch (ReflectionTypeLoadException e)
            {
                // Scan the subset that loaded; one unloadable type must not
                // break discovery for the entire session.
                types = e.Types;
                Debug.LogWarning(
                    $"Some types of assembly {assembly.FullName} failed to load; "
                        + $"command discovery continues with the {e.Types.Length} types that loaded"
                );
                return true;
            }
            catch (Exception e)
            {
                types = Array.Empty<Type>();
                Debug.LogError(
                    $"Failed to enumerate types for assembly {assembly.FullName} with exception {e}"
                );
                return false;
            }
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
            AutoRegisteredCommands = ReadOnlyHashSet<string>.Empty;
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

            IgnoredCommands = _ignoredCommands.ToReadOnlyHashSet(StringComparer.OrdinalIgnoreCase);
            _rejectedCommands.Clear();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Stopwatch stopwatch = Stopwatch.StartNew();
#endif
            int registeredCount = 0;
            int catalogAssemblies = 0;
            int reflectedAssemblies = 0;

            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<Assembly> scanCandidates = CollectScanCandidates(
                loadedAssemblies,
                typeof(BuiltInCommands).Assembly
            );

            List<AutoCommand> autoCommands = new();
            for (int i = 0; i < scanCandidates.Count; i++)
            {
                if (CollectAutoCommands(scanCandidates[i], autoCommands))
                {
                    catalogAssemblies++;
                }
                else
                {
                    reflectedAssemblies++;
                }
            }

            for (int i = 0; i < autoCommands.Count; i++)
            {
                AutoCommand command = autoCommands[i];
                string commandName = command.Name;
                if (_ignoredCommands.Contains(commandName))
                {
                    continue;
                }

                if (ignoreDefaultCommands && command.IsDefault)
                {
                    continue;
                }

                if (command.EditorOnly && !Application.isEditor)
                {
                    continue;
                }

                if (command.DevelopmentOnly && !Application.isEditor && !Debug.isDebugBuild)
                {
                    continue;
                }

                if (command.Binder == null)
                {
                    RegisterRejectedCommand(command, commandName);
                    continue;
                }

                Action<CommandArg[]> proc;
                try
                {
                    proc = command.Binder();
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"[DxCommandTerminal] Failed to bind command {commandName} "
                            + $"(method {command.MethodName}): {e.Message}"
                    );
                    continue;
                }

                if (proc == null)
                {
                    Debug.LogError(
                        $"[DxCommandTerminal] Failed to bind command {commandName} "
                            + $"(method {command.MethodName}): no handler was produced"
                    );
                    continue;
                }

                // Perf boost, much cheaper than running reflection on invoking the method
                bool success = AddCommand(
                    commandName,
                    proc,
                    command.MinArgCount,
                    command.MaxArgCount,
                    command.Help,
                    command.Hint,
                    command.AddToHistory
                );
                if (success)
                {
                    _autoRegisteredCommands.Add(commandName);
                    registeredCount++;
                }
            }

            AutoRegisteredCommands = _autoRegisteredCommands.ToReadOnlyHashSet(
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                $"[DxCommandTerminal] Registered {registeredCount} auto-registered commands in "
                    + $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms "
                    + $"({catalogAssemblies} generated catalog(s), {reflectedAssemblies} reflection-scanned assembly(ies))"
            );
#endif
        }

        private void RegisterRejectedCommand(AutoCommand command, string commandName)
        {
            MethodInfo method = null;
            if (command.MethodAccessor != null)
            {
                try
                {
                    method = command.MethodAccessor();
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[DxCommandTerminal] Failed to resolve rejected command {commandName} "
                            + $"(method {command.MethodName}) for diagnostics: {e.Message}"
                    );
                }
            }

            if (method == null)
            {
                IssueErrorMessage(
                    $"{commandName} has an invalid signature. "
                        + $"Expected: {command.MethodName}(CommandArg[])."
                );
                return;
            }

            _rejectedCommands.TryAdd(commandName, method);
        }

        /// <summary>
        ///     Parses an input line into a command and runs that command.
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
                // No command identified, unconditional push
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
                // Unknown command, unconditional push
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
                // Known command with invalid arguments, respect addToHistory flag
                if (command.addToHistory)
                {
                    _history.Push(line, false, false);
                }
                return false;
            }

            int errorCount = _errorMessages.Count;
            command.proc?.Invoke(arguments);
            // Known command executed, respect addToHistory flag
            if (command.addToHistory)
            {
                _history.Push(line, true, errorCount == _errorMessages.Count);
            }
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
            string hint = null,
            bool addToHistory = true
        )
        {
            CommandInfo info = new(proc, minArgs, maxArgs, help, hint, addToHistory);
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
                    arg = new CommandArg(input, firstChar);
                    stringValue = string.Empty;
                }
                else
                {
                    // Extract the argument inside the quotes.
                    string input = stringValue.Substring(1, closingQuoteIndex - 1);
                    arg = new CommandArg(input, firstChar, firstChar);
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
