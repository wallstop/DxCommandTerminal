namespace WallstopStudios.DxCommandTerminal.UI
{
    using Backend;

    public interface ITerminalRuntimeConfigurator
    {
        void SetMode(TerminalRuntimeModeFlags modes);

        bool EditorAutoDiscover { get; set; }

        int TryAutoDiscoverParsers();
    }
}
