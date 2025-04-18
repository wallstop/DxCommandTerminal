namespace WallstopStudios.DxCommandTerminal.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using DxCommandTerminal.Helper;
    using Extensions;
    using Themes;
    using UnityEditor;
    using UnityEngine;

    [CustomEditor(typeof(TerminalFontPack))]
    public sealed class TerminalFontPackEditor : Editor
    {
        [Flags]
        private enum FontType
        {
            None = 0,
            Normal = 1 << 0,
            Bold = 1 << 1,
            Italic = 1 << 2,
            BoldItalic = 1 << 3,
            ExtraBold = 1 << 4,
            ExtraBoldItalic = 1 << 5,
            ExtraLight = 1 << 6,
            ExtraLightItalic = 1 << 7,
            Light = 1 << 8,
            LightItalic = 1 << 9,
            Medium = 1 << 10,
            MediumItalic = 1 << 11,
            SemiBold = 1 << 12,
            SemiBoldItalic = 1 << 13,
            Thin = 1 << 14,
            ThinItalic = 1 << 15,
            Black = 1 << 16,
            BlackItalic = 1 << 17,
            Regular = 1 << 18,
            Variable = 1 << 19,
            Monospace = 1 << 20,
            Condensed = 1 << 21,
            CondensedBold = 1 << 22,
            CondensedExtraBold = 1 << 23,
            CondensedExtraLight = 1 << 24,
            CondensedLight = 1 << 25,
            CondensedMedium = 1 << 26,
            CondensedSemiBold = 1 << 27,
            CondensedThin = 1 << 28,
            VariableFont_wght = 1 << 29,
            VariableFont_width = 1 << 29,
        }

        private readonly HashSet<Font> _fontCache = new();
        private FontType _fontRemovalType = FontType.None;
        private FontType _fontAdditionType = FontType.None;
        private string _lastSelectedDirectory;
        private GUIStyle _impactButtonStyle;

        private void OnEnable()
        {
            _fontCache.Clear();
            _fontRemovalType = FontType.None;
        }

        public override void OnInspectorGUI()
        {
            _impactButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = Color.yellow },
                fontStyle = FontStyle.Bold,
            };

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

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Data Manipulation", EditorStyles.boldLabel);

            if (anyNullFont || _fontCache.Count != fontPack._fonts.Count)
            {
                if (GUILayout.Button("Fix Invalid Fonts", _impactButtonStyle))
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

            if (0 < fontPack._fonts.Count)
            {
                EditorGUILayout.BeginHorizontal();
                try
                {
                    _fontRemovalType = (FontType)EditorGUILayout.EnumFlagsField(_fontRemovalType);
                    if (GUILayout.Button("Remove Fonts Of Type", _impactButtonStyle))
                    {
                        foreach (FontType fontType in Enum.GetValues(typeof(FontType)))
                        {
                            int removed = fontPack._fonts.RemoveAll(font =>
                                Matches(font, fontType)
                            );
                            anyChanged |= removed != 0;
                        }
                    }
                }
                finally
                {
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.BeginHorizontal();
            try
            {
                _fontAdditionType = (FontType)EditorGUILayout.EnumFlagsField(_fontAdditionType);
                if (GUILayout.Button("Load From Current Directory"))
                {
                    string assetPath = AssetDatabase.GetAssetPath(fontPack);
                    UpdateFromDirectory(assetPath);
                }
                else if (GUILayout.Button("Load From Directory"))
                {
                    _lastSelectedDirectory = EditorUtility.OpenFolderPanel(
                        "Select Directory",
                        string.IsNullOrWhiteSpace(_lastSelectedDirectory)
                            ? Application.dataPath
                            : _lastSelectedDirectory,
                        string.Empty
                    );
                    UpdateFromDirectory(_lastSelectedDirectory);
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            if (
                anyChanged
                || (
                    !fontPack._fonts.IsSorted(UnityObjectNameComparer.Instance)
                    && GUILayout.Button("Sort Fonts")
                )
            )
            {
                fontPack._fonts.SortByName();
                EditorUtility.SetDirty(fontPack);
            }

            return;

            void UpdateFromDirectory(string directory)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                DirectoryInfo directoryInfo = new(directory);
                if (!directoryInfo.Exists)
                {
                    return;
                }

                directory = DirectoryHelper.AbsoluteToUnityRelativePath(directoryInfo.FullName);

                string[] fontGuids = AssetDatabase.FindAssets("t:Font", new[] { directory });
                foreach (string guid in fontGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                    if (string.IsNullOrWhiteSpace(assetPath))
                    {
                        continue;
                    }

                    Font font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
                    if (Matches(font, _fontAdditionType) && _fontCache.Add(font))
                    {
                        fontPack._fonts.Add(font);
                    }
                }
            }
        }

        private static bool Matches(Font font, FontType toCheck)
        {
            if (font == null)
            {
                return false;
            }

            if (toCheck == FontType.None)
            {
                return false;
            }

            foreach (FontType fontType in Enum.GetValues(typeof(FontType)))
            {
                if ((fontType & toCheck) == 0)
                {
                    continue;
                }

                if (font.name.EndsWith(fontType.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}
