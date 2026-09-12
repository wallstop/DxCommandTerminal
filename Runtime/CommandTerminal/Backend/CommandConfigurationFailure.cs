namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    ///     Classifies one <see cref="CommandConfigurationException"/> so
    ///     callers branch on the failure kind instead of parsing
    ///     <see cref="System.Exception.Message"/>. Every definition-time
    ///     misconfiguration the builder and argument specs reject has a kind.
    /// </summary>
    public enum CommandConfigurationFailure
    {
        /// <summary>
        ///     Unclassified. Set only by the legacy message-only constructor
        ///     and preserved through scope re-throws.
        /// </summary>
        None = 0,

        /// <summary>A command, subcommand, or argument name is missing or blank.</summary>
        EmptyName,

        /// <summary>A subcommand or argument name collides with an existing one.</summary>
        DuplicateName,

        /// <summary>The command registered without a handler.</summary>
        MissingHandler,

        /// <summary>A required argument is declared after an optional argument.</summary>
        ArgumentOrdering,

        /// <summary>
        ///     The unbounded trailing argument is misused: an argument
        ///     declared after it, a second one, or a default on it.
        /// </summary>
        InvalidRemainingArgument,

        /// <summary>
        ///     The argument's type has no parser and no parser override was
        ///     configured; <see cref="CommandConfigurationException.ArgumentType"/>
        ///     names the unparseable type.
        /// </summary>
        UnparseableArgumentType,

        /// <summary>
        ///     An argument feature does not support the argument's type (bool
        ///     or enum choices, a numeric range on a non-comparable type);
        ///     <see cref="CommandConfigurationException.ArgumentType"/> names
        ///     the unsupported type.
        /// </summary>
        UnsupportedArgumentFeature,

        /// <summary>Static choices are empty or contain a null element.</summary>
        InvalidChoices,

        /// <summary>A range's bounds are inverted (min above max).</summary>
        InvalidRange,

        /// <summary>An optional argument's default fails the argument's own validation.</summary>
        InvalidDefault,

        /// <summary>
        ///     Subcommand composition is misconfigured: a subcommand sets its
        ///     own execution contexts or history policy, or a routed parent
        ///     declares its own arguments.
        /// </summary>
        InvalidSubcommandConfiguration,

        /// <summary>An argument configuration callback returned null.</summary>
        NullConfigurationResult,
    }
}
