namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    [CreateAssetMenu(
        menuName = "DxCommandTerminal/Theme Pack",
        fileName = nameof(TerminalThemePack),
        order = 1_111_123
    )]
    public class TerminalThemePack : ScriptableObject
    {
        public virtual IReadOnlyList<StyleSheet> Themes => _themes;

        [SerializeField]
        protected internal List<StyleSheet> _themes = new();
    }
}
