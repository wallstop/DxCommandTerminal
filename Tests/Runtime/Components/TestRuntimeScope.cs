namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
    using System.Collections.Generic;
    using Backend;
    using UI;
    using UnityEngine.UIElements;

    internal static class TestRuntimeScope
    {
        private static ITerminalRuntimeScope CurrentScope =>
            TerminalUI.ServiceLocator?.RuntimeScope;

        internal static ITerminalRuntime Runtime => CurrentScope?.ActiveRuntime;

        internal static CommandShell Shell => CurrentScope?.Shell;

        internal static CommandHistory History => CurrentScope?.History;

        internal static CommandLog Buffer => CurrentScope?.Buffer;

        internal static CommandAutoComplete AutoComplete => CurrentScope?.AutoComplete;

        private static TerminalUI ActiveTerminal => TerminalUI.Instance;

        private static TerminalUI RequireTerminal()
        {
            TerminalUI terminal = ActiveTerminal;
            if (terminal == null)
            {
                throw new System.InvalidOperationException("TerminalUI instance is not available.");
            }

            return terminal;
        }

        internal static ScrollView LogScrollViewForTests => RequireTerminal().LogScrollViewForTests;

        internal static IList<LogItem> LogItemsForTests => RequireTerminal().LogItemsForTests;

        internal static ScrollView AutoCompleteContainerForTests =>
            RequireTerminal().AutoCompleteContainerForTests;

        internal static TerminalHistoryFadeTargets HistoryFadeTargetsForTests =>
            RequireTerminal().HistoryFadeTargetsForTests;

        internal static bool LogUnityMessagesForTests => RequireTerminal().LogUnityMessagesForTests;

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
