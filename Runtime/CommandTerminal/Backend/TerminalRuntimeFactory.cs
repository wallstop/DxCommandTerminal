namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    /// Default implementation that adapts existing <see cref="TerminalRuntime"/> behaviour to the factory abstraction.
    /// </summary>
    public sealed class TerminalRuntimeFactory : ITerminalRuntimeFactory
    {
        public ITerminalRuntime CreateRuntime(ITerminalSettingsProvider settingsProvider)
        {
            TerminalRuntime runtime = new TerminalRuntime();
            if (settingsProvider != null)
            {
                TerminalRuntimeSettings settings = settingsProvider.BuildSettings();
                _ = runtime.Configure(settings, forceReset: true);
            }

            return runtime;
        }
    }
}
