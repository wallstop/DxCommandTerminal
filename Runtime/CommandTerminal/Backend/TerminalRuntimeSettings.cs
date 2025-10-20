namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;

    public readonly struct TerminalRuntimeSettings
    {
        public TerminalRuntimeSettings(
            int logCapacity,
            int historyCapacity,
            IReadOnlyList<TerminalLogType> blockedLogTypes,
            IReadOnlyList<TerminalLogType> allowedLogTypes,
            IReadOnlyList<string> blockedCommands,
            IReadOnlyList<string> allowedCommands,
            bool includeDefaultCommands
        )
        {
            LogCapacity = logCapacity;
            HistoryCapacity = historyCapacity;
            BlockedLogTypes = blockedLogTypes ?? System.Array.Empty<TerminalLogType>();
            AllowedLogTypes = allowedLogTypes ?? System.Array.Empty<TerminalLogType>();
            BlockedCommands = blockedCommands ?? System.Array.Empty<string>();
            AllowedCommands = allowedCommands ?? System.Array.Empty<string>();
            IncludeDefaultCommands = includeDefaultCommands;
        }

        public int LogCapacity { get; }

        public int HistoryCapacity { get; }

        public IReadOnlyList<TerminalLogType> BlockedLogTypes { get; }

        public IReadOnlyList<TerminalLogType> AllowedLogTypes { get; }

        public IReadOnlyList<string> BlockedCommands { get; }

        public IReadOnlyList<string> AllowedCommands { get; }

        public bool IncludeDefaultCommands { get; }

        public bool IgnoreDefaultCommands => !IncludeDefaultCommands;
    }
}
