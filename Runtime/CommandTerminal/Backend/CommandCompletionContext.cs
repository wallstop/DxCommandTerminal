namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;

    /// <summary>
    ///     Immutable description of one completion request, built by the shell
    ///     from the current input, caret, and parsed token stream. Completion
    ///     providers read it to produce candidates for the active argument.
    /// </summary>
    public readonly struct CommandCompletionContext
    {
        /// <summary>Execution environment the request runs in.</summary>
        public CommandExecutionContext ExecutionContext { get; }

        /// <summary>The full, unmodified input line.</summary>
        public string Input { get; }

        /// <summary>Caret position within <see cref="Input"/> at request time.</summary>
        public int CaretIndex { get; }

        /// <summary>
        ///     Zero-based stage of the argument being completed: 0 completes
        ///     the first argument after the command name. Arguments before the
        ///     active one are available through
        ///     <see cref="PrecedingArguments"/>.
        /// </summary>
        public int ActiveArgumentIndex { get; }

        /// <summary>
        ///     Read-only view over the parsed arguments that precede the
        ///     active token, in input order and with their original quoting.
        ///     Valid only during the provider callback that received it.
        /// </summary>
        public BorrowedCommandArguments PrecedingArguments { get; }

        /// <summary>
        ///     Text of the token being completed, from its start to the caret.
        ///     Empty when the caret opens a new argument.
        /// </summary>
        public string Token { get; }

        /// <summary>
        ///     Start index within <see cref="Input"/> of the range an accepted
        ///     completion replaces: the active token's span, quotes excluded.
        /// </summary>
        public int ReplacementStart { get; }

        /// <summary>
        ///     Length within <see cref="Input"/> of the range an accepted
        ///     completion replaces.
        /// </summary>
        public int ReplacementLength { get; }

        /// <summary>True when the active token was opened with a quote;
        /// insertions then go inside the existing quotes verbatim.</summary>
        public bool IsQuoted { get; }

        /// <summary>The quote character that opened the active token, if any.</summary>
        public char? QuoteCharacter { get; }

        internal CommandCompletionContext(
            CommandExecutionContext executionContext,
            string input,
            int caretIndex,
            int activeArgumentIndex,
            List<CommandArg> precedingArguments,
            string token,
            int replacementStart,
            int replacementLength,
            bool isQuoted,
            char? quoteCharacter
        )
        {
            ExecutionContext = executionContext;
            Input = input;
            CaretIndex = caretIndex;
            ActiveArgumentIndex = activeArgumentIndex;
            PrecedingArguments = new BorrowedCommandArguments(precedingArguments);
            Token = token;
            ReplacementStart = replacementStart;
            ReplacementLength = replacementLength;
            IsQuoted = isQuoted;
            QuoteCharacter = quoteCharacter;
        }
    }
}
