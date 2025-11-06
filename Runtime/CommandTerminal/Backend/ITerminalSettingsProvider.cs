namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    /// Provides runtime settings for a terminal instance. Allows DI-friendly configuration and testing.
    /// </summary>
    public interface ITerminalSettingsProvider
    {
        TerminalRuntimeSettings BuildSettings();
    }
}
