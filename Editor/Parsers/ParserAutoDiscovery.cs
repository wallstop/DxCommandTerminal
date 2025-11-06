namespace WallstopStudios.DxCommandTerminal.Editor
{
#if UNITY_EDITOR
    using UnityEditor;
    using Backend;

    [InitializeOnLoad]
    internal static class ParserAutoDiscovery
    {
        static ParserAutoDiscovery()
        {
            // Editor convenience: allow auto-discovery via config flags
            if (
                TerminalRuntimeConfig.ShouldEnableEditorFeatures()
                && TerminalRuntimeConfig.EditorAutoDiscover
            )
            {
                CommandArg.DiscoverAndRegisterParsers(replaceExisting: false);
            }
        }
    }
#endif
}
