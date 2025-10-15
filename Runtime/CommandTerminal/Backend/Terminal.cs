namespace WallstopStudios.DxCommandTerminal.Backend
{
    using JetBrains.Annotations;

    public static class Terminal
    {
        private static ITerminalRuntime _activeRuntime;

        public static CommandLog Buffer => _activeRuntime?.Log;

        public static CommandShell Shell => _activeRuntime?.Shell;

        public static CommandHistory History => _activeRuntime?.History;

        public static CommandAutoComplete AutoComplete => _activeRuntime?.AutoComplete;

        internal static ITerminalRuntime ActiveRuntime => _activeRuntime;

        internal static void RegisterRuntime(ITerminalRuntime runtime)
        {
            _activeRuntime = runtime;
        }

        internal static void UnregisterRuntime(ITerminalRuntime runtime)
        {
            if (_activeRuntime == runtime)
            {
                _activeRuntime = null;
            }
        }

        [StringFormatMethod("format")]
        public static bool Log(string format, params object[] parameters)
        {
            return Log(TerminalLogType.ShellMessage, format, parameters);
        }

        [StringFormatMethod("format")]
        public static bool Log(TerminalLogType type, string format, params object[] parameters)
        {
            ITerminalRuntime runtime = _activeRuntime;
            if (runtime == null)
            {
                return false;
            }

            return runtime.LogMessage(type, format, parameters);
        }
    }
}
