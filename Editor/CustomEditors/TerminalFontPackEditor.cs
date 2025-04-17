namespace WallstopStudios.DxCommandTerminal.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Themes;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Windows;

    [CustomEditor(typeof(TerminalFontPack))]
    public sealed class TerminalFontPackEditor : Editor
    {
        private enum FontType
        {
            Normal = 0,
            Bold = 1,
            Italic = 2,
            BoldItalic = 3,
        }

        private readonly HashSet<Font> _fontCache = new();

        public override void OnInspectorGUI()
        {
            TerminalFontPack fontPack = target as TerminalFontPack;
            base.OnInspectorGUI();

            if (fontPack == null)
            {
                return;
            }

            bool anyChanged = false;
            if (fontPack._fonts == null)
            {
                anyChanged = true;
                fontPack._fonts = new List<Font>();
            }

            _fontCache.Clear();
            bool anyNullFont = false;
            foreach (Font font in fontPack._fonts)
            {
                if (font == null)
                {
                    anyNullFont = true;
                }
                _fontCache.Add(font);
            }

            if (anyNullFont || _fontCache.Count != fontPack._fonts.Count)
            {
                if (GUILayout.Button("Fix Invalid Fonts"))
                {
                    _fontCache.Clear();
                    for (int i = fontPack._fonts.Count - 1; 0 <= i; --i)
                    {
                        Font font = fontPack._fonts[i];
                        if (font != null && _fontCache.Add(font))
                        {
                            continue;
                        }

                        anyChanged = true;
                        fontPack._fonts.RemoveAt(i);
                    }
                }
            }

            if (GUILayout.Button("Load From Current Directory"))
            {
                string assetPath = AssetDatabase.GetAssetPath(fontPack);
                UpdateFromDirectory(assetPath);
            }
            else if (GUILayout.Button("Load From Directory"))
            {
                string directory = EditorUtility.OpenFolderPanel(
                    "Select Directory",
                    Application.dataPath,
                    string.Empty
                );
                UpdateFromDirectory(directory);
            }

            if (anyChanged)
            {
                fontPack._fonts.Sort(
                    (lhs, rhs) =>
                        string.Compare(lhs.name, rhs.name, StringComparison.OrdinalIgnoreCase)
                );
                EditorUtility.SetDirty(fontPack);
            }

            return;

            void UpdateFromDirectory(string directory)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                string directoryPath = System.IO.Path.GetDirectoryName(directory);
                if (string.IsNullOrWhiteSpace(directoryPath))
                {
                    return;
                }

                string[] fontPaths = System.IO.Directory.GetFiles(
                    directoryPath,
                    searchPattern: "*",
                    searchOption: System.IO.SearchOption.AllDirectories
                );
                foreach (string fontPath in fontPaths)
                {
                    if (fontPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    Font font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                    if (font == null)
                    {
                        continue;
                    }

                    if (_fontCache.Add(font))
                    {
                        anyChanged = true;
                        fontPack._fonts.Add(font);
                    }
                }
            }
        }
    }
#endif
}
