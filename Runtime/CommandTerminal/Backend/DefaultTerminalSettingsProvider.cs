namespace WallstopStudios.DxCommandTerminal.Backend
{
    internal sealed class DefaultTerminalSettingsProvider : ITerminalSettingsProvider
    {
        public TerminalRuntimeSettings BuildSettings()
        {
            return new TerminalRuntimeSettings(
                logCapacity: 0,
                historyCapacity: 0,
                blockedLogTypes: System.Array.Empty<TerminalLogType>(),
                allowedLogTypes: System.Array.Empty<TerminalLogType>(),
                blockedCommands: System.Array.Empty<string>(),
                allowedCommands: System.Array.Empty<string>(),
                includeDefaultCommands: true
            );
        }
    }
}
