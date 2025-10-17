namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;

    public readonly struct TerminalRuntimeSettings
    {
        public TerminalRuntimeSettings(
            int logCapacity,
            int historyCapacity,
            IReadOnlyList<TerminalLogType> ignoredLogTypes,
            IReadOnlyList<string> disabledCommands,
            bool ignoreDefaultCommands
        )
        {
            LogCapacity = logCapacity;
            HistoryCapacity = historyCapacity;
            IgnoredLogTypes = ignoredLogTypes ?? System.Array.Empty<TerminalLogType>();
            DisabledCommands = disabledCommands ?? System.Array.Empty<string>();
            IgnoreDefaultCommands = ignoreDefaultCommands;
        }

        public int LogCapacity { get; }

        public int HistoryCapacity { get; }

        public IReadOnlyList<TerminalLogType> IgnoredLogTypes { get; }

        public IReadOnlyList<string> DisabledCommands { get; }

        public bool IgnoreDefaultCommands { get; }
    }
}
