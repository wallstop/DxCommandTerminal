namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    public readonly struct CommandInfo
    {
        public readonly Action<CommandArg[]> proc;
        public readonly int minArgCount;
        public readonly int maxArgCount;
        public readonly string help;
        public readonly string hint;
        public readonly bool addToHistory;

        /// <summary>
        ///     Context-aware handler. Null for commands registered through the
        ///     legacy <see cref="CommandArg[]"/> surface, which receive a
        ///     materialized array instead.
        /// </summary>
        public readonly CommandHandler handler;

        /// <summary>
        ///     Synchronous argument completion provider. Null leaves completion
        ///     to the history-based suggestions shared by every command
        ///     without a provider.
        /// </summary>
        public readonly CommandCompletionProvider completionProvider;

        /// <summary>
        ///     Environments the command may run in. Commands registered without
        ///     explicit context metadata keep <see cref="CommandExecutionContexts.All"/>,
        ///     their previous availability everywhere.
        /// </summary>
        public readonly CommandExecutionContexts executionContexts;

        public CommandInfo(
            Action<CommandArg[]> proc,
            int minArgCount,
            int maxArgCount,
            string help,
            string hint,
            bool addToHistory = true
        )
            : this(
                proc,
                null,
                null,
                CommandExecutionContexts.All,
                minArgCount,
                maxArgCount,
                help,
                hint,
                addToHistory
            ) { }

        public CommandInfo(
            Action<CommandArg[]> proc,
            CommandHandler handler,
            CommandCompletionProvider completionProvider,
            CommandExecutionContexts executionContexts,
            int minArgCount,
            int maxArgCount,
            string help,
            string hint,
            bool addToHistory = true
        )
        {
            this.proc = proc;
            this.handler = handler;
            this.completionProvider = completionProvider;
            this.executionContexts = executionContexts;
            this.maxArgCount = maxArgCount;
            this.minArgCount = minArgCount;
            this.help = help;
            this.hint = hint;
            this.addToHistory = addToHistory;
        }
    }
}
