namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    [CreateAssetMenu(
        menuName = "Wallstop Studios/DxCommandTerminal/Theme Pack",
        fileName = nameof(TerminalThemePack),
        order = 1_111_123
    )]
    public class TerminalThemePack : ScriptableObject
    {
        public virtual IReadOnlyList<StyleSheet> Themes => _themes;
        public virtual IReadOnlyList<string> ThemeNames => _themeNames;

        [SerializeField]
        protected internal List<StyleSheet> _themes = new();

        [SerializeField]
        protected internal List<string> _themeNames = new();
    }
}
