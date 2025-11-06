namespace WallstopStudios.DxCommandTerminal.Backend.Profiles
{
    using Backend;

    internal sealed class RuntimeProfileSettingsProvider : ITerminalSettingsProvider
    {
        private readonly TerminalRuntimeProfile _profile;

        internal RuntimeProfileSettingsProvider(TerminalRuntimeProfile profile)
        {
            _profile = profile;
        }

        public TerminalRuntimeSettings BuildSettings()
        {
            return _profile != null
                ? _profile.BuildSettings()
                : new TerminalRuntimeSettings(
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
