namespace WallstopStudios.DxCommandTerminal.Editor
{
    using System;
    using System.Collections.Generic;
    using Themes;
    using UnityEditor;

    internal sealed class TerminalAssetPackPostProcessor : AssetPostprocessor
    {
        internal static readonly List<TerminalThemePack> NewThemePacks = new();
        internal static readonly List<TerminalFontPack> NewFontPacks = new();

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            foreach (string importedAsset in importedAssets)
            {
                if (!importedAsset.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TerminalThemePack themePack = AssetDatabase.LoadAssetAtPath<TerminalThemePack>(
                    importedAsset
                );
                if (themePack != null)
                {
                    NewThemePacks.Add(themePack);
                }

                TerminalFontPack fontPack = AssetDatabase.LoadAssetAtPath<TerminalFontPack>(
                    importedAsset
                );
                if (fontPack != null)
                {
                    NewFontPacks.Add(fontPack);
                }
            }
        }
    }
}
