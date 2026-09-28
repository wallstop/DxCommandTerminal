namespace WallstopStudios.DxCommandTerminal.Extensions
{
    using System.Globalization;

    internal static class StringExtensions
    {
        /* Nothing below this code unit continues the character in front of it,
           so it is the fast path for the ASCII and Latin-1 command names a
           caret is usually inside. */
        private const char FirstContinuing = '\u00ad';

        /* A joiner binds the character it follows to the one it precedes:
           U+200D is the one emoji sequences are built from, U+200C is its
           non-joining twin. */
        private const char ZeroWidthJoiner = '\u200d';

        private const char ZeroWidthNonJoiner = '\u200c';

        /* A tag character is an invisible letter of a subdivision flag (Wales
           and its neighbors), which carries no glyph of its own. The range is
           the surrogate pair DB80 DC20-D C7F. */
        private const char TagHigh = '\udb80';

        private const char TagLowFirst = '\udc20';

        private const char TagLowLast = '\udc7f';

        /* A skin-tone modifier is one of five emoji, U+1F3FB through U+1F3FF,
           stored as the surrogate pair D87C DFFB-D FFF. */
        private const char ModifierHigh = '\ud87c';

        private const char ModifierLowFirst = '\udffb';

        /* A regional indicator is one of the 26 flag letters, U+1F1E6 through
           U+1F1FF, stored as the surrogate pair D87C DDE6-D DFA. The two stop
           where the other starts, because a pair is one of them or the other
           and never both. */
        private const char IndicatorHigh = '\ud87c';

        private const char IndicatorLowFirst = '\udde6';

        private const char IndicatorLowLast = '\udffa';

        /* Every code point above U+FFFF ends in one of these. */
        private const char LastLowSurrogate = '\udfff';

        /// <summary>
        ///     Moves an index back to the nearest position that is not inside a
        ///     character, clamped to the string.
        /// </summary>
        /// <remarks>
        ///     A UTF-16 code unit is the unit every caret index in this package
        ///     is counted in, and it is the wrong one: an accented letter, an
        ///     emoji, and a joined emoji sequence are one character to the
        ///     person reading and two or more code units to the string. A caret
        ///     between those units is inside a character, and a caret the package
        ///     reads there slices a token in half - a completion provider is
        ///     handed half a character and answers with a candidate the
        ///     developer never asked for - while one it writes there leaves an
        ///     unpaired surrogate in the value, which is not text at all.
        ///     <para>
        ///         The field is engine-owned, so whether Unity hands such a caret
        ///         back is engine behavior; the package does not depend on the
        ///         answer. Every caret it reads or writes is snapped here, so a
        ///         token is always whole characters and a replacement never lands
        ///         inside one.
        ///     </para>
        ///     <para>
        ///         The result floors: the character stays whole and the caret
        ///         lands in front of it, the nearest legal position to the one
        ///         asked for.
        ///     </para>
        /// </remarks>
        internal static int SnapToTextBoundary(this string input, int index)
        {
            if (input == null || index <= 0)
            {
                return 0;
            }

            int length = input.Length;
            if (length <= index)
            {
                return length;
            }

            while (0 < index && ContinuesCharacter(input, index))
            {
                --index;
            }

            return index;
        }

        internal static bool NeedsLowerInvariantConversion(this string input)
        {
            /*
                Counting, not foreach: a string's enumerator is a class, so
                foreach allocates one per call. This runs over every command
                name when the completion list rebuilds, and a rebuild of the
                1,000-command tier is a measured gate (rule 11).
             */
            int length = input.Length;
            for (int i = 0; i < length; ++i)
            {
                char inputCharacter = input[i];
                if (char.ToLowerInvariant(inputCharacter) != inputCharacter)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool NeedsTrim(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            return char.IsWhiteSpace(input[0]) || char.IsWhiteSpace(input[^1]);
        }

        /* A code unit continues the character in front of it when it is the
           second half of a surrogate pair, when it is a mark (an accent, a
           keycap, a variation selector), when it is a joiner, a tag
           character, or a skin-tone modifier, or when it follows a zero-width
           joiner. A flag letter continues one only when an odd number of them
           precede it, which is what pairs a flag's two letters.

           Following a joiner counts whether or not the next character is an
           emoji, because telling the two apart needs the Unicode emoji table.
           That floors a caret further left than it has to be in the rare case
           of a joiner between two letters, and never inside a character, which
           is the property that matters. */
        private static bool ContinuesCharacter(string input, int index)
        {
            char c = input[index];
            if (char.IsLowSurrogate(c))
            {
                return true;
            }

            if (0 < index && input[index - 1] == ZeroWidthJoiner)
            {
                return true;
            }

            if (c < FirstContinuing)
            {
                return false;
            }

            if (
                c == ZeroWidthJoiner
                || c == ZeroWidthNonJoiner
                || StartsPair(input, index, TagHigh, TagLowFirst, TagLowLast)
            )
            {
                return true;
            }

            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.SpacingCombiningMark:
                case UnicodeCategory.EnclosingMark:
                    return true;
            }

            if (StartsPair(input, index, IndicatorHigh, IndicatorLowFirst, IndicatorLowLast))
            {
                return StartsUnpairedFlag(input, index);
            }

            return StartsPair(input, index, ModifierHigh, ModifierLowFirst, LastLowSurrogate);
        }

        /* Every code point above U+FFFF is stored as a high surrogate followed
           by a low one, and the ones that continue a character are told apart
           by the pair they form. */
        private static bool StartsPair(
            string input,
            int index,
            char high,
            char lowFirst,
            char lowLast
        )
        {
            return input[index] == high
                && index + 1 < input.Length
                && lowFirst <= input[index + 1]
                && input[index + 1] <= lowLast;
        }

        /* A flag is two flag letters, so a run of three is a flag and a letter
           and the third starts a character. Counting the run behind the index
           two units at a time is bounded by the length of that run. */
        private static bool StartsUnpairedFlag(string input, int index)
        {
            int preceding = 0;
            for (int i = index - 2; 0 <= i; i -= 2)
            {
                if (!StartsPair(input, i, IndicatorHigh, IndicatorLowFirst, IndicatorLowLast))
                {
                    break;
                }

                ++preceding;
            }

            return (preceding & 1) != 0;
        }
    }
}
