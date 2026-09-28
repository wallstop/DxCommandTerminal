namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     Single tokenization model shared by execution and completion.
    ///     Production (<see cref="CommandShell.TryEatArgument"/>) semantics
    ///     are preserved exactly: leading whitespace is skipped, both quote
    ///     characters open a quoted token, an unclosed quote consumes the rest
    ///     of the line, and unquoted tokens end at the next whitespace
    ///     character.
    /// </summary>
    /// <remarks>
    ///     The terminator is the whitespace class, not the space character,
    ///     and both ends of a token have to agree or a command runs on
    ///     different arguments than the one completion edited. A tab or a
    ///     newline inside a token is not a name a developer typed: it is a
    ///     pasted block, a log line, or a message from another system, and it
    ///     has to arrive as the arguments it reads as.
    ///     <para>
    ///         The rule is about a line. A <c>$variable</c> is substituted after
    ///         the split, so a stored value is substituted whole and keeps
    ///         whatever it holds - <c>set-variable</c> stores one value by
    ///         design, and splitting it here would undo that.
    ///     </para>
    /// </remarks>
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
                while (index < length && IsSeparator(line[index]))
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
                        /*
                           Unclosed quote consumes the rest of the line
                           (excluding the opening quote), matching
                           TryEatArgument.
                        */
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
                    int end = index;
                    while (end < length && !IsSeparator(line[end]))
                    {
                        ++end;
                    }

                    tokens.Add(
                        new CommandToken(line.Substring(index, end - index), null, null, index, end)
                    );
                    index = end;
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
        ///     The caret edits the token whose content span contains it
        ///     (<c>Start &lt;= caret &lt;= End</c>; a zero-length token is
        ///     active when the caret sits on it). Otherwise the caret opens a
        ///     new argument at the caret position; its replacement range is
        ///     empty and its token text is empty. Mid-token text after the
        ///     caret is part of the replaced span, matching shell completion
        ///     conventions.
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
            if (line == null || tokens == null || tokens.Count == 0)
            {
                activeTokenIndex = -1;
                replacementStart = -1;
                replacementLength = -1;
                isNewArgument = false;
                return false;
            }

            caret = Math.Clamp(caret, 0, line.Length);
            int tokenCount = tokens.Count;
            for (int i = 0; i < tokenCount; ++i)
            {
                CommandToken token = tokens[i];
                if (caret < token.Start || token.End < caret)
                {
                    continue;
                }

                activeTokenIndex = i;
                replacementStart = token.Start;
                replacementLength = token.End - token.Start;
                isNewArgument = false;
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

        public static bool TryPrepareInsertion(
            string input,
            string value,
            int start,
            int length,
            bool tokenQuoted,
            out string insertion,
            out int replacementStart,
            out int replacementLength,
            bool wholeToken = true
        )
        {
            if (
                input == null
                || string.IsNullOrEmpty(value)
                || start < 0
                || length < 0
                || input.Length < start
                || input.Length - start < length
            )
            {
                insertion = string.Empty;
                replacementStart = start;
                replacementLength = length;
                return false;
            }

            if (!wholeToken)
            {
                insertion =
                    tokenQuoted ? value
                    : TrySerializeValue(value, out string serialized, out bool _) ? serialized
                    : value;
                replacementStart = start;
                replacementLength = length;
                return true;
            }

            if (tokenQuoted)
            {
                if (start == 0 || !CommandArg.Quotes.Contains(input[start - 1]))
                {
                    insertion = string.Empty;
                    replacementStart = start;
                    replacementLength = length;
                    return false;
                }

                char quote = input[start - 1];
                int end = start + length;
                int closing = input.IndexOf(quote, start);
                if (closing != end && !(closing < 0 && end == input.Length))
                {
                    insertion = string.Empty;
                    replacementStart = start;
                    replacementLength = length;
                    return false;
                }

                --start;
                ++length;
                if (closing == end)
                {
                    ++length;
                }
                if (value.IndexOf(quote) < 0)
                {
                    insertion = $"{quote}{value}{quote}";
                    replacementStart = start;
                    replacementLength = length;
                    return true;
                }
            }

            if (!TrySerializeValue(value, out insertion, out bool quotedInsertion))
            {
                insertion = string.Empty;
                replacementStart = start;
                replacementLength = length;
                return false;
            }

            if (
                !quotedInsertion
                && start + length < input.Length
                && !IsSeparator(input[start + length])
            )
            {
                insertion = string.Empty;
                replacementStart = start;
                replacementLength = length;
                return false;
            }

            replacementStart = start;
            replacementLength = length;
            return true;
        }

        public static string QuoteInsertionIfNeeded(string insertion, bool tokenQuoted)
        {
            if (tokenQuoted)
            {
                return insertion;
            }

            TryPrepareInsertion(
                string.Empty,
                insertion,
                0,
                0,
                false,
                out string serialized,
                out _,
                out _,
                wholeToken: false
            );
            return serialized;
        }

        /*
            The one definition of a token boundary. A token ends at any
            whitespace, not only at a space, so every caller splits a line the
            same way: Tokenize below, CommandShell.TryEatArgument (production
            execution and the public parse entry), the completion quoting and
            replacement checks here, and TextFieldPaste.Flatten (which
            collapses a run to the space this returns).
         */
        internal static bool IsSeparator(char c)
        {
            return char.IsWhiteSpace(c);
        }

        private static bool TrySerializeValue(
            string value,
            out string insertion,
            out bool quotedInsertion
        )
        {
            bool requiresQuote = value[0] == '$' || CommandArg.Quotes.Contains(value[0]);

            /*
                The scan starts at zero, not one: a value that leads with
                whitespace carries a separator at index zero, and starting
                past it would leave requiresQuote false, so the value would
                be inserted bare and the tokenizer would drop the run
                instead of quoting it.
             */
            for (int i = 0; i < value.Length && !requiresQuote; ++i)
            {
                requiresQuote = IsSeparator(value[i]);
            }

            foreach (char quote in CommandArg.Quotes)
            {
                if (char.IsWhiteSpace(quote) || 0 <= value.IndexOf(quote))
                {
                    continue;
                }

                if (requiresQuote || 0 <= value.IndexOf('"') || 0 <= value.IndexOf('\''))
                {
                    insertion = $"{quote}{value}{quote}";
                    quotedInsertion = true;
                    return true;
                }

                break;
            }

            insertion = requiresQuote ? string.Empty : value;
            quotedInsertion = false;
            return !requiresQuote;
        }
    }
}
