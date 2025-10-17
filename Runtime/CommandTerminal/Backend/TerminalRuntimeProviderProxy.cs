namespace WallstopStudios.DxCommandTerminal.Backend
{
    public sealed class TerminalRuntimeProviderProxy : ITerminalRuntimeProvider
    {
        internal static ITerminalRuntimeProvider Default { get; } =
            new TerminalRuntimeProviderProxy();

        private TerminalRuntimeProviderProxy() { }

        public ITerminalRuntime ActiveRuntime => Terminal.ActiveRuntime;
    }
}
