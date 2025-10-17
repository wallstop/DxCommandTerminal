namespace WallstopStudios.DxCommandTerminal.Backend
{
    public readonly struct TerminalRuntimeUpdateResult
    {
        public TerminalRuntimeUpdateResult(
            bool logRecreated,
            bool historyRecreated,
            bool shellRecreated,
            bool autoCompleteRecreated,
            bool commandsRefreshed
        )
        {
            LogRecreated = logRecreated;
            HistoryRecreated = historyRecreated;
            ShellRecreated = shellRecreated;
            AutoCompleteRecreated = autoCompleteRecreated;
            CommandsRefreshed = commandsRefreshed;
        }

        public bool LogRecreated { get; }

        public bool HistoryRecreated { get; }

        public bool ShellRecreated { get; }

        public bool AutoCompleteRecreated { get; }

        public bool CommandsRefreshed { get; }

        public bool RuntimeReset =>
            LogRecreated || HistoryRecreated || ShellRecreated || AutoCompleteRecreated;
    }
}
