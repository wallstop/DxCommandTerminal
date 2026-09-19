namespace WallstopStudios.DxCommandTerminal.Editor
{
    using System;
    using System.IO;
    using System.Text;
    using Themes;
    using UnityEditor;
    using UnityEngine;

    /*
        Auto-wires TerminalThemeAsset authoring (issue #72 option A, owner
        decision "auto wire"): every time a theme asset is created or edited,
        the sibling .uss file named after the asset is (re)written with the
        sheet the asset's colors describe. Writes are content-compared first,
        so unchanged imports never dirty the project or re-trigger imports.
        The generated sheet is a plain StyleSheet: drop it into a
        TerminalThemePack's Themes list and it behaves like any hand-written
        theme sheet.
     */
    internal sealed class TerminalThemeAssetPostProcessor : AssetPostprocessor
    {
        internal static void WriteSiblingSheet(TerminalThemeAsset themeAsset, string assetPath)
        {
            if (themeAsset == null || string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            string sheetPath = SheetPathFor(assetPath);
            string contents = themeAsset.BuildUss();

            if (File.Exists(sheetPath))
            {
                string existing = File.ReadAllText(sheetPath);
                if (string.Equals(existing, contents, StringComparison.Ordinal))
                {
                    return;
                }
            }

            File.WriteAllText(sheetPath, contents, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(sheetPath);
            Debug.Log($"Generated theme sheet '{sheetPath}' for '{themeAsset.name}'.", themeAsset);
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            foreach (string assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TerminalThemeAsset themeAsset = AssetDatabase.LoadAssetAtPath<TerminalThemeAsset>(
                    assetPath
                );
                if (themeAsset != null)
                {
                    WriteSiblingSheet(themeAsset, assetPath);
                }
            }

            for (int i = 0; i < movedAssets.Length; ++i)
            {
                if (!movedAssets[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TerminalThemeAsset themeAsset = AssetDatabase.LoadAssetAtPath<TerminalThemeAsset>(
                    movedAssets[i]
                );
                if (themeAsset == null)
                {
                    continue;
                }

                WriteSiblingSheet(themeAsset, movedAssets[i]);

                /*
                    A rename leaves the previously generated sheet behind under
                    the old asset name; remove it once the new-name sheet
                    exists so no stale theme class survives the rename.
                 */
                string oldSheetPath = SheetPathFor(movedFromAssetPaths[i]);
                string newSheetPath = SheetPathFor(movedAssets[i]);
                if (
                    !string.Equals(oldSheetPath, newSheetPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(oldSheetPath)
                )
                {
                    AssetDatabase.DeleteAsset(oldSheetPath);
                }
            }
        }

        private static string SheetPathFor(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath) ?? string.Empty;
            return Path.Combine(directory, Path.GetFileNameWithoutExtension(assetPath) + ".uss")
                .Replace('\\', '/');
        }
    }
}
