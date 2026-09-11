namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Declarative registration input for one context-aware command.
    ///     Associates the handler, completion provider, help metadata, and
    ///     execution eligibility in a single definition.
    /// </summary>
    /// <remarks>
    ///     Instances are configuration objects; <see cref="CommandShell.AddCommand(CommandDefinition)"/>
    ///     snapshots the current values into the shell, so later edits to a
    ///     definition do not affect registered commands. Exactly one handler
    ///     must be set before registration.
    /// </remarks>
    public sealed class CommandDefinition
    {
        /// <summary>Command name, matched case-insensitively like every
        /// registered command. Spaces are stripped at registration.</summary>
        public string Name { get; set; }

        /// <summary>Help text shown by the built-in <c>help</c> command.</summary>
        public string Help { get; set; } = string.Empty;

        /// <summary>Usage hint appended to argument-count errors.</summary>
        public string Hint { get; set; }

        /// <summary>Minimum number of arguments the command accepts.</summary>
        public int MinArgCount { get; set; }

        /// <summary>
        ///     Maximum number of arguments the command accepts, or
        ///     <c>null</c> for unbounded.
        /// </summary>
        public int? MaxArgCount { get; set; }

        /// <summary>Whether invocations are recorded in the command history.</summary>
        public bool AddToHistory { get; set; } = true;

        /// <summary>
        ///     Environments the command is eligible to run in. Defaults to
        ///     gameplay contexts (Editor Play Mode and players); Edit Mode
        ///     execution requires explicit opt-in by including
        ///     <see cref="CommandExecutionContexts.EditorEditMode"/>.
        /// </summary>
        public CommandExecutionContexts Contexts { get; set; } =
            CommandExecutionContextSets.Gameplay;

        /// <summary>
        ///     Context-aware handler receiving the execution context and a
        ///     borrowed, read-only argument view. Exactly one of this and
        ///     <see cref="LegacyHandler"/> must be set.
        /// </summary>
        public CommandHandler Handler { get; set; }

        /// <summary>
        ///     Existing <see cref="CommandArg[]"/>-style handler, matching the
        ///     signature every prior registration path uses. The shell
        ///     materializes a fresh owned array for each invocation; the
        ///     handler may retain it. Exactly one of this and
        ///     <see cref="Handler"/> must be set.
        /// </summary>
        public Action<CommandArg[]> LegacyHandler { get; set; }

        /// <summary>
        ///     Synchronous completion provider for the command's arguments.
        ///     Null leaves completion to the history-based suggestions that
        ///     every command without a provider shares.
        /// </summary>
        public CommandCompletionProvider CompletionProvider { get; set; }
    }
}
