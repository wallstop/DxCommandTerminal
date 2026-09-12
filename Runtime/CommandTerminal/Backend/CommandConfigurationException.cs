namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Thrown when a <see cref="CommandBuilder"/> or
    ///     <see cref="CommandDefinition"/> is misconfigured at definition
    ///     time: missing handlers, duplicate names, required-after-optional
    ///     ordering, unparseable argument types, defaults failing their own
    ///     validation, and subcommand conflicts.
    /// </summary>
    /// <remarks>
    ///     Derives from <see cref="InvalidOperationException"/>, so existing
    ///     handlers of definition-time failures keep working, while new
    ///     callers branch on <see cref="Failure"/> and read the structured
    ///     identity fields without parsing <see cref="Exception.Message"/>.
    /// </remarks>
    public sealed class CommandConfigurationException : InvalidOperationException
    {
        /// <summary>The command whose configuration was rejected, when known.</summary>
        public string CommandName { get; }

        /// <summary>The machine-readable classification of this failure.</summary>
        public CommandConfigurationFailure Failure { get; }

        /// <summary>
        ///     The argument the failure concerns, when the failure is
        ///     argument-scoped (duplicate names, ordering, remaining-argument
        ///     misuse, choices, ranges, defaults, and parser problems).
        /// </summary>
        public string ArgumentName { get; }

        /// <summary>
        ///     The subcommand the failure concerns, when the failure is
        ///     subcommand-scoped (duplicate subcommand names and rejected
        ///     subcommand configuration).
        /// </summary>
        public string SubcommandName { get; }

        /// <summary>
        ///     The argument type the failure concerns, when the failure is
        ///     type-scoped (unparseable types and type-incompatible features).
        /// </summary>
        public Type ArgumentType { get; }

        public CommandConfigurationException(string message, string commandName = null)
            : this(CommandConfigurationFailure.None, message, commandName) { }

        public CommandConfigurationException(
            CommandConfigurationFailure failure,
            string message,
            string commandName = null,
            string argumentName = null,
            string subcommandName = null,
            Type argumentType = null,
            Exception innerException = null
        )
            : base(message, innerException)
        {
            Failure = failure;
            CommandName = commandName;
            ArgumentName = argumentName;
            SubcommandName = subcommandName;
            ArgumentType = argumentType;
        }
    }
}
