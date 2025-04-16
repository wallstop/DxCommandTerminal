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
    }
}
