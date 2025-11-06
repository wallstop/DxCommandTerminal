namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    public sealed class BoolArgParser : ArgParser<bool>
    {
        public static readonly BoolArgParser Instance = new();

        protected override bool TryParseTyped(string input, out bool value)
        {
            return bool.TryParse(input, out value);
        }
    }
}
