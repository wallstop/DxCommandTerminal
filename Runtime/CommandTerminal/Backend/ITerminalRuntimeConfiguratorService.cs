namespace WallstopStudios.DxCommandTerminal.Backend
{
    public interface ITerminalRuntimeConfiguratorService
    {
        TerminalRuntimeModeFlags CurrentMode { get; }

        void SetMode(TerminalRuntimeModeFlags mode);

        bool EditorAutoDiscover { get; set; }

        bool ShouldEnableEditorFeatures();

        bool ShouldEnableDevelopmentFeatures();

        bool ShouldEnableProductionFeatures();

        bool HasFlag(TerminalRuntimeModeFlags value, TerminalRuntimeModeFlags flag);

        int TryAutoDiscoverParsers();
    }
}
