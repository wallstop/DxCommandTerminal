namespace WallstopStudios.DxCommandTerminal.Editor
{
#if UNITY_EDITOR
    using UnityEditor;
    using WallstopStudios.DxCommandTerminal.Backend;

    [InitializeOnLoad]
    internal static class ParserAutoDiscovery
    {
        static ParserAutoDiscovery()
        {
            // Editor convenience: allow auto-discovery via config flags
            if (
                Backend.TerminalRuntimeConfig.ShouldEnableEditorFeatures()
                && Backend.TerminalRuntimeConfig.EditorAutoDiscover
            )
            {
                CommandArg.DiscoverAndRegisterParsers(replaceExisting: false);
            }
        }
    }
#endif
}
