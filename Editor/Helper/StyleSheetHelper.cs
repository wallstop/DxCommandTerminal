namespace CommandTerminal.Editor.Helper
{
#if UNITY_EDITOR
    using System;
    using UnityEngine;
    using UnityEngine.UIElements;
    using UnityEditor;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Linq;

    public static class StyleSheetHelper
    {
        public static string[] GetAvailableThemes(UIDocument uiDocument)
        {
            if (uiDocument == null)
            {
                return Array.Empty<string>();
            }

            return uiDocument.panelSettings == null
                ? Array.Empty<string>()
                : GetAvailableThemes(uiDocument.panelSettings.themeStyleSheet);
        }

        public static string[] GetAvailableThemes(ThemeStyleSheet themeSettingsAsset)
        {
            if (themeSettingsAsset == null)
            {
                return Array.Empty<string>();
            }

            using SerializedObject serializedThemeSettings = new(themeSettingsAsset);
            SerializedProperty themesListProperty = serializedThemeSettings.FindProperty(
                "m_FlattenedImportedStyleSheets"
            );

            if (themesListProperty == null)
            {
                return Array.Empty<string>();
            }

            try
            {
                if (!themesListProperty.isArray)
                {
                    return Array.Empty<string>();
                }
                if (themesListProperty.arraySize == 0)
                {
                    return Array.Empty<string>();
                }

                SortedSet<string> availableThemes = new(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < themesListProperty.arraySize; ++i)
                {
                    SerializedProperty themeProperty = themesListProperty.GetArrayElementAtIndex(i);
                    if (themeProperty == null)
                    {
                        continue;
                    }

                    try
                    {
                        StyleSheet styleSheet = themeProperty.objectReferenceValue as StyleSheet;
                        if (styleSheet == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(styleSheet.name))
                        {
                            continue;
                        }

                        if (!styleSheet.name.Contains("Theme", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        availableThemes.UnionWith(GetAvailableThemes(styleSheet));
                    }
                    finally
                    {
                        themeProperty.Dispose();
                    }
                }

                return availableThemes.ToArray();
            }
            finally
            {
                themesListProperty.Dispose();
            }
        }

        public static string[] GetAvailableThemes(StyleSheet styleSheetAsset)
        {
            if (styleSheetAsset == null)
            {
                return Array.Empty<string>();
            }

            string assetPath = AssetDatabase.GetAssetPath(styleSheetAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return Array.Empty<string>();
            }

            if (!assetPath.EndsWith(".uss", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<string>();
            }

            try
            {
                string ussContent = File.ReadAllText(assetPath);

                // Remove block comments /* ... */
                ussContent = Regex.Replace(
                    ussContent,
                    @"/\*.*?\*/",
                    string.Empty,
                    RegexOptions.Singleline
                );

                // Remove line comments // ...
                ussContent = Regex.Replace(
                    ussContent,
                    "//.*$",
                    string.Empty,
                    RegexOptions.Multiline
                );

                SortedSet<string> selectors = new(StringComparer.OrdinalIgnoreCase);

                int lastIndex = 0;
                while (lastIndex < ussContent.Length)
                {
                    int braceIndex = ussContent.IndexOf('{', lastIndex);
                    if (braceIndex < 0)
                    {
                        break;
                    }

                    int previousRuleEnd = ussContent.LastIndexOf(
                        '}',
                        braceIndex - 1,
                        braceIndex - lastIndex
                    );
                    int selectorStartIndex = previousRuleEnd < 0 ? lastIndex : previousRuleEnd + 1;

                    string selectorPart = ussContent
                        .Substring(selectorStartIndex, braceIndex - selectorStartIndex)
                        .Trim();

                    if (!string.IsNullOrWhiteSpace(selectorPart))
                    {
                        string[] individualSelectors = selectorPart.Split(',');
                        foreach (string sel in individualSelectors)
                        {
                            string trimmedSelector = sel.Trim();
                            if (trimmedSelector.StartsWith('.'))
                            {
                                trimmedSelector = trimmedSelector[1..];
                            }

                            if (string.IsNullOrWhiteSpace(trimmedSelector))
                            {
                                continue;
                            }

                            if (
                                trimmedSelector.Contains(
                                    "theme",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                selectors.Add(trimmedSelector);
                            }
                        }
                    }

                    int nextBraceIndex = ussContent.IndexOf('}', braceIndex + 1);
                    lastIndex = nextBraceIndex < 0 ? ussContent.Length : nextBraceIndex + 1;
                }

                return selectors.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Array.Empty<string>();
            }
        }
    }
#endif
}
