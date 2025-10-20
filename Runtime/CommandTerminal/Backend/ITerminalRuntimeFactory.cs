namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    /// Creates <see cref="ITerminalRuntime"/> instances using provided configuration and services.
    /// </summary>
    public interface ITerminalRuntimeFactory
    {
        ITerminalRuntime CreateRuntime(ITerminalSettingsProvider settingsProvider);
    }
}
