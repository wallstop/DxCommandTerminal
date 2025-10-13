namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
    using Themes;
    using UnityEngine;
    using UnityEngine.UIElements;

    // Test-only pack implementations to populate protected internal lists
    public sealed class TestThemePack : TerminalThemePack
    {
        public void Add(StyleSheet sheet, string name)
        {
            _themes.Add(sheet);
            _themeNames.Add(name);
        }
    }

    public sealed class TestFontPack : TerminalFontPack
    {
        public void Add(Font f)
        {
            _fonts.Add(f);
        }
    }
}
