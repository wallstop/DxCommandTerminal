namespace WallstopStudios.DxCommandTerminal.Input
{
    public sealed class DefaultTerminalInput : ITerminalInput
    {
        public static readonly DefaultTerminalInput Instance = new();

        public string CommandText { get; set; } = string.Empty;

        private DefaultTerminalInput() { }
    }
}
