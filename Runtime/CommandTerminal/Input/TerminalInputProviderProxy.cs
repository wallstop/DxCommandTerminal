namespace WallstopStudios.DxCommandTerminal.Input
{
    using UI;

    internal sealed class TerminalInputProviderProxy : ITerminalInputProvider
    {
        internal static ITerminalInputProvider Default { get; } = new TerminalInputProviderProxy();

        private TerminalInputProviderProxy() { }

        public ITerminalInput GetInput(TerminalUI terminal)
        {
            return DefaultTerminalInput.Instance;
        }
    }
}
