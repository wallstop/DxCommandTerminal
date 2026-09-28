namespace WallstopStudios.DxCommandTerminal.Extensions
{
    internal static class StringExtensions
    {
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
    }
}
