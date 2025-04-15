namespace CommandTerminal.Editor.CustomEditors
{
    using System;
    using System.Collections.Generic;
    using CommandTerminal.Helper;
    using UnityEditor;
    using UnityEngine;

#if UNITY_EDITOR
    public static class CommandTerminalFontLoader
    {
        private const string FontPath = "Fonts";

        public static Font[] LoadFonts()
        {
            string fontDirectoryRelativePath = DirectoryHelper.FindAbsolutePathToDirectory(
                FontPath
            );

            if (string.IsNullOrWhiteSpace(fontDirectoryRelativePath))
            {
                return Array.Empty<Font>();
            }

            string[] searchFolders = { fontDirectoryRelativePath };
            List<Font> foundFonts = new();

            string[] fontGuids = AssetDatabase.FindAssets("t:Font", searchFolders);

            foreach (string guid in fontGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (
                    !string.IsNullOrWhiteSpace(assetPath)
                    && assetPath.StartsWith(
                        fontDirectoryRelativePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && (
                        assetPath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                        || assetPath.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    Font fontAsset = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
                    if (fontAsset != null)
                    {
                        foundFonts.Add(fontAsset);
                    }
                }
            }

            return foundFonts.ToArray();
        }
    }
#endif
}
