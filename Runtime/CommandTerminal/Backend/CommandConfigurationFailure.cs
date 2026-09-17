namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

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
        [Obsolete("Use a classified failure")]
        None = 0,

        /// <summary>A command, subcommand, or argument name is missing or blank.</summary>
        EmptyName = 1,

        /// <summary>A subcommand or argument name collides with an existing one.</summary>
        DuplicateName = 2,

        /// <summary>The command registered without a handler.</summary>
        MissingHandler = 3,

        /// <summary>A required argument is declared after an optional argument.</summary>
        ArgumentOrdering = 4,

        /// <summary>
        ///     The unbounded trailing argument is misused: an argument
        ///     declared after it, a second one, or a default on it.
        /// </summary>
        InvalidRemainingArgument = 5,

        /// <summary>
        ///     The argument's type has no parser and no parser override was
        ///     configured; <see cref="CommandConfigurationException.ArgumentType"/>
        ///     names the unparseable type.
        /// </summary>
        UnparseableArgumentType = 6,

        /// <summary>
        ///     An argument feature does not support the argument's type (bool
        ///     or enum choices, a numeric range on a non-comparable type);
        ///     <see cref="CommandConfigurationException.ArgumentType"/> names
        ///     the unsupported type.
        /// </summary>
        UnsupportedArgumentFeature = 7,

        /// <summary>Static choices are empty or contain a null element.</summary>
        InvalidChoices = 8,

        /// <summary>A range's bounds are inverted (min above max).</summary>
        InvalidRange = 9,

        /// <summary>An optional argument's default fails the argument's own validation.</summary>
        InvalidDefault = 10,

        /// <summary>
        ///     Subcommand composition is misconfigured: a subcommand sets its
        ///     own execution contexts or history policy, or a routed parent
        ///     declares its own arguments.
        /// </summary>
        InvalidSubcommandConfiguration = 11,

        /// <summary>An argument configuration callback returned null.</summary>
        NullConfigurationResult = 12,
    }
}
