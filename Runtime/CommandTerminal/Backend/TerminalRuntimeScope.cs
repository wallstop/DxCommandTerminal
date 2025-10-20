namespace WallstopStudios.DxCommandTerminal.Backend
{
    internal sealed class TerminalRuntimeScope : ITerminalRuntimeScope
    {
        internal static ITerminalRuntimeScope Default { get; } = new TerminalRuntimeScope();

        private TerminalRuntimeScope() { }

        private ITerminalRuntime _activeRuntime;

        public ITerminalRuntime ActiveRuntime => _activeRuntime;

        public CommandLog Buffer => _activeRuntime?.Log;

        public CommandShell Shell => _activeRuntime?.Shell;

        public CommandHistory History => _activeRuntime?.History;

        public CommandAutoComplete AutoComplete => _activeRuntime?.AutoComplete;

        public void RegisterRuntime(ITerminalRuntime runtime)
        {
            _activeRuntime = runtime;
            Terminal.RegisterRuntime(runtime);
        }

        public void UnregisterRuntime(ITerminalRuntime runtime)
        {
            if (_activeRuntime == runtime)
            {
                _activeRuntime = null;
            }

            Terminal.UnregisterRuntime(runtime);
        }

        public bool Log(TerminalLogType type, string format, params object[] parameters)
        {
            ITerminalRuntime runtime = _activeRuntime;
            if (runtime == null)
            {
                return false;
            }

            return runtime.LogMessage(type, format, parameters);
        }

        public bool Log(string format, params object[] parameters)
        {
            return Log(TerminalLogType.ShellMessage, format, parameters);
        }
    }
}
