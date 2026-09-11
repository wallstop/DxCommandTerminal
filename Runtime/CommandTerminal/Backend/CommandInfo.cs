namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    public readonly struct CommandInfo
    {
        public readonly Action<CommandArg[]> proc;
        public readonly int minArgCount;

        /// <summary>
        ///     Maximum number of arguments the command accepts, or
        ///     <c>null</c> for unbounded. Legacy registrations that pass a
        ///     negative value are normalized to <c>null</c>.
        /// </summary>
        public readonly int? maxArgCount;

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
        ///     explicit context metadata keep <see cref="CommandExecutionContextSets.All"/>,
        ///     their previous availability everywhere.
        /// </summary>
        public readonly CommandExecutionContexts executionContexts;

        public CommandInfo(
            Action<CommandArg[]> proc,
            int minArgCount,
            int? maxArgCount,
            string help,
            string hint,
            bool addToHistory = true
        )
            : this(
                proc,
                null,
                null,
                CommandExecutionContextSets.All,
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
            int? maxArgCount,
            string help,
            string hint,
            bool addToHistory = true
        )
        {
            this.proc = proc;
            this.handler = handler;
            this.completionProvider = completionProvider;
            this.executionContexts = executionContexts;
            this.maxArgCount = NormalizeMaxArgCount(maxArgCount);
            this.minArgCount = minArgCount;
            this.help = help;
            this.hint = hint;
            this.addToHistory = addToHistory;
        }

        /// <summary>
        ///     Negative bounds were the legacy "unbounded" spelling; they
        ///     normalize to <c>null</c> so the sentinel has no meaning here.
        /// </summary>
        private static int? NormalizeMaxArgCount(int? maxArgCount)
        {
            return maxArgCount is int bound && 0 <= bound ? bound : null;
        }
    }
}
