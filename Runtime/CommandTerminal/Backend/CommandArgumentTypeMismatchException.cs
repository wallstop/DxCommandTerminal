namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Thrown by <see cref="CommandArguments.Get{T}"/> when the parsed
    ///     value stored for the argument is not readable as the requested
    ///     type. A handler programming error: user-input mistakes are
    ///     rejected by the definition's parse and validation pass before the
    ///     handler runs. Note remaining arguments store one array of their
    ///     element type, so a read of the element type itself reports
    ///     <see cref="StoredType"/> as that array type.
    /// </summary>
    /// <remarks>
    ///     Derives from <see cref="InvalidOperationException"/>, so existing
    ///     catchers keep working, while new callers read
    ///     <see cref="ArgumentName"/>, <see cref="StoredType"/>, and
    ///     <see cref="RequestedType"/> without parsing
    ///     <see cref="Exception.Message"/>.
    /// </remarks>
    public sealed class CommandArgumentTypeMismatchException : InvalidOperationException
    {
        /// <summary>The argument whose parsed value was read.</summary>
        public string ArgumentName { get; }

        /// <summary>The type of the parsed value stored for the argument.</summary>
        public Type StoredType { get; }

        /// <summary>The type the read requested.</summary>
        public Type RequestedType { get; }

        public CommandArgumentTypeMismatchException(
            string message,
            string argumentName,
            Type storedType,
            Type requestedType
        )
            : base(message)
        {
            ArgumentName = argumentName;
            StoredType = storedType;
            RequestedType = requestedType;
        }
    }
}
