namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Helper;

    /// <summary>
    ///     Fluent authoring for one typed command: names, help, hints, history
    ///     policy, execution eligibility, typed arguments, handler, and
    ///     completion all come from one definition, registered into the same
    ///     shell through <see cref="CommandShell.AddCommand(CommandBuilder, out CommandRegistrationHandle)"/>.
    ///     Subcommands route the first argument to nested definitions, so
    ///     <c>inventory add pickaxe</c> and <c>inventory remove pickaxe</c>
    ///     share one registered command.
    /// </summary>
    /// <remarks>
    ///     Argument bounds, the usage hint, and the completion provider derive
    ///     from the declared arguments, so help, validation, and completion
    ///     cannot drift apart. Configuration errors (missing handler,
    ///     duplicate argument names, required-after-optional ordering,
    ///     unparseable argument types, defaults failing their own validation)
    ///     throw at definition time with a diagnostic naming the command;
    ///     user-input mistakes at execution become controlled shell errors
    ///     and never run the handler.
    /// </remarks>
    public sealed class CommandBuilder
    {
        private static readonly CommandArgument[] NoArguments = Array.Empty<CommandArgument>();
        private static readonly object[] NoValues = Array.Empty<object>();

        /// <summary>The command name, matched case-insensitively; spaces are stripped at registration.</summary>
        public string Name { get; }

        private readonly List<CommandArgument> _arguments = new();
        private readonly List<CommandBuilder> _subcommands = new();

        private string _help = string.Empty;
        private string _hint;
        private CommandExecutionContexts _contexts = CommandExecutionContextSets.Gameplay;
        private bool _addToHistory = true;
        private TypedCommandHandler _handler;

        private CommandBuilder(string name)
        {
            Name = name;
        }

        /// <summary>Creates a builder for the named command.</summary>
        public static CommandBuilder Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CommandConfigurationException(
                    "A command name is required; pass a non-empty name to CommandBuilder.Create."
                );
            }

            return new CommandBuilder(name);
        }

        /// <summary>Creates a builder for the named command with help text.</summary>
        public static CommandBuilder Create(string name, string help)
        {
            return Create(name).Help(help);
        }

        private static string NormalizeName(string owner, string name)
        {
            string normalized = name;
            if (normalized != null && normalized.Contains(' '))
            {
                normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new CommandConfigurationException(
                    $"Command '{owner ?? string.Empty}': subcommand names must not be empty.",
                    owner
                );
            }

            return normalized;
        }

        private static void RunTyped(
            CommandShell owner,
            CommandArgument[] specs,
            string name,
            TypedCommandHandler handler,
            CommandExecutionContext context,
            BorrowedCommandArguments arguments,
            int offset
        )
        {
            object[] parsed = specs.Length == 0 ? NoValues : new object[specs.Length];
            for (int i = 0; i < specs.Length; ++i)
            {
                if (arguments.Count <= offset + i)
                {
                    /*
                        The dispatching level guarantees required arguments
                        exist: the shell's derived bounds for top-level
                        commands, the router for subcommands.
                     */
                    parsed[i] = specs[i].GetDefault();
                    continue;
                }

                CommandArg input = arguments[offset + i];
                if (!specs[i].TryParse(input, out object value))
                {
                    owner.IssueErrorMessage($"'{name}': {specs[i].FormatParseError(input)}");
                    return;
                }

                string error = specs[i].ValidateParsed(value);
                if (error != null)
                {
                    owner.IssueErrorMessage($"'{name}': {error}");
                    return;
                }

                parsed[i] = value;
            }

            handler(context, new CommandArguments(specs, parsed, arguments.Slice(offset), context));
        }

        private static string BuildUsageHint(string name, CommandArgument[] specs)
        {
            if (specs.Length == 0)
            {
                return name;
            }

            StringBuilder builder = new(name.Length + 16 * specs.Length);
            builder.Append(name);
            for (int i = 0; i < specs.Length; ++i)
            {
                builder.Append(' ');
                specs[i].AppendUsage(builder);
            }

            return builder.ToString();
        }

        private static CommandCompletionProvider BuildCompletionProvider(CommandArgument[] specs)
        {
            CommandCompletionProvider[] stages = BuildSpecCompletionStages(specs);
            foreach (CommandCompletionProvider stage in stages)
            {
                if (stage != null)
                {
                    return CommandCompletionProviders.Staged(stages);
                }
            }

            /*
                No choices anywhere: leave the provider unset so the command
                keeps the shared history-based completion path.
             */
            return null;
        }

        /*
            One stage per declared argument, in declaration order; stages for
            arguments without choices stay null. The array is always sized to
            specs.Length so route completion can index it directly.
        */
        private static CommandCompletionProvider[] BuildSpecCompletionStages(
            CommandArgument[] specs
        )
        {
            CommandCompletionProvider[] stages = new CommandCompletionProvider[specs.Length];
            for (int i = 0; i < specs.Length; ++i)
            {
                CommandArgument spec = specs[i];
                if (!spec.HasChoices)
                {
                    continue;
                }

                stages[i] = (
                    in CommandCompletionContext context,
                    List<CommandCompletion> results
                ) => spec.AppendCompletions(context, results);
            }

            return stages;
        }

        /*
            Router dispatch and completion: one registered command routes the
            first argument to nested definitions. offset counts how many
            leading arguments belong to the routing levels already matched, so
            the same routine serves every nesting depth.
        */
        private static void RouteInvocation(
            CommandShell owner,
            Dictionary<string, SubcommandRoute> lookup,
            string path,
            TypedCommandHandler fallback,
            CommandExecutionContext context,
            BorrowedCommandArguments arguments,
            int offset
        )
        {
            if (arguments.Count <= offset)
            {
                if (fallback != null)
                {
                    RunTyped(owner, NoArguments, path, fallback, context, arguments, offset);
                    return;
                }

                owner.IssueErrorMessage(
                    $"'{path}': expected a subcommand. Expected one of: {FormatRouteList(lookup)}"
                );
                return;
            }

            CommandArg token = arguments[offset];
            string tokenText = token.contents ?? string.Empty;
            if (!lookup.TryGetValue(tokenText, out SubcommandRoute route))
            {
                owner.IssueErrorMessage(
                    $"'{path}': unknown subcommand '{tokenText}'. "
                        + $"Expected one of: {FormatRouteList(lookup)}"
                );
                return;
            }

            if (route.IsRouter)
            {
                RouteInvocation(
                    owner,
                    route.Lookup,
                    route.FullPath,
                    route.Fallback,
                    context,
                    arguments,
                    offset + 1
                );
                return;
            }

            int available = arguments.Count - (offset + 1);
            if (available < route.RequiredCount)
            {
                owner.IssueErrorMessage(
                    $"'{route.FullPath}': requires at least {route.RequiredCount} argument"
                        + (route.RequiredCount == 1 ? string.Empty : "s")
                        + $"\n    -> Usage: {route.PathUsage}"
                );
                return;
            }

            if (route.Specs.Length < available)
            {
                owner.IssueErrorMessage(
                    $"'{route.FullPath}': expects at most {route.Specs.Length} argument"
                        + (route.Specs.Length == 1 ? string.Empty : "s")
                        + $"\n    -> Usage: {route.PathUsage}"
                );
                return;
            }

            RunTyped(
                owner,
                route.Specs,
                route.FullPath,
                route.Handler,
                context,
                arguments,
                offset + 1
            );
        }

        private static void RouteCompletion(
            Dictionary<string, SubcommandRoute> lookup,
            in CommandCompletionContext context,
            int baseStage,
            List<CommandCompletion> results
        )
        {
            int stage = context.ActiveArgumentIndex - baseStage;
            if (stage == 0)
            {
                string token = context.Token;
                foreach (SubcommandRoute candidate in lookup.Values)
                {
                    if (candidate.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(
                            new CommandCompletion(candidate.Name, description: candidate.Help)
                        );
                    }
                }

                return;
            }

            if (context.PrecedingArguments.Count <= baseStage)
            {
                return;
            }

            CommandArg selector = context.PrecedingArguments[baseStage];
            if (!lookup.TryGetValue(selector.contents ?? string.Empty, out SubcommandRoute route))
            {
                return;
            }

            if (route.IsRouter)
            {
                RouteCompletion(route.Lookup, context, baseStage + 1, results);
                return;
            }

            int childStage = stage - 1;
            if (route.Specs.Length <= childStage)
            {
                return;
            }

            /*
                The routed subcommand owns its argument window: hand the stage
                a subcommand-relative context so dynamic choice providers read
                the same shape as on a top-level command.
            */
            route
                .CompletionStages[childStage]
                ?.Invoke(context.ForSubcommand(baseStage + 1), results);
        }

        private static string FormatRouteList(Dictionary<string, SubcommandRoute> lookup)
        {
            using CachedStringBuilder.Scope scope = new(64);
            StringBuilder builder = scope.Builder;
            bool first = true;
            foreach (SubcommandRoute route in lookup.Values)
            {
                if (!first)
                {
                    builder.Append(", ");
                }

                builder.Append(route.Usage);
                first = false;
            }

            return builder.ToString();
        }

        private static int RequiredCount(CommandArgument[] specs)
        {
            int count = 0;
            foreach (CommandArgument spec in specs)
            {
                if (spec.IsRequired)
                {
                    ++count;
                }
            }

            return count;
        }

        /// <summary>Sets the help text shown by the built-in <c>help</c> command.</summary>
        public CommandBuilder Help(string help)
        {
            _help = help ?? string.Empty;
            return this;
        }

        /// <summary>
        ///     Overrides the derived usage hint. Without this, the hint derives
        ///     from the declared arguments, e.g. <c>heal &lt;amount:int&gt; [target:string]</c>.
        /// </summary>
        public CommandBuilder Hint(string hint)
        {
            _hint = hint;
            return this;
        }

        /// <summary>
        ///     Sets the environments the command is eligible to run in. Defaults
        ///     to gameplay contexts; Edit Mode execution requires explicit opt-in.
        /// </summary>
        public CommandBuilder Contexts(CommandExecutionContexts contexts)
        {
            _contexts = contexts;
            return this;
        }

        /// <summary>Sets whether invocations are recorded in the command history.</summary>
        public CommandBuilder AddToHistory(bool addToHistory)
        {
            _addToHistory = addToHistory;
            return this;
        }

        /// <summary>
        ///     Declares one typed argument. The optional configuration callback
        ///     marks it required, gives it a default, and attaches validation
        ///     and choices. Argument order is declaration order.
        /// </summary>
        public CommandBuilder Arg<T>(
            string name,
            Func<CommandArgumentSpec<T>, CommandArgumentSpec<T>> configure = null
        )
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CommandConfigurationException(
                    $"Command '{Name}': argument names must not be empty.",
                    Name
                );
            }

            foreach (CommandArgument argument in _arguments)
            {
                if (string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandConfigurationException(
                        $"Command '{Name}': duplicate argument name '{name}'.",
                        Name
                    );
                }
            }

            CommandArgumentSpec<T> spec = new(name);
            if (configure != null)
            {
                spec = configure(spec);
                if (spec == null)
                {
                    throw new CommandConfigurationException(
                        $"Command '{Name}': the configuration callback for argument '{name}' returned null.",
                        Name
                    );
                }
            }

            spec.EnsureParseable();
            _arguments.Add(spec);
            return this;
        }

        /// <summary>
        ///     Declares a subcommand routed on this command's first argument.
        ///     The configured builder may itself declare arguments, a handler,
        ///     and further subcommands; it registers with the parent, never
        ///     separately. The parent's execution contexts and history policy
        ///     govern every subcommand, so setting
        ///     <see cref="Contexts"/> or <see cref="AddToHistory"/> inside the
        ///     callback throws, as does declaring parent arguments on a
        ///     command that has subcommands.
        /// </summary>
        public CommandBuilder Subcommand(string name, Action<CommandBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            string normalized = NormalizeName(Name, name);
            foreach (CommandBuilder existing in _subcommands)
            {
                if (string.Equals(existing.Name, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandConfigurationException(
                        $"Command '{Name}': duplicate subcommand name '{normalized}'.",
                        Name
                    );
                }
            }

            CommandBuilder subcommand = Create(normalized);
            configure(subcommand);
            if (subcommand._contexts != CommandExecutionContextSets.Gameplay)
            {
                throw new CommandConfigurationException(
                    $"Command '{Name} {normalized}': subcommands run inside the parent's "
                        + "execution contexts; do not set Contexts on a subcommand.",
                    Name
                );
            }

            if (!subcommand._addToHistory)
            {
                throw new CommandConfigurationException(
                    $"Command '{Name} {normalized}': subcommands are recorded under the "
                        + "parent's history policy; do not set AddToHistory on a subcommand.",
                    Name
                );
            }

            _subcommands.Add(subcommand);
            return this;
        }

        /// <summary>
        ///     Sets the handler. It runs only when every argument parsed and
        ///     validated; otherwise a controlled error reports the first
        ///     problem and the invocation ends.
        /// </summary>
        public CommandBuilder Handler(TypedCommandHandler handler)
        {
            _handler = handler;
            return this;
        }

        /// <summary>
        ///     Snapshots this definition into a
        ///     <see cref="CommandDefinition"/> registered against
        ///     <paramref name="owner"/>. Throws
        ///     <see cref="CommandConfigurationException"/> for configuration
        ///     errors; duplicate names against the live shell stay runtime
        ///     registration failures handled by the shell.
        /// </summary>
        internal CommandDefinition Build(CommandShell owner)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new CommandConfigurationException(
                    "A command name is required; pass a non-empty name to CommandBuilder.Create."
                );
            }

            string name = NormalizeName(Name, Name);
            if (0 < _subcommands.Count)
            {
                return BuildRouterDefinition(owner, name);
            }

            return BuildLeafDefinition(owner, name);
        }

        private CommandDefinition BuildLeafDefinition(CommandShell owner, string name)
        {
            CommandArgument[] specs = ValidateLeafConfiguration(name);
            string hint = _hint ?? BuildUsageHint(name, specs);
            CommandCompletionProvider provider = BuildCompletionProvider(specs);
            TypedCommandHandler handler = _handler;

            CommandHandler dispatch = (context, arguments) =>
                RunTyped(owner, specs, name, handler, context, arguments, 0);

            return new CommandDefinition
            {
                Name = name,
                Help = _help,
                Hint = hint,
                MinArgCount = RequiredCount(specs),
                MaxArgCount = specs.Length,
                AddToHistory = _addToHistory,
                Contexts = _contexts,
                Handler = dispatch,
                CompletionProvider = provider,
            };
        }

        private CommandDefinition BuildRouterDefinition(CommandShell owner, string name)
        {
            if (0 < _arguments.Count)
            {
                throw new CommandConfigurationException(
                    $"Command '{name}': a command with subcommands cannot declare its own "
                        + "arguments; declare them on the subcommands.",
                    name
                );
            }

            SubcommandRoute self = BuildRouterRoute(name, name);
            TypedCommandHandler fallback = _handler;

            return new CommandDefinition
            {
                Name = name,
                Help = _help,
                Hint = _hint ?? $"{name} <subcommand>",
                MinArgCount = 0,
                /*
                    Unbounded at the shell level: the router knows which leaf
                    owns the arguments only after routing, so it issues the
                    precise composed bounds errors itself.
                */
                MaxArgCount = null,
                AddToHistory = _addToHistory,
                Contexts = _contexts,
                Handler = (context, arguments) =>
                    RouteInvocation(owner, self.Lookup, name, fallback, context, arguments, 0),
                CompletionProvider = (
                    in CommandCompletionContext context,
                    List<CommandCompletion> results
                ) => RouteCompletion(self.Lookup, context, 0, results),
            };
        }

        /// <summary>
        ///     Builds this builder as one route of a parent's subcommand
        ///     router. Children never register on their own; the returned
        ///     route is consumed by the parent's dispatch and completion.
        /// </summary>
        private SubcommandRoute BuildRoute(string path)
        {
            string name = NormalizeName(path, Name);
            if (0 < _subcommands.Count)
            {
                return BuildRouterRoute(path, name);
            }

            return BuildLeafRoute(path, name);
        }

        private SubcommandRoute BuildLeafRoute(string path, string name)
        {
            CommandArgument[] specs = ValidateLeafConfiguration(path);
            return new SubcommandRoute
            {
                Name = name,
                FullPath = path,
                Help = _help,
                Usage = _hint ?? BuildUsageHint(name, specs),
                PathUsage = _hint ?? BuildUsageHint(path, specs),
                Specs = specs,
                RequiredCount = RequiredCount(specs),
                Handler = _handler,
                CompletionStages = BuildSpecCompletionStages(specs),
            };
        }

        private SubcommandRoute BuildRouterRoute(string path, string name)
        {
            if (0 < _arguments.Count)
            {
                throw new CommandConfigurationException(
                    $"Command '{path}': a command with subcommands cannot declare its own "
                        + "arguments; declare them on the subcommands.",
                    path
                );
            }

            SubcommandRoute[] children = new SubcommandRoute[_subcommands.Count];
            Dictionary<string, SubcommandRoute> lookup = new(
                children.Length,
                StringComparer.OrdinalIgnoreCase
            );
            for (int i = 0; i < children.Length; ++i)
            {
                CommandBuilder child = _subcommands[i];
                SubcommandRoute route = child.BuildRoute($"{path} {child.Name}");
                if (!lookup.TryAdd(route.Name, route))
                {
                    /*
                        The Subcommand-time duplicate scan runs before each
                        configure callback, so re-entrant callbacks can slip a
                        second name past it; Build is the authoritative gate.
                    */
                    throw new CommandConfigurationException(
                        $"Command '{path}': duplicate subcommand name '{route.Name}'.",
                        path
                    );
                }

                children[i] = route;
            }

            return new SubcommandRoute
            {
                Name = name,
                FullPath = path,
                Help = _help,
                Usage = _hint ?? $"{name} <subcommand>",
                PathUsage = _hint ?? $"{path} <subcommand>",
                Fallback = _handler,
                Children = children,
                Lookup = lookup,
            };
        }

        private CommandArgument[] ValidateLeafConfiguration(string path)
        {
            if (_handler == null)
            {
                throw new CommandConfigurationException(
                    $"Command '{path}': set a handler with CommandBuilder.Handler before registration.",
                    path
                );
            }

            CommandArgument[] specs = _arguments.ToArray();

            /*
                Required arguments must form a leading block; a required
                argument after an optional one would make the derived bounds
                and defaults ambiguous.
             */
            bool seenOptional = false;
            foreach (CommandArgument spec in specs)
            {
                if (spec.IsRequired)
                {
                    if (seenOptional)
                    {
                        throw new CommandConfigurationException(
                            $"Command '{path}': required argument '{spec.Name}' must be "
                                + "declared before every optional argument.",
                            path
                        );
                    }

                    continue;
                }

                seenOptional = true;
            }

            /*
                Defaults are configuration, but the handler contract promises
                validated values, so a default must satisfy its argument's own
                validation. Surfacing the contradiction here beats failing the
                first omitted invocation.
             */
            foreach (CommandArgument spec in specs)
            {
                if (spec.IsRequired)
                {
                    continue;
                }

                string defaultError = spec.ValidateParsed(spec.GetDefault());
                if (defaultError != null)
                {
                    throw new CommandConfigurationException(
                        $"Command '{path}': the default for argument '{spec.Name}' fails "
                            + $"its own validation: {defaultError}",
                        path
                    );
                }
            }

            return specs;
        }

        /*
            One routing target of a subcommand command: either a leaf with
            typed argument specs and a handler, or a nested router with its
            own children.
        */
        private sealed class SubcommandRoute
        {
            public string Name { get; internal set; }

            public string FullPath { get; internal set; }

            public string Help { get; internal set; }

            /// <summary>Usage within a route listing, e.g. <c>add &lt;item:string&gt;</c>.</summary>
            public string Usage { get; internal set; }

            /// <summary>Usage with the composed path for bounds errors, e.g. <c>inventory add &lt;item:string&gt;</c>.</summary>
            public string PathUsage { get; internal set; }

            public CommandArgument[] Specs { get; internal set; }

            public int RequiredCount { get; internal set; }

            public TypedCommandHandler Handler { get; internal set; }

            public TypedCommandHandler Fallback { get; internal set; }

            public CommandCompletionProvider[] CompletionStages { get; internal set; } =
                Array.Empty<CommandCompletionProvider>();

            public SubcommandRoute[] Children { get; internal set; }

            public Dictionary<string, SubcommandRoute> Lookup { get; internal set; }

            public bool IsRouter => Children != null;
        }
    }
}
