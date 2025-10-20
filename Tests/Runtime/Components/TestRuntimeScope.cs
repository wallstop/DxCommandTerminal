namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
    using Backend;
    using UI;

    internal static class TestRuntimeScope
    {
        private static ITerminalRuntimeScope CurrentScope =>
            TerminalUI.ServiceLocator?.RuntimeScope;

        internal static ITerminalRuntime Runtime => CurrentScope?.ActiveRuntime;

        internal static CommandShell Shell => CurrentScope?.Shell;

        internal static CommandHistory History => CurrentScope?.History;

        internal static CommandLog Buffer => CurrentScope?.Buffer;

        internal static CommandAutoComplete AutoComplete => CurrentScope?.AutoComplete;

        internal static bool Log(TerminalLogType type, string format, params object[] arguments)
        {
            ITerminalRuntimeScope scope = CurrentScope;
            if (scope == null)
            {
                return false;
            }

            return scope.Log(type, format, arguments ?? System.Array.Empty<object>());
        }
    }
}
