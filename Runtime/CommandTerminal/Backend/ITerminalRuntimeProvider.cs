namespace WallstopStudios.DxCommandTerminal.Backend
{
    public interface ITerminalRuntimeProvider
    {
        ITerminalRuntime ActiveRuntime { get; }
    }
}
