namespace WallstopStudios.DxCommandTerminal.Input
{
    internal interface ITerminalInputSource
    {
        InputMode Mode { get; }

        bool IsKeyPressed(string binding);
    }
}
