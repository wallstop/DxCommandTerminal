namespace WallstopStudios.DxCommandTerminal.Backend
{
    public interface ITerminalRuntimeScope
    {
        ITerminalRuntime ActiveRuntime { get; }

        CommandLog Buffer { get; }

        CommandShell Shell { get; }

        CommandHistory History { get; }

        CommandAutoComplete AutoComplete { get; }

        void RegisterRuntime(ITerminalRuntime runtime);

        void UnregisterRuntime(ITerminalRuntime runtime);

        bool Log(TerminalLogType type, string format, params object[] parameters);

        bool Log(string format, params object[] parameters);
    }
}
