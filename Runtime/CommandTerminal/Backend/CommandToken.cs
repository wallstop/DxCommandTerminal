namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    ///     One parsed token of an input line, with the span it came from.
    ///     Contents and quote flags match
    ///     <see cref="CommandShell.TryEatArgument"/> exactly; the span lets
    ///     completion address the raw text around the caret.
    /// </summary>
    internal readonly struct CommandToken
    {
        public string Contents { get; }
        public char? StartQuote { get; }
        public char? EndQuote { get; }

        /// <summary>Start index of the token's contents in the line, quotes
        /// excluded.</summary>
        public int Start { get; }

        /// <summary>End index (exclusive) of the token's contents in the line,
        /// closing quote excluded.</summary>
        public int End { get; }

        public CommandToken(string contents, char? startQuote, char? endQuote, int start, int end)
        {
            Contents = contents;
            StartQuote = startQuote;
            EndQuote = endQuote;
            Start = start;
            End = end;
        }

        public CommandArg ToArgument()
        {
            return new CommandArg(Contents, StartQuote, EndQuote);
        }
    }
}
