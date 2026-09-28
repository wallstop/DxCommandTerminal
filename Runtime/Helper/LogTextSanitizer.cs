namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System.Globalization;
    using System.Text;

    /*
        The terminal is the developer's source of truth for what a game
        printed, so log text is data, not markup. Every source funnels
        through CommandLog.HandleLog, and this runs there: a caller string
        carrying a control character, a bidi override, or a zero-width
        joiner reaches the log list verbatim, where it either renders as
        noise or - worse - reads one way and copies out as another.
        "Admin<U+202E>exe" is the case that matters.

        Two rendering paths never pass this funnel: the terminal's
        suggestion bar and the palette's result rows, which print a
        completion candidate - a history line, a GameObject name, a
        description. They call this too, so the same text reads one way
        wherever a developer reads it. A row shows the escaped text and still
        applies the raw one: the escape is for the reader, and the command
        the developer runs is the text they chose.

        Escaping rather than stripping is the deliberate choice: dropping
        the override would show "Adminexe", a name that looks legitimate and
        is not the one in the project. The escape is the honest rendering.

        The backslash is left alone, which is a stated limit: a name holding
        the literal text "Admin\u202Eexe" prints the same as a name holding a
        real override, so the output is not machine-decodable. What a reader
        relies on still holds - a real override is always visible as an
        escape - and escaping the backslash costs more than it buys, because
        it would double every separator, so "C:\Users\dev" would read wrong,
        and it would break the idempotence the trace command depends on.

        A reserved character can expand to six (or ten) characters, so a log
        line built entirely from them grows. That is the cost of making an
        invisible character visible, and the log buffer is already bounded by
        item count.

        Nothing is truncated here. A cap was tried and removed: it looked
        motivated by a clipping claim that is not true (the output label
        wraps inside a vertical scroller, so a long line is reachable, not
        clipped), and truncating at the funnel destroys log data in the one
        tool whose job is to be the record. The one sink that genuinely
        overflows - the palette's output panel, which has no scroller and no
        max height - is bounded where it renders instead.

        Idempotence is a property, not a hope: every escape is printable
        ASCII and a line break stays a line break, so a second pass returns
        the same reference. That matters because the trace command re-logs an
        already-sanitized message.
     */
    internal static class LogTextSanitizer
    {
        private const string HexDigits = "0123456789ABCDEF";

        public static string Sanitize(string message)
        {
            if (string.IsNullOrEmpty(message) || !NeedsSanitizing(message))
            {
                return message;
            }

            int length = message.Length;
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(length + 16);
            StringBuilder builder = scope.Builder;
            for (int i = 0; i < length; ++i)
            {
                char c = message[i];
                if (c == '\r')
                {
                    /*
                        A carriage return is a line break, not a control
                        character to show: the stack-trace reducer joins
                        frames with Environment.NewLine, so on Windows every
                        frame would otherwise carry a visible escape.
                     */
                    builder.Append('\n');
                    if (i + 1 < length && message[i + 1] == '\n')
                    {
                        ++i;
                    }

                    continue;
                }

                if (IsEscaped(c))
                {
                    AppendEscape(builder, c);
                    continue;
                }

                /*
                    A scalar above the BMP arrives as a surrogate pair, and
                    neither half is Format on its own - so the category has to
                    be asked of the pair, or an invisible tag character
                    (U+E0001 and its neighbours) walks straight through. Only
                    the reserved categories are escaped, so an emoji's
                    regional indicators, which are ordinary symbols, survive.
                 */
                if (IsSurrogatePairAt(message, i))
                {
                    if (IsReserved(CharUnicodeInfo.GetUnicodeCategory(message, i)))
                    {
                        AppendExtendedEscape(builder, message, i);
                    }
                    else
                    {
                        builder.Append(c).Append(message[i + 1]);
                    }

                    ++i;
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        /*
            The hot path: a clean message returns the same reference, so a
            write that allocated nothing still allocates nothing (pinned by
            StandardOperationsAllocationTests).
         */
        private static bool NeedsSanitizing(string message)
        {
            int length = message.Length;
            for (int i = 0; i < length; ++i)
            {
                char c = message[i];
                if (c == '\r' || IsEscaped(c))
                {
                    return true;
                }

                if (IsSurrogatePairAt(message, i))
                {
                    if (IsReserved(CharUnicodeInfo.GetUnicodeCategory(message, i)))
                    {
                        return true;
                    }

                    ++i;
                }
            }

            return false;
        }

        private static bool IsSurrogatePairAt(string message, int index)
        {
            return char.IsHighSurrogate(message[index])
                && index + 1 < message.Length
                && char.IsLowSurrogate(message[index + 1]);
        }

        private static bool IsEscaped(char c)
        {
            /* Printable ASCII is the overwhelming majority of log text, so it takes the first branch. */
            if (' ' <= c && c <= '~')
            {
                return false;
            }

            /* Newline, tab, and carriage return are the line structure a log line carries. */
            if (c == '\n' || c == '\t' || c == '\r')
            {
                return false;
            }

            /* Cc: the C0 controls, DEL, and the C1 controls. */
            if (char.IsControl(c))
            {
                return true;
            }

            return IsReserved(CharUnicodeInfo.GetUnicodeCategory(c));
        }

        /*
            The categories that cannot render honestly, asked rather than
            written out: a hand-listed range is a denylist, and this has to
            fail closed against a character nobody listed.
         */
        private static bool IsReserved(UnicodeCategory category)
        {
            switch (category)
            {
                /* Cf: bidi overrides, zero-width joiners, and the rest. */
                case UnicodeCategory.Format:
                /* Zl and Zp: line and paragraph separators. */
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return true;
                default:
                    return false;
            }
        }

        /* A BMP code unit is exactly four hex digits. */
        private static void AppendEscape(StringBuilder builder, char c)
        {
            builder.Append("\\u");
            AppendHex(builder, c, 4);
        }

        /*
            A supplementary scalar needs \U and eight digits: padding to four
            without switching would write \uE0001, which reads as a different
            character than the one that was logged.
         */
        private static void AppendExtendedEscape(StringBuilder builder, string message, int index)
        {
            builder.Append("\\U");
            AppendHex(builder, char.ConvertToUtf32(message[index], message[index + 1]), 8);
        }

        private static void AppendHex(StringBuilder builder, int value, int digits)
        {
            for (int shift = (digits - 1) * 4; 0 <= shift; shift -= 4)
            {
                builder.Append(HexDigits[(value >> shift) & 0xF]);
            }
        }
    }
}
