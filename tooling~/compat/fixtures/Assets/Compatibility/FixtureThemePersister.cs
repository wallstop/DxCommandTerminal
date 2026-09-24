#if UNITY_EDITOR
namespace DxCommandTerminal.T13.Compatibility.Tests
{
    using System;
    using System.IO;
    using WallstopStudios.DxCommandTerminal.Persistence;

    public sealed class FixtureThemePersister : TerminalThemePersister
    {
        protected override string ThemeFile =>
            Environment.GetEnvironmentVariable("DX_T13_PERSISTENCE_FILE")
            ?? Path.Combine(Path.GetTempPath(), "DxCommandTerminal", "TerminalTheme.json");
    }
}
#endif
