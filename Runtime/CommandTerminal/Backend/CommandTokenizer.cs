namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;

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

    /// <summary>
    ///     Single tokenization model shared by execution and completion.
    ///     Production (<see cref="CommandShell.TryEatArgument"/>) semantics
    ///     are preserved exactly: leading whitespace is skipped with
    ///     <see cref="char.IsWhiteSpace"/>, both quote characters open a
    ///     quoted token, an unclosed quote consumes the rest of the line, and
    ///     unquoted tokens end only at a space character.
    /// </summary>
    internal static class CommandTokenizer
    {
        public static void Tokenize(string line, List<CommandToken> tokens)
        {
            if (line == null)
            {
                return;
            }

            int index = 0;
            int length = line.Length;
            while (index < length)
            {
                while (index < length && char.IsWhiteSpace(line[index]))
                {
                    ++index;
                }

                if (length <= index)
                {
                    break;
                }

                char firstChar = line[index];
                if (CommandArg.Quotes.Contains(firstChar))
                {
                    int closingQuoteIndex = -1;
                    for (int i = index + 1; i < length; ++i)
                    {
                        if (line[i] == firstChar)
                        {
                            closingQuoteIndex = i;
                            break;
                        }
                    }

                    if (closingQuoteIndex < 0)
                    {
                        // Unclosed quote consumes the rest of the line
                        // (excluding the opening quote), matching
                        // TryEatArgument.
                        tokens.Add(
                            new CommandToken(
                                line.Substring(index + 1),
                                firstChar,
                                null,
                                index + 1,
                                length
                            )
                        );
                        return;
                    }

                    tokens.Add(
                        new CommandToken(
                            line.Substring(index + 1, closingQuoteIndex - index - 1),
                            firstChar,
                            firstChar,
                            index + 1,
                            closingQuoteIndex
                        )
                    );
                    index = closingQuoteIndex + 1;
                }
                else
                {
                    int spaceIndex = line.IndexOf(' ', index);
                    int end = spaceIndex < 0 ? length : spaceIndex;
                    tokens.Add(
                        new CommandToken(line.Substring(index, end - index), null, null, index, end)
                    );
                    index = spaceIndex < 0 ? length : end + 1;
                }
            }
        }

        /// <summary>
        ///     Locates the token the caret is editing. Returns false only when
        ///     the caret opens the command-name slot (no tokens precede it),
        ///     which is command-name completion territory, not argument
        ///     completion.
        /// </summary>
        /// <remarks>
        ///     The caret edits an existing token when the token's content span
        ///     satisfies <c>Start &lt; caret &lt;= End</c>, or when a
        ///     zero-length token (an empty quoted argument) sits exactly at
        ///     the caret. Otherwise the caret opens a new argument at the
        ///     caret position; its replacement range is empty and its token
        ///     text is empty. Mid-token text after the caret is part of the
        ///     replaced span, matching shell completion conventions.
        /// </remarks>
        public static bool TryFindActiveToken(
            string line,
            int caret,
            List<CommandToken> tokens,
            out int activeTokenIndex,
            out int replacementStart,
            out int replacementLength,
            out bool isNewArgument
        )
        {
            activeTokenIndex = -1;
            replacementStart = -1;
            replacementLength = -1;
            isNewArgument = false;

            if (line == null || tokens == null || tokens.Count == 0)
            {
                return false;
            }

            caret = Math.Clamp(caret, 0, line.Length);
            for (int i = 0; i < tokens.Count; ++i)
            {
                CommandToken token = tokens[i];
                if (token.Start == token.End)
                {
                    // A zero-length token (an empty quoted argument) is
                    // active exactly when the caret sits on it.
                    if (token.Start != caret)
                    {
                        continue;
                    }
                }
                else if (!(token.Start < caret && caret <= token.End))
                {
                    continue;
                }

                activeTokenIndex = i;
                replacementStart = token.Start;
                replacementLength = token.End - token.Start;
                return true;
            }

            /*
                The caret sits at a whitespace boundary (or past the last
                token): a new, empty argument opens at the caret.
             */
            int precedingTokens = 0;
            foreach (CommandToken token in tokens)
            {
                if (token.End < caret)
                {
                    ++precedingTokens;
                }
            }

            activeTokenIndex = precedingTokens;
            replacementStart = caret;
            replacementLength = 0;
            isNewArgument = true;
            return true;
        }
    }
}
