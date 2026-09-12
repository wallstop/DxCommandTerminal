namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Invocation-scoped typed view over the parsed arguments of one
    ///     builder command. Values are parsed and validated by the command's
    ///     definition before the handler runs; a value read with the wrong
    ///     type is a programming error and throws, while user-input mistakes
    ///     are rejected before the handler runs and never reach this view.
    ///     Null defaults (optional reference-type and nullable arguments
    ///     without an explicit default) read as <c>default(T)</c>.
    /// </summary>
    /// <remarks>
    ///     Valid only during the handler callback that received it, like the
    ///     underlying borrowed argument view.
    /// </remarks>
    public readonly struct CommandArguments
    {
        /// <summary>The execution environment of the current invocation.</summary>
        public CommandExecutionContext Context { get; }

        /// <summary>Number of parsed arguments, always matching the definition.</summary>
        public int Count => _values?.Length ?? 0;

        /// <summary>
        ///     The raw, invocation-scoped borrowed view over the input
        ///     arguments, for handlers that need original text or quoting.
        ///     Valid only during the handler callback.
        /// </summary>
        public BorrowedCommandArguments Raw => _raw;

        private readonly CommandArgument[] _specs;
        private readonly object[] _values;
        private readonly BorrowedCommandArguments _raw;

        internal CommandArguments(
            CommandArgument[] specs,
            object[] values,
            BorrowedCommandArguments raw,
            CommandExecutionContext context
        )
        {
            _specs = specs;
            _values = values;
            _raw = raw;
            Context = context;
        }

        private static T Cast<T>(object value, CommandArgument spec)
        {
            if (value is T typed)
            {
                return typed;
            }

            /*
                An omitted optional argument without an explicit default reads
                as default(T): null is a valid, correctly typed read for
                reference types and nullable value types alike.
             */
            if (value == null && ReadsAsNull<T>())
            {
                return default;
            }

            throw new CommandArgumentTypeMismatchException(
                $"Argument '{spec.Name}' is declared as {spec.TypeName}; "
                    + $"requested {typeof(T).Name}.",
                spec.Name,
                spec.DeclaredType,
                typeof(T)
            );
        }

        private static bool ReadsAsNull<T>()
        {
            return !typeof(T).IsValueType || Nullable.GetUnderlyingType(typeof(T)) != null;
        }

        /// <summary>
        ///     The parsed value at <paramref name="index"/>, in definition
        ///     order. Throws <see cref="ArgumentOutOfRangeException"/> for an
        ///     out-of-range index and
        ///     <see cref="CommandArgumentTypeMismatchException"/> when the
        ///     requested type differs from the argument's declared type.
        /// </summary>
        public T Get<T>(int index)
        {
            if (_values == null || _values.Length <= (uint)index)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Cast<T>(_values[index], _specs[index]);
        }

        /// <summary>
        ///     Attempts to read the parsed value at <paramref name="index"/> as
        ///     <typeparamref name="T"/>; false on a type mismatch or an
        ///     out-of-range index. A null default reads as
        ///     <c>default(T)</c> for reference types.
        /// </summary>
        public bool TryGet<T>(int index, out T value)
        {
            if (_values == null || _values.Length <= (uint)index)
            {
                value = default;
                return false;
            }

            object raw = _values[index];
            if (raw is T typed)
            {
                value = typed;
                return true;
            }

            if (raw == null && ReadsAsNull<T>())
            {
                value = default;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        ///     The parsed value of the argument named <paramref name="name"/>.
        ///     Throws <see cref="ArgumentException"/> for an unknown name and
        ///     <see cref="CommandArgumentTypeMismatchException"/> on a type
        ///     mismatch.
        /// </summary>
        public T Get<T>(string name)
        {
            int index = IndexOfArgument(name);
            if (index < 0)
            {
                throw new ArgumentException(
                    $"No argument named '{name}' on this command.",
                    nameof(name)
                );
            }

            return Get<T>(index);
        }

        /// <summary>
        ///     Attempts to read the parsed value of the named argument as
        ///     <typeparamref name="T"/>; false on an unknown name or a type
        ///     mismatch.
        /// </summary>
        public bool TryGet<T>(string name, out T value)
        {
            int index = IndexOfArgument(name);
            if (index < 0)
            {
                value = default;
                return false;
            }

            return TryGet(index, out value);
        }

        private int IndexOfArgument(string name)
        {
            if (name == null || _specs == null)
            {
                return -1;
            }

            for (int i = 0; i < _specs.Length; ++i)
            {
                if (string.Equals(_specs[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
