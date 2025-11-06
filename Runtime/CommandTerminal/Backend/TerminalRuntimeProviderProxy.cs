namespace WallstopStudios.DxCommandTerminal.Backend
{
    using UI;

    public sealed class TerminalRuntimeProviderProxy : ITerminalRuntimeProvider
    {
        internal static ITerminalRuntimeProvider Default { get; } =
            new TerminalRuntimeProviderProxy();

        private TerminalRuntimeProviderProxy() { }

        public ITerminalRuntime ActiveRuntime =>
            TerminalUI.ServiceLocator?.RuntimeScope?.ActiveRuntime;
    }
}
