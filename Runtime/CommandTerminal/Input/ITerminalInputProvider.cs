namespace WallstopStudios.DxCommandTerminal.Input
{
    using UI;

    public interface ITerminalInputProvider
    {
        ITerminalInput GetInput(TerminalUI terminal);
    }
}
