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
        /*
            C# keyword aliases for usage text and errors. nameof cannot express
            these: it resolves to the type's name (nameof(Int32) is "Int32"),
            and the keyword spellings are keywords, not identifiers. Unmapped
            types fall back to Type.Name.
         */
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

        /// <summary>
        ///     True when the argument is its command's unbounded trailing
        ///     argument; see <see cref="CommandBuilder.Remaining{T}"/>.
        /// </summary>
        internal override bool IsRemaining => _isRemaining;

        /// <summary>True when an explicit default was configured for this argument.</summary>
        internal bool HasExplicitDefault => _hasExplicitDefault;

        private readonly bool _required;
        private readonly bool _isRemaining;
        private readonly bool _hasExplicitDefault;
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

        internal CommandArgumentSpec(string name, bool isRemaining)
            : this(
                name,
                required: false,
                defaultValue: default,
                parserOverride: null,
                staticChoices: null,
                dynamicChoices: null,
                rangeValidator: null,
                validator: null,
                description: null,
                isRemaining: isRemaining
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
            string description,
            bool isRemaining = false,
            bool hasExplicitDefault = false
        )
            : base(name, GetTypeName(typeof(T)))
        {
            _required = required;
            _isRemaining = isRemaining;
            _hasExplicitDefault = hasExplicitDefault;
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
                Description,
                isRemaining: _isRemaining,
                hasExplicitDefault: _hasExplicitDefault
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
                Description,
                isRemaining: _isRemaining,
                hasExplicitDefault: true
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
                description,
                isRemaining: _isRemaining,
                hasExplicitDefault: _hasExplicitDefault
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
                throw new CommandConfigurationException(
                    $"Argument '{Name}' needs at least one choice."
                );
            }

            for (int i = 0; i < values.Length; ++i)
            {
                if (values[i] == null)
                {
                    throw new CommandConfigurationException(
                        $"Argument '{Name}' choices must not contain null."
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
                Description,
                isRemaining: _isRemaining,
                hasExplicitDefault: _hasExplicitDefault
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
                Description,
                isRemaining: _isRemaining,
                hasExplicitDefault: _hasExplicitDefault
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
                throw new CommandConfigurationException(
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
                throw new CommandConfigurationException(
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
                throw new CommandConfigurationException(
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
                throw new CommandConfigurationException(
                    $"Argument '{Name}' range {FormatValue(min)}..{FormatValue(max)} is inverted."
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
                Description,
                isRemaining: _isRemaining,
                hasExplicitDefault: _hasExplicitDefault
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
                Description,
                isRemaining: _isRemaining,
                hasExplicitDefault: _hasExplicitDefault
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
                Description,
                isRemaining: _isRemaining,
                hasExplicitDefault: _hasExplicitDefault
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

            throw new CommandConfigurationException(
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

        internal override bool TryParseAll(
            BorrowedCommandArguments arguments,
            int start,
            object[] parsedValues,
            int slot,
            out CommandArg failedToken,
            out string validationError
        )
        {
            if (!IsRemaining)
            {
                throw new InvalidOperationException(
                    $"Argument '{Name}' is not a remaining argument; "
                        + "TryParseAll collects only a command's unbounded trailing argument."
                );
            }

            /*
                Every token parses straight into the typed array and each
                value validates with full type knowledge; the collected
                array reaches the shared parsed-values buffer as one typed
                hand-off at its own slot, like any single parsed value.
             */
            T[] values = new T[Math.Max(0, arguments.Count - start)];
            for (int i = 0; i < values.Length; ++i)
            {
                CommandArg input = arguments[start + i];
                if (!input.TryGet(out values[i], _parserOverride))
                {
                    failedToken = input;
                    validationError = null;
                    return false;
                }

                string error = ValidateValue(values[i]);
                if (error != null)
                {
                    failedToken = input;
                    validationError = error;
                    return false;
                }
            }

            parsedValues[slot] = values;
            failedToken = default;
            validationError = null;
            return true;
        }

        internal override object GetDefault()
        {
            return _defaultValue;
        }

        internal override string ValidateParsed(object parsed)
        {
            return ValidateValue((T)parsed);
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
            if (_isRemaining)
            {
                builder.Append("...");
            }

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

        private string ValidateValue(T value)
        {
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
    }
}
