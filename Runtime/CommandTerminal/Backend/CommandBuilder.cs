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
    ///     unparseable argument types, defaults failing their own validation,
    ///     arguments after the remaining argument, or a default on the
    ///     remaining argument) throw at definition time with a diagnostic
    ///     naming the command; user-input mistakes at execution become
    ///     controlled shell errors and never run the handler.
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
                    CommandConfigurationFailure.EmptyName,
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

        private static string NormalizeName(string owner, string name, bool isSubcommandName = true)
        {
            string normalized = name;
            if (normalized != null && 0 <= normalized.IndexOf(' ', StringComparison.Ordinal))
            {
                normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
            }

            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.EmptyName,
                    $"Command '{owner ?? string.Empty}': subcommand names must not be empty.",
                    owner,
                    subcommandName: isSubcommandName ? name : null
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
                CommandArgument spec = specs[i];
                if (spec.IsRemaining)
                {
                    /*
                        The remaining argument consumes every trailing token:
                        it parses and validates each one with full type
                        knowledge and stores its typed array in its own slot
                        of the parsed buffer.
                     */
                    if (
                        !spec.TryParseAll(
                            arguments,
                            offset + i,
                            parsed,
                            i,
                            out CommandArg failedToken,
                            out string validationError
                        )
                    )
                    {
                        owner.IssueErrorMessage(
                            validationError == null
                                ? $"'{name}': {spec.FormatParseError(failedToken)}"
                                : $"'{name}': {validationError}"
                        );
                        return;
                    }

                    continue;
                }

                if (arguments.Count <= offset + i)
                {
                    /*
                        The dispatching level guarantees required arguments
                        exist: the shell's derived bounds for top-level
                        commands, the router for subcommands.
                     */
                    parsed[i] = spec.GetDefault();
                    continue;
                }

                CommandArg input = arguments[offset + i];
                if (!spec.TryParse(input, out object value))
                {
                    owner.IssueErrorMessage($"'{name}': {spec.FormatParseError(input)}");
                    return;
                }

                string error = spec.ValidateParsed(value);
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
            bool hasRemaining = HasRemaining(specs);
            foreach (CommandCompletionProvider stage in stages)
            {
                if (stage == null)
                {
                    continue;
                }

                if (!hasRemaining)
                {
                    return CommandCompletionProviders.Staged(stages);
                }

                return (in CommandCompletionContext context, List<CommandCompletion> results) =>
                {
                    /*
                        The remaining argument owns every trailing stage, so
                        requests past the last fixed index clamp onto its
                        choices.
                     */
                    int index = context.ActiveArgumentIndex;
                    if (stages.Length <= index)
                    {
                        index = stages.Length - 1;
                    }

                    stages[index]?.Invoke(context, results);
                };
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

            if (!route.HasRemaining && route.Specs.Length < available)
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
                if (!route.HasRemaining)
                {
                    return;
                }

                /*
                    The remaining argument owns every trailing stage, so later
                    stages clamp onto its choices.
                 */
                childStage = route.Specs.Length - 1;
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

        private static bool HasRemaining(CommandArgument[] specs)
        {
            return 0 < specs.Length && specs[specs.Length - 1].IsRemaining;
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
        ///     and choices. Argument order is declaration order; declaring an
        ///     argument after the command's
        ///     <see cref="Remaining{T}"/> argument throws.
        /// </summary>
        public CommandBuilder Arg<T>(
            string name,
            Func<CommandArgumentSpec<T>, CommandArgumentSpec<T>> configure = null
        )
        {
            EnsureArgumentNameAvailable(name);
            if (0 < _arguments.Count && _arguments[_arguments.Count - 1].IsRemaining)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidRemainingArgument,
                    $"Command '{Name}': argument '{name}' cannot follow the remaining "
                        + $"argument '{_arguments[_arguments.Count - 1].Name}'; the remaining "
                        + "argument consumes every trailing token.",
                    Name,
                    argumentName: name
                );
            }

            _arguments.Add(ConfigureArgument(name, configure, isRemaining: false));
            return this;
        }

        /// <summary>
        ///     Declares the command's unbounded trailing argument: every token
        ///     after the declared arguments parses and validates as
        ///     <typeparamref name="T"/> and collects, in input order, into one
        ///     array the handler reads with
        ///     <c>arguments.Get&lt;T[]&gt;(name)</c>. An invocation without
        ///     trailing tokens reads as an empty array; mark the argument
        ///     <see cref="CommandArgumentSpec{T}.Required"/> to demand at
        ///     least one token instead.
        /// </summary>
        /// <remarks>
        ///     The remaining argument must be the final declaration: later
        ///     <see cref="Arg{T}"/> calls, a second
        ///     <see cref="Remaining{T}"/> call, and
        ///     <see cref="CommandArgumentSpec{T}.Default"/> on the remaining
        ///     argument all throw at definition time.
        /// </remarks>
        public CommandBuilder Remaining<T>(
            string name,
            Func<CommandArgumentSpec<T>, CommandArgumentSpec<T>> configure = null
        )
        {
            EnsureArgumentNameAvailable(name);
            foreach (CommandArgument argument in _arguments)
            {
                if (argument.IsRemaining)
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.InvalidRemainingArgument,
                        $"Command '{Name}': only one remaining argument is allowed; "
                            + $"'{argument.Name}' already consumes every trailing token.",
                        Name,
                        argumentName: name
                    );
                }
            }

            CommandArgumentSpec<T> spec = ConfigureArgument(name, configure, isRemaining: true);
            if (spec.HasExplicitDefault)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidRemainingArgument,
                    $"Command '{Name}': the remaining argument '{name}' cannot declare a "
                        + "default; an invocation without trailing tokens reads as an empty array.",
                    Name,
                    argumentName: name
                );
            }

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
                        CommandConfigurationFailure.DuplicateName,
                        $"Command '{Name}': duplicate subcommand name '{normalized}'.",
                        Name,
                        subcommandName: normalized
                    );
                }
            }

            CommandBuilder subcommand = Create(normalized);
            try
            {
                configure(subcommand);
            }
            catch (CommandConfigurationException e)
            {
                throw WithComposedScope(e, normalized);
            }

            if (subcommand._contexts != CommandExecutionContextSets.Gameplay)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
                    $"Command '{Name} {normalized}': subcommands run inside the parent's "
                        + "execution contexts; do not set Contexts on a subcommand.",
                    Name,
                    subcommandName: normalized
                );
            }

            if (!subcommand._addToHistory)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
                    $"Command '{Name} {normalized}': subcommands are recorded under the "
                        + "parent's history policy; do not set AddToHistory on a subcommand.",
                    Name,
                    subcommandName: normalized
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
                    CommandConfigurationFailure.EmptyName,
                    "A command name is required; pass a non-empty name to CommandBuilder.Create."
                );
            }

            string name = NormalizeName(Name, Name, isSubcommandName: false);
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
                /*
                    A remaining argument consumes every trailing token, so the
                    command is unbounded at the shell level and the typed
                    dispatch reports per-element problems itself.
                 */
                MaxArgCount = HasRemaining(specs) ? (int?)null : specs.Length,
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
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
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
                HasRemaining = HasRemaining(specs),
                Handler = _handler,
                CompletionStages = BuildSpecCompletionStages(specs),
            };
        }

        private SubcommandRoute BuildRouterRoute(string path, string name)
        {
            if (0 < _arguments.Count)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.InvalidSubcommandConfiguration,
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
                        CommandConfigurationFailure.DuplicateName,
                        $"Command '{path}': duplicate subcommand name '{route.Name}'.",
                        path,
                        subcommandName: route.Name
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

        private void EnsureArgumentNameAvailable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.EmptyName,
                    $"Command '{Name}': argument names must not be empty.",
                    Name,
                    argumentName: name
                );
            }

            foreach (CommandArgument argument in _arguments)
            {
                if (string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.DuplicateName,
                        $"Command '{Name}': duplicate argument name '{name}'.",
                        Name,
                        argumentName: name
                    );
                }
            }
        }

        private CommandArgumentSpec<T> ConfigureArgument<T>(
            string name,
            Func<CommandArgumentSpec<T>, CommandArgumentSpec<T>> configure,
            bool isRemaining
        )
        {
            CommandArgumentSpec<T> spec = new(name, isRemaining);
            if (configure != null)
            {
                try
                {
                    spec = configure(spec);
                }
                catch (CommandConfigurationException e)
                {
                    throw WithCommandScope(e, name);
                }

                if (spec == null)
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.NullConfigurationResult,
                        $"Command '{Name}': the configuration callback for argument '{name}' returned null.",
                        Name,
                        argumentName: name
                    );
                }
            }

            try
            {
                spec.EnsureParseable();
            }
            catch (CommandConfigurationException e)
            {
                throw WithCommandScope(e, name);
            }

            return spec;
        }

        /*
            Spec-level misconfigurations throw without knowing their owning
            command; re-throwing through the builder attaches the command and
            argument scope so every definition-time failure reports the same
            structured identity. The original exception rides as the inner
            exception, keeping its throw-site stack.
         */
        private CommandConfigurationException WithCommandScope(
            CommandConfigurationException e,
            string argumentName
        )
        {
            return new CommandConfigurationException(
                e.Failure,
                e.Message,
                e.CommandName ?? Name,
                argumentName: e.ArgumentName ?? argumentName,
                subcommandName: e.SubcommandName,
                argumentType: e.ArgumentType,
                innerException: e
            );
        }

        /*
            Failures thrown while configuring a subcommand report the
            subcommand builder's own name; the parent composes the routed
            path here so the identity matches Build-time diagnostics, which
            name the composed path ('inventory add').
         */
        private CommandConfigurationException WithComposedScope(
            CommandConfigurationException e,
            string subcommandName
        )
        {
            string innerCommand = e.CommandName;
            return new CommandConfigurationException(
                e.Failure,
                e.Message,
                string.IsNullOrEmpty(innerCommand)
                    ? $"{Name} {subcommandName}"
                    : $"{Name} {innerCommand}",
                argumentName: e.ArgumentName,
                subcommandName: e.SubcommandName ?? subcommandName,
                argumentType: e.ArgumentType,
                innerException: e
            );
        }

        private CommandArgument[] ValidateLeafConfiguration(string path)
        {
            if (_handler == null)
            {
                throw new CommandConfigurationException(
                    CommandConfigurationFailure.MissingHandler,
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
                            CommandConfigurationFailure.ArgumentOrdering,
                            $"Command '{path}': required argument '{spec.Name}' must be "
                                + "declared before every optional argument.",
                            path,
                            argumentName: spec.Name
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
                if (spec.IsRequired || spec.IsRemaining)
                {
                    continue;
                }

                string defaultError = spec.ValidateParsed(spec.GetDefault());
                if (defaultError != null)
                {
                    throw new CommandConfigurationException(
                        CommandConfigurationFailure.InvalidDefault,
                        $"Command '{path}': the default for argument '{spec.Name}' fails "
                            + $"its own validation: {defaultError}",
                        path,
                        argumentName: spec.Name
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

            /// <summary>True when the route's final argument is its unbounded trailing argument.</summary>
            public bool HasRemaining { get; internal set; }

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
