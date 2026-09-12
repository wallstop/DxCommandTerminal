namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Thrown by <see cref="CommandArguments.Get{T}"/> when the requested
    ///     type differs from the argument's declared type. A handler
    ///     programming error: user-input mistakes are rejected by the
    ///     definition's parse and validation pass before the handler runs.
    /// </summary>
    /// <remarks>
    ///     Derives from <see cref="InvalidOperationException"/>, so existing
    ///     catchers keep working, while new callers read
    ///     <see cref="ArgumentName"/>, <see cref="DeclaredType"/>, and
    ///     <see cref="RequestedType"/> without parsing
    ///     <see cref="Exception.Message"/>.
    /// </remarks>
    public sealed class CommandArgumentTypeMismatchException : InvalidOperationException
    {
        /// <summary>The argument whose parsed value was read.</summary>
        public string ArgumentName { get; }

        /// <summary>The type the argument was declared with.</summary>
        public Type DeclaredType { get; }

        /// <summary>The type the read requested.</summary>
        public Type RequestedType { get; }

        public CommandArgumentTypeMismatchException(
            string message,
            string argumentName,
            Type declaredType,
            Type requestedType
        )
            : base(message)
        {
            ArgumentName = argumentName;
            DeclaredType = declaredType;
            RequestedType = requestedType;
        }
    }
}
