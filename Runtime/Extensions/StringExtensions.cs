namespace WallstopStudios.DxCommandTerminal.Extensions
{
    public static class StringExtensions
    {
        public static bool NeedsLowerInvariantConversion(this string input)
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

        public static bool NeedsTrim(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return false;
            }

            return char.IsWhiteSpace(input[0]) || char.IsWhiteSpace(input[^1]);
        }
    }
}
