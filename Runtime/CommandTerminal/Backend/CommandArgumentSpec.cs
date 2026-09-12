namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    ///     Fluent, immutable specification of one typed command argument.
    ///     Parsing reuses <see cref="CommandArg.TryGet{T}"/> and the registered
    ///     parser table; validation composes static choices, numeric ranges,
    ///     and custom validators; completion derives from static or dynamic
    ///     choices.
    /// </summary>
    public sealed class CommandArgumentSpec<T> : CommandArgument
    {
        private static readonly Dictionary<Type, string> FriendlyTypeNames = new()
        {
            [typeof(bool)] = "bool",
            [typeof(byte)] = "byte",
            [typeof(char)] = "char",
            [typeof(decimal)] = "decimal",
            [typeof(double)] = "double",
            [typeof(float)] = "float",
            [typeof(int)] = "int",
            [typeof(long)] = "long",
            [typeof(object)] = "object",
            [typeof(sbyte)] = "sbyte",
            [typeof(short)] = "short",
            [typeof(string)] = "string",
            [typeof(uint)] = "uint",
            [typeof(ulong)] = "ulong",
            [typeof(ushort)] = "ushort",
        };

        public override bool IsRequired => _required;

        public override string Description { get; }

        public override bool HasChoices => _staticChoices != null || _dynamicChoices != null;

        private readonly bool _required;
        private readonly T _defaultValue;
        private readonly CommandArgParser<T> _parserOverride;
        private readonly IReadOnlyList<T> _staticChoices;
        private readonly string[] _staticChoiceTexts;
        private readonly Func<CommandCompletionContext, IReadOnlyList<T>> _dynamicChoices;
        private readonly Func<T, string> _rangeValidator;
        private readonly Func<T, string> _validator;

        internal CommandArgumentSpec(string name)
            : this(
                name,
                required: false,
                defaultValue: default,
                parserOverride: null,
                staticChoices: null,
                dynamicChoices: null,
                rangeValidator: null,
                validator: null,
                description: null
            ) { }

        private CommandArgumentSpec(
            string name,
            bool required,
            T defaultValue,
            CommandArgParser<T> parserOverride,
            IReadOnlyList<T> staticChoices,
            Func<CommandCompletionContext, IReadOnlyList<T>> dynamicChoices,
            Func<T, string> rangeValidator,
            Func<T, string> validator,
            string description
        )
            : base(name, GetTypeName(typeof(T)))
        {
            _required = required;
            _defaultValue = defaultValue;
            _parserOverride = parserOverride;

            /*
                Copy caller-owned choice arrays: the spec is immutable, so a
                later mutation of the caller's array must not change
                validation or completion of a registered command. The
                IReadOnlyList branch is not reachable from the public API
                today (the only entry point is the params array) and keeps
                its reference.
             */
            if (staticChoices is T[] choicesArray)
            {
                T[] choicesCopy = new T[choicesArray.Length];
                choicesArray.CopyTo(choicesCopy, 0);
                _staticChoices = choicesCopy;
            }
            else
            {
                _staticChoices = staticChoices;
            }

            _staticChoiceTexts = _staticChoices == null ? null : FormatChoices(_staticChoices);
            _dynamicChoices = dynamicChoices;
            _rangeValidator = rangeValidator;
            _validator = validator;
            Description = description;
        }

        private static string GetTypeName(Type type)
        {
            return FriendlyTypeNames.TryGetValue(type, out string friendly) ? friendly : type.Name;
        }

        private static bool ChoicesEqual(T candidate, T value)
        {
            if (candidate is string candidateText && value is string valueText)
            {
                return string.Equals(candidateText, valueText, StringComparison.OrdinalIgnoreCase);
            }

            return EqualityComparer<T>.Default.Equals(candidate, value);
        }

        private static string FormatValue(T value)
        {
            if (value is bool boolean)
            {
                return boolean ? "true" : "false";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string[] FormatChoices(IReadOnlyList<T> values)
        {
            string[] texts = new string[values.Count];
            for (int i = 0; i < texts.Length; ++i)
            {
                texts[i] = FormatValue(values[i]);
            }

            return texts;
        }

        private static void AppendCandidate(
            string text,
            string description,
            in CommandCompletionContext context,
            List<CommandCompletion> results
        )
        {
            if (text.StartsWith(context.Token, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new CommandCompletion(text, description: description));
            }
        }

        /// <summary>Marks the argument required. Omitting it fails the invocation.</summary>
        public CommandArgumentSpec<T> Required()
        {
            return new CommandArgumentSpec<T>(
                Name,
                true,
                _defaultValue,
                _parserOverride,
                _staticChoices,
                _dynamicChoices,
                _rangeValidator,
                _validator,
                Description
            );
        }

        /// <summary>
        ///     Marks the argument optional and sets the value used when it is
        ///     omitted. Optional arguments without an explicit default use
        ///     <c>default(T)</c>; a later <see cref="Required"/> makes the
        ///     argument mandatory again and leaves the default unused.
        /// </summary>
        public CommandArgumentSpec<T> Default(T value)
        {
            return new CommandArgumentSpec<T>(
                Name,
                required: false,
                value,
                _parserOverride,
                _staticChoices,
                _dynamicChoices,
                _rangeValidator,
                _validator,
                Description
            );
        }

        /// <summary>Sets the description shown with the argument's completions.</summary>
        public CommandArgumentSpec<T> Describe(string description)
        {
            return new CommandArgumentSpec<T>(
                Name,
                _required,
                _defaultValue,
                _parserOverride,
                _staticChoices,
                _dynamicChoices,
                _rangeValidator,
                _validator,
                description
            );
        }

        /// <summary>
        ///     Restricts the argument to the given values and completes them.
        ///     String choices compare case-insensitively, like command names.
        /// </summary>
        public CommandArgumentSpec<T> Choices(params T[] values)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException(
                    $"Argument '{Name}' needs at least one choice.",
                    nameof(values)
                );
            }

            for (int i = 0; i < values.Length; ++i)
            {
                if (values[i] == null)
                {
                    throw new ArgumentException(
                        $"Argument '{Name}' choices must not contain null.",
                        nameof(values)
                    );
                }
            }

            return new CommandArgumentSpec<T>(
                Name,
                _required,
                _defaultValue,
                _parserOverride,
                values,
                _dynamicChoices,
                _rangeValidator,
                _validator,
                Description
            );
        }

        /// <summary>
        ///     Completes the argument from live context: the callback runs on
        ///     every completion request for this argument and its returned
        ///     values are offered as candidates. Dynamic choices inform
        ///     completion only; execution validation stays with
        ///     <see cref="Validate"/> and <see cref="Range"/> so an item that
        ///     disappeared between suggestion and Enter is still rejected.
        /// </summary>
        public CommandArgumentSpec<T> Choices(
            Func<CommandCompletionContext, IReadOnlyList<T>> provider
        )
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            return new CommandArgumentSpec<T>(
                Name,
                _required,
                _defaultValue,
                _parserOverride,
                _staticChoices,
                provider,
                _rangeValidator,
                _validator,
                Description
            );
        }

        /// <summary>
        ///     Completes both boolean values. Only valid on
        ///     <see cref="CommandArgumentSpec{bool}" />.
        /// </summary>
        public CommandArgumentSpec<T> BoolChoices()
        {
            if (typeof(T) != typeof(bool))
            {
                throw new InvalidOperationException(
                    $"Argument '{Name}' of type {TypeName} cannot use {nameof(BoolChoices)}; only bool arguments can."
                );
            }

            return Choices((T)(object)true, (T)(object)false);
        }

        /// <summary>
        ///     Completes every enum value of the argument's type. Completion
        ///     offers the enum names; input may also use any spelling the
        ///     shared <see cref="CommandArg.TryGet{T}"/> enum path accepts,
        ///     including bare numeric values.
        /// </summary>
        public CommandArgumentSpec<T> EnumChoices()
        {
            if (!typeof(T).IsEnum)
            {
                throw new InvalidOperationException(
                    $"Argument '{Name}' of type {TypeName} cannot use {nameof(EnumChoices)}; only enum arguments can."
                );
            }

            Array values = Enum.GetValues(typeof(T));
            T[] choices = new T[values.Length];
            for (int i = 0; i < choices.Length; ++i)
            {
                choices[i] = (T)values.GetValue(i);
            }

            return Choices(choices);
        }

        /// <summary>
        ///     Constrains the argument to the inclusive range
        ///     [<paramref name="min"/>, <paramref name="max"/>].
        /// </summary>
        public CommandArgumentSpec<T> Range(T min, T max)
        {
            Type type = typeof(T);
            if (
                !typeof(IComparable<T>).IsAssignableFrom(type)
                && !typeof(IComparable).IsAssignableFrom(type)
            )
            {
                throw new InvalidOperationException(
                    $"Argument '{Name}' of type {TypeName} cannot use {nameof(Range)}; {type} is not comparable."
                );
            }

            if (min == null || max == null)
            {
                throw new ArgumentNullException(
                    min == null ? nameof(min) : nameof(max),
                    $"Argument '{Name}' range bounds must not be null."
                );
            }

            /*
                Comparer<T>.Default calls IComparable<T>.CompareTo directly on
                value types (no per-invocation boxing), and still works for
                non-generic-IComparable types through the BCL fallback.
             */
            IComparer<T> comparer = Comparer<T>.Default;
            if (0 < comparer.Compare(min, max))
            {
                throw new ArgumentException(
                    $"Argument '{Name}' range {FormatValue(min)}..{FormatValue(max)} is inverted.",
                    nameof(min)
                );
            }

            return new CommandArgumentSpec<T>(
                Name,
                _required,
                _defaultValue,
                _parserOverride,
                _staticChoices,
                _dynamicChoices,
                value =>
                {
                    if (comparer.Compare(min, value) <= 0 && comparer.Compare(value, max) <= 0)
                    {
                        return null;
                    }

                    return $"Value '{FormatValue(value)}' for argument '{Name}' is out of range "
                        + $"[{FormatValue(min)}, {FormatValue(max)}]";
                },
                _validator,
                Description
            );
        }

        /// <summary>
        ///     Runs a custom validator after parsing and the built-in checks.
        ///     Return null to accept the value, or the error text describing
        ///     the problem.
        /// </summary>
        public CommandArgumentSpec<T> Validate(Func<T, string> validator)
        {
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

            return new CommandArgumentSpec<T>(
                Name,
                _required,
                _defaultValue,
                _parserOverride,
                _staticChoices,
                _dynamicChoices,
                _rangeValidator,
                validator,
                Description
            );
        }

        /// <summary>Overrides the parser used to convert input to this argument's type.</summary>
        public CommandArgumentSpec<T> Parser(CommandArgParser<T> parser)
        {
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            return new CommandArgumentSpec<T>(
                Name,
                _required,
                _defaultValue,
                parser,
                _staticChoices,
                _dynamicChoices,
                _rangeValidator,
                _validator,
                Description
            );
        }

        /// <summary>
        ///     Throws when the argument's type has no parse path and no parser
        ///     override was configured, so misconfigured definitions fail at
        ///     definition time instead of on the first invocation.
        /// </summary>
        internal void EnsureParseable()
        {
            if (_parserOverride != null || CommandArg.CanParse(typeof(T)))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Argument '{Name}' of type {typeof(T)} has no registered parser. "
                    + $"Register one through {nameof(CommandArg)}.{nameof(CommandArg.RegisterParser)} "
                    + "or provide one with CommandArgumentSpec.Parser."
            );
        }

        internal override bool TryParse(CommandArg input, out object parsed)
        {
            bool ok = input.TryGet(out T value, _parserOverride);
            parsed = value;
            return ok;
        }

        internal override object GetDefault()
        {
            return _defaultValue;
        }

        internal override string ValidateParsed(object parsed)
        {
            T value = (T)parsed;
            if (_staticChoices != null)
            {
                bool matches = false;
                for (int i = 0; i < _staticChoices.Count; ++i)
                {
                    if (ChoicesEqual(_staticChoices[i], value))
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches)
                {
                    return $"Invalid value '{FormatValue(value)}' for argument '{Name}' "
                        + $"(expected one of: {string.Join(", ", _staticChoiceTexts)})";
                }
            }

            string error = _rangeValidator?.Invoke(value);
            if (error != null)
            {
                return error;
            }

            error = _validator?.Invoke(value);
            if (error != null)
            {
                return $"Invalid value '{FormatValue(value)}' for argument '{Name}': {error}";
            }

            return null;
        }

        internal override string FormatParseError(CommandArg input)
        {
            string expected =
                _staticChoices == null
                    ? TypeName
                    : $"one of: {string.Join(", ", _staticChoiceTexts)}";
            return $"Invalid value '{input.contents}' for argument '{Name}' (expected {expected})";
        }

        internal override void AppendUsage(StringBuilder builder)
        {
            builder.Append(_required ? '<' : '[');
            builder.Append(Name);
            builder.Append(':');
            builder.Append(TypeName);
            builder.Append(_required ? '>' : ']');
        }

        internal override void AppendCompletions(
            in CommandCompletionContext context,
            List<CommandCompletion> results
        )
        {
            if (_staticChoices != null)
            {
                for (int i = 0; i < _staticChoiceTexts.Length; ++i)
                {
                    AppendCandidate(_staticChoiceTexts[i], Description, context, results);
                }

                return;
            }

            if (_dynamicChoices == null)
            {
                return;
            }

            IReadOnlyList<T> provided = _dynamicChoices(context);
            if (provided == null)
            {
                return;
            }

            for (int i = 0; i < provided.Count; ++i)
            {
                AppendCandidate(FormatValue(provided[i]), Description, context, results);
            }
        }
    }
}
