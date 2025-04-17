namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System.Collections.Generic;
    using UnityEngine;

    [CreateAssetMenu(
        menuName = "DxCommandTerminal/Font Pack",
        fileName = nameof(TerminalFontPack),
        order = 1_111_123
    )]
    public class TerminalFontPack : ScriptableObject
    {
        public virtual IReadOnlyList<Font> Fonts => _fonts;

        [SerializeField]
        protected internal List<Font> _fonts = new();
    }
}
