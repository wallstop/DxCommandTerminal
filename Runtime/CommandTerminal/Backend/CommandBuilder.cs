namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    ///     Fluent authoring for one typed command: names, help, hints, history
    ///     policy, execution eligibility, typed arguments, handler, and
    ///     completion all come from one definition, registered into the same
    ///     shell through <see cref="CommandShell.AddCommand(CommandBuilder, out CommandRegistrationHandle)"/>.
    /// </summary>
    /// <remarks>
    ///     Argument bounds, the usage hint, and the completion provider derive
    ///     from the declared arguments, so help, validation, and completion
    ///     cannot drift apart. Configuration errors (missing handler,
    ///     duplicate argument names, unparseable argument types) throw at
    ///     definition time with a diagnostic naming the command; user-input
    ///     mistakes at execution become controlled shell errors and never run
    ///     the handler.
    /// </remarks>
    public sealed class CommandBuilder
    {
        /// <summary>The command name, matched case-insensitively; spaces are stripped at registration.</summary>
        public string Name { get; }

        private readonly List<CommandArgument> _arguments = new();

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
                throw new InvalidOperationException(
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

        private static void RunTyped(
            CommandShell owner,
            CommandArgument[] specs,
            string name,
            TypedCommandHandler handler,
            CommandExecutionContext context,
            BorrowedCommandArguments arguments
        )
        {
            object[] parsed = new object[specs.Length];
            for (int i = 0; i < specs.Length; ++i)
            {
                if (arguments.Count <= i)
                {
                    // The shell's derived bounds guarantee required arguments exist.
                    parsed[i] = specs[i].GetDefault();
                    continue;
                }

                CommandArg input = arguments[i];
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

            handler(context, new CommandArguments(specs, parsed, arguments, context));
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
            bool anyChoices = false;
            for (int i = 0; i < specs.Length; ++i)
            {
                anyChoices |= specs[i].HasChoices;
            }

            if (!anyChoices)
            {
                /*
                    No choices anywhere: leave the provider unset so the
                    command keeps the shared history-based completion path.
                 */
                return null;
            }

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

            return CommandCompletionProviders.Staged(stages);
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
                throw new InvalidOperationException(
                    $"Command '{Name}': argument names must not be empty."
                );
            }

            for (int i = 0; i < _arguments.Count; ++i)
            {
                if (string.Equals(_arguments[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Command '{Name}': duplicate argument name '{name}'."
                    );
                }
            }

            CommandArgumentSpec<T> spec = new(name);
            if (configure != null)
            {
                spec = configure(spec);
                if (spec == null)
                {
                    throw new InvalidOperationException(
                        $"Command '{Name}': the configuration callback for argument '{name}' returned null."
                    );
                }
            }

            spec.EnsureParseable();
            _arguments.Add(spec);
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
        ///     <see cref="InvalidOperationException"/> for configuration
        ///     errors; duplicate names against the live shell stay runtime
        ///     registration failures handled by the shell.
        /// </summary>
        internal CommandDefinition Build(CommandShell owner)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                throw new InvalidOperationException(
                    "A command name is required; pass a non-empty name to CommandBuilder.Create."
                );
            }

            if (_handler == null)
            {
                throw new InvalidOperationException(
                    $"Command '{Name}': set a handler with CommandBuilder.Handler before registration."
                );
            }

            string name = Name.Replace(" ", string.Empty, StringComparison.Ordinal);
            CommandArgument[] specs = _arguments.ToArray();

            /*
                Required arguments must form a leading block; a required
                argument after an optional one would make the derived bounds
                and defaults ambiguous.
             */
            bool seenOptional = false;
            int requiredCount = 0;
            for (int i = 0; i < specs.Length; ++i)
            {
                if (specs[i].IsRequired)
                {
                    if (seenOptional)
                    {
                        throw new InvalidOperationException(
                            $"Command '{name}': required argument '{specs[i].Name}' must be "
                                + "declared before every optional argument."
                        );
                    }

                    ++requiredCount;
                }
                else
                {
                    seenOptional = true;
                }
            }

            string hint = _hint ?? BuildUsageHint(name, specs);
            CommandCompletionProvider provider = BuildCompletionProvider(specs);
            TypedCommandHandler handler = _handler;

            CommandHandler dispatch = (context, arguments) =>
                RunTyped(owner, specs, name, handler, context, arguments);

            return new CommandDefinition
            {
                Name = name,
                Help = _help,
                Hint = hint,
                MinArgCount = requiredCount,
                MaxArgCount = specs.Length,
                AddToHistory = _addToHistory,
                Contexts = _contexts,
                Handler = dispatch,
                CompletionProvider = provider,
            };
        }
    }
}
