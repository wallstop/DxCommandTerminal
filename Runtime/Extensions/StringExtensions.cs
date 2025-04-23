namespace WallstopStudios.DxCommandTerminal.Extensions
{
    internal static class StringExtensions
    {
        internal static bool NeedsLowerInvariantConversion(this string input)
        {
            foreach (char inputCharacter in input)
            {
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
