namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    /// Represents an isolated runtime backing a terminal instance. Provides access to the
    /// command buffer, history, shell, and autocomplete services without relying on static state.
    /// </summary>
    public interface ITerminalRuntime
    {
        CommandLog Log { get; }

        CommandHistory History { get; }

        CommandShell Shell { get; }

        CommandAutoComplete AutoComplete { get; }

        TerminalRuntimeUpdateResult Configure(in TerminalRuntimeSettings settings, bool forceReset);

        bool LogMessage(TerminalLogType type, string format, params object[] parameters);
    }
}
