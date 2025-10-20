namespace WallstopStudios.DxCommandTerminal.Backend
{
    internal sealed class TerminalRuntimeConfiguratorService : ITerminalRuntimeConfiguratorService
    {
        internal static ITerminalRuntimeConfiguratorService Default { get; } =
            new TerminalRuntimeConfiguratorService();

        private TerminalRuntimeConfiguratorService() { }

        public TerminalRuntimeModeFlags CurrentMode => TerminalRuntimeConfig.GetModeForTests();

        public void SetMode(TerminalRuntimeModeFlags mode)
        {
            TerminalRuntimeConfig.SetMode(mode);
        }

        public bool EditorAutoDiscover
        {
            get => TerminalRuntimeConfig.EditorAutoDiscover;
            set => TerminalRuntimeConfig.EditorAutoDiscover = value;
        }

        public bool ShouldEnableEditorFeatures()
        {
            return TerminalRuntimeConfig.ShouldEnableEditorFeatures();
        }

        public bool ShouldEnableDevelopmentFeatures()
        {
            return TerminalRuntimeConfig.ShouldEnableDevelopmentFeatures();
        }

        public bool ShouldEnableProductionFeatures()
        {
            return TerminalRuntimeConfig.ShouldEnableProductionFeatures();
        }

        public bool HasFlag(TerminalRuntimeModeFlags value, TerminalRuntimeModeFlags flag)
        {
            return TerminalRuntimeConfig.HasFlagNoAlloc(value, flag);
        }

        public int TryAutoDiscoverParsers()
        {
            return TerminalRuntimeConfig.TryAutoDiscoverParsers();
        }
    }
}
