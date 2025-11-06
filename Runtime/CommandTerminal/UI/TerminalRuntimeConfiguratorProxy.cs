namespace WallstopStudios.DxCommandTerminal.UI
{
    using Backend;

    internal sealed class TerminalRuntimeConfiguratorProxy : ITerminalRuntimeConfigurator
    {
        internal static ITerminalRuntimeConfigurator Default { get; } =
            new TerminalRuntimeConfiguratorProxy();

        private TerminalRuntimeConfiguratorProxy() { }

        public void SetMode(TerminalRuntimeModeFlags modes)
        {
            TerminalRuntimeConfig.SetMode(modes);
        }

        public bool EditorAutoDiscover
        {
            get => TerminalRuntimeConfig.EditorAutoDiscover;
            set => TerminalRuntimeConfig.EditorAutoDiscover = value;
        }

        public int TryAutoDiscoverParsers()
        {
            return TerminalRuntimeConfig.TryAutoDiscoverParsers();
        }
    }
}
