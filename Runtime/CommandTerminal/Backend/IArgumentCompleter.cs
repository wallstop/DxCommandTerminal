namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;

    /// <summary>
    /// Context for argument completion queries.
    /// </summary>
    public readonly struct CommandCompletionContext
    {
        public readonly string FullText;
        public readonly string CommandName;
        public readonly IReadOnlyList<CommandArg> ArgsBeforeCursor;
        public readonly string PartialArg;
        public readonly int ArgIndex;
        public readonly CommandShell Shell;

        public CommandCompletionContext(
            string fullText,
            string commandName,
            IReadOnlyList<CommandArg> argsBeforeCursor,
            string partialArg,
            int argIndex,
            CommandShell shell
        )
        {
            FullText = fullText;
            CommandName = commandName;
            ArgsBeforeCursor = argsBeforeCursor;
            PartialArg = partialArg;
            ArgIndex = argIndex;
            Shell = shell;
        }
    }

    /// <summary>
    /// Implement to provide dynamic, argument-aware completions for a command.
    /// </summary>
    public interface IArgumentCompleter
    {
        IEnumerable<string> Complete(CommandCompletionContext context);
    }
}
