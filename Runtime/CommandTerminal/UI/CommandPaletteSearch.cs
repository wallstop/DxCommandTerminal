namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;

    /*
        Pure search ranking for the quick-launch bar. Match tiers read in
        ascending preference order: exact, prefix, then fuzzy subsequence.
        Ties break by the caller's source order (ordinal command names).
     */
    internal static class CommandPaletteSearch
    {
        internal const int NoMatch = -1;
        internal const int ExactMatch = 0;
        internal const int PrefixMatch = 1;
        internal const int SubsequenceMatch = 2;

        internal static int Rank(string query, string candidate)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(candidate))
            {
                return NoMatch;
            }

            if (string.Equals(query, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return ExactMatch;
            }

            if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            {
                return PrefixMatch;
            }

            return IsSubsequence(query, candidate) ? SubsequenceMatch : NoMatch;
        }

        /*
            Fills <paramref name="results"/> with candidates matching
            <paramref name="query"/>, grouped by ascending rank tier. Within a
            tier the caller's source order is preserved, so an ordinal-sorted
            source yields ordinal ties. Caller-owned buffers are reused.
         */
        internal static void Filter(string query, List<string> source, List<string> results)
        {
            results.Clear();
            if (string.IsNullOrEmpty(query))
            {
                foreach (string candidate in source)
                {
                    results.Add(candidate);
                }

                return;
            }

            for (int tier = ExactMatch; tier <= SubsequenceMatch; ++tier)
            {
                foreach (string candidate in source)
                {
                    if (Rank(query, candidate) == tier)
                    {
                        results.Add(candidate);
                    }
                }
            }
        }

        private static bool IsSubsequence(string query, string candidate)
        {
            int searchIndex = 0;
            for (int queryIndex = 0; queryIndex < query.Length; ++queryIndex)
            {
                char queryChar = query[queryIndex];
                bool found = false;
                while (searchIndex < candidate.Length)
                {
                    if (CharsEqualIgnoreCase(queryChar, candidate[searchIndex]))
                    {
                        ++searchIndex;
                        found = true;
                        break;
                    }

                    ++searchIndex;
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CharsEqualIgnoreCase(char left, char right)
        {
            return left == right || char.ToLowerInvariant(left) == char.ToLowerInvariant(right);
        }
    }
}
