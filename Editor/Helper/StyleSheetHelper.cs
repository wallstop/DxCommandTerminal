namespace WallstopStudios.DxCommandTerminal.Editor.Helper
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public static class StyleSheetHelper
    {
        private static readonly List<string> RequiredVariables = new()
        {
            "--terminal-bg",
            "--button-bg",
            "--input-field-bg",
            "--button-selected-bg",
            "--button-hover-bg",
            "--scroll-bg",
            "--scroll-inverse-bg",
            "--scroll-active-bg",
            "--button-text",
            "--button-selected-text",
            "--button-hover-text",
            "--input-text-color",
            "--text-message",
            "--text-warning",
            "--text-input-echo",
            "--text-shell",
            "--text-error",
            "--scroll-color",
            "--caret-color",
        };

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

        public static void AddStyleSheets(
            UIDocument uiDocument,
            IEnumerable<StyleSheet> styleSheets
        )
        {
            if (uiDocument == null)
            {
                return;
            }
            AddStyleSheets(uiDocument.panelSettings, styleSheets);
        }

        public static void AddStyleSheets(
            PanelSettings panelSettings,
            IEnumerable<StyleSheet> styleSheets
        )
        {
            if (panelSettings == null)
            {
                return;
            }
            AddStyleSheets(panelSettings.themeStyleSheet, styleSheets);
        }

        public static void AddStyleSheets(
            ThemeStyleSheet themeSettingsAsset,
            IEnumerable<StyleSheet> styleSheets
        )
        {
            List<StyleSheet> uniqueStyleSheets = (styleSheets ?? Enumerable.Empty<StyleSheet>())
                .Distinct()
                .ToList();
            using SerializedObject serializedThemeSettings = new(themeSettingsAsset);
            SerializedProperty themesListProperty = serializedThemeSettings.FindProperty("imports");

            if (themesListProperty == null)
            {
                return;
            }

            try
            {
                if (!themesListProperty.isArray)
                {
                    return;
                }

                foreach (StyleSheet styleSheet in uniqueStyleSheets)
                {
                    themesListProperty.InsertArrayElementAtIndex(themesListProperty.arraySize);
                }
                int beginningIndex = themesListProperty.arraySize - uniqueStyleSheets.Count;
                IList imports =
                    typeof(ThemeStyleSheet)
                        .GetField(
                            "imports",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                        )
                        ?.GetValue(themeSettingsAsset) as IList;
                if (imports == null)
                {
                    return;
                }
                Type styleSheetType = typeof(StyleSheet);
                Type importStructType = styleSheetType.GetNestedType(
                    "ImportStruct",
                    BindingFlags.Instance
                        | BindingFlags.Static
                        | BindingFlags.Public
                        | BindingFlags.NonPublic
                );
                for (int i = 0; i < uniqueStyleSheets.Count; ++i)
                {
                    StyleSheet styleSheet = uniqueStyleSheets[i];
                    object importStruct = Activator.CreateInstance(importStructType);
                    importStruct
                        .GetType()
                        .GetField(
                            "styleSheet",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        )
                        .SetValue(importStruct, styleSheet);
                    imports[beginningIndex + i] = importStruct;
                }

                themesListProperty.serializedObject.ApplyModifiedProperties();
            }
            finally
            {
                serializedThemeSettings.ApplyModifiedProperties();
                themesListProperty.Dispose();
            }
        }

        public static void ClearStyleSheets(
            UIDocument uiDocument,
            IEnumerable<StyleSheet> styleSheets
        )
        {
            if (uiDocument == null)
            {
                return;
            }
            ClearStyleSheets(uiDocument.panelSettings, styleSheets);
        }

        public static void ClearStyleSheets(
            PanelSettings panelSettings,
            IEnumerable<StyleSheet> styleSheets
        )
        {
            if (panelSettings == null)
            {
                return;
            }
            ClearStyleSheets(panelSettings.themeStyleSheet, styleSheets);
        }

        public static void ClearStyleSheets(
            ThemeStyleSheet themeSettingsAsset,
            IEnumerable<StyleSheet> styleSheets
        )
        {
            HashSet<StyleSheet> uniqueStyleSheets = new(
                styleSheets ?? Enumerable.Empty<StyleSheet>()
            );
            using SerializedObject serializedThemeSettings = new(themeSettingsAsset);
            SerializedProperty themesListProperty = serializedThemeSettings.FindProperty("imports");

            if (themesListProperty == null)
            {
                return;
            }

            try
            {
                if (!themesListProperty.isArray || themesListProperty.arraySize == 0)
                {
                    return;
                }

                for (int i = themesListProperty.arraySize - 1; 0 <= i; --i)
                {
                    SerializedProperty themeProperty = themesListProperty.GetArrayElementAtIndex(i);
                    if (themeProperty == null)
                    {
                        themesListProperty.DeleteArrayElementAtIndex(i);
                        continue;
                    }

                    try
                    {
                        StyleSheet styleSheet =
                            themeProperty.serializedObject?.targetObject as StyleSheet;
                        if (
                            styleSheet == null
                            || string.IsNullOrWhiteSpace(styleSheet.name)
                            || uniqueStyleSheets.Contains(styleSheet)
                        )
                        {
                            themesListProperty.DeleteArrayElementAtIndex(i);
                        }
                    }
                    finally
                    {
                        themeProperty.Dispose();
                    }
                }
            }
            finally
            {
                themesListProperty.serializedObject.ApplyModifiedProperties();
                serializedThemeSettings.ApplyModifiedProperties();
                themesListProperty.Dispose();
            }
        }

        public static StyleSheet[] GetTerminalThemeStyleSheets(UIDocument uiDocument)
        {
            return uiDocument == null
                ? Array.Empty<StyleSheet>()
                : GetTerminalThemeStyleSheets(uiDocument.panelSettings);
        }

        public static StyleSheet[] GetTerminalThemeStyleSheets(PanelSettings panelSettings)
        {
            return panelSettings == null
                ? Array.Empty<StyleSheet>()
                : GetTerminalThemeStyleSheets(panelSettings.themeStyleSheet);
        }

        public static StyleSheet[] GetStyleSheets(ThemeStyleSheet themeSettingsAsset)
        {
            if (themeSettingsAsset == null)
            {
                return Array.Empty<StyleSheet>();
            }

            using SerializedObject serializedThemeSettings = new(themeSettingsAsset);
            SerializedProperty themesListProperty = serializedThemeSettings.FindProperty("imports");

            if (themesListProperty == null)
            {
                return Array.Empty<StyleSheet>();
            }

            try
            {
                if (!themesListProperty.isArray || themesListProperty.arraySize == 0)
                {
                    return Array.Empty<StyleSheet>();
                }

                HashSet<StyleSheet> availableThemes = new();
                for (int i = 0; i < themesListProperty.arraySize; ++i)
                {
                    SerializedProperty themeProperty = themesListProperty.GetArrayElementAtIndex(i);
                    if (themeProperty == null)
                    {
                        continue;
                    }

                    try
                    {
                        StyleSheet styleSheet =
                            themeProperty.serializedObject?.targetObject as StyleSheet;
                        if (styleSheet == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(styleSheet.name))
                        {
                            continue;
                        }

                        availableThemes.Add(styleSheet);
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

        public static StyleSheet[] GetTerminalThemeStyleSheets(ThemeStyleSheet themeSettingsAsset)
        {
            if (themeSettingsAsset == null)
            {
                return Array.Empty<StyleSheet>();
            }

            using SerializedObject serializedThemeSettings = new(themeSettingsAsset);
            SerializedProperty themesListProperty = serializedThemeSettings.FindProperty(
                "m_FlattenedImportedStyleSheets"
            );

            if (themesListProperty == null)
            {
                return Array.Empty<StyleSheet>();
            }

            try
            {
                if (!themesListProperty.isArray || themesListProperty.arraySize == 0)
                {
                    return Array.Empty<StyleSheet>();
                }

                HashSet<StyleSheet> availableThemes = new();
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

                        availableThemes.Add(styleSheet);
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

        public static string[] GetAvailableThemes(ThemeStyleSheet themeSettingsAsset)
        {
            return GetTerminalThemeStyleSheets(themeSettingsAsset)
                .SelectMany(GetAvailableThemes)
                .OrderBy(theme => theme)
                .ToArray();
        }

        public static string[] GetAvailableThemes(StyleSheet styleSheetAsset)
        {
            if (styleSheetAsset == null)
            {
                return Array.Empty<string>();
            }

            string assetPath = AssetDatabase.GetAssetPath(styleSheetAsset);
            if (
                string.IsNullOrWhiteSpace(assetPath)
                || !assetPath.EndsWith(".uss", StringComparison.OrdinalIgnoreCase)
            )
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
                                int nextObjectBraceIndex = ussContent.IndexOf('}', braceIndex + 1);
                                if (nextObjectBraceIndex < 0)
                                {
                                    nextObjectBraceIndex = ussContent.Length;
                                }
                                string objectContents = ussContent.Substring(
                                    selectorStartIndex,
                                    nextObjectBraceIndex - selectorStartIndex
                                );

                                if (
                                    RequiredVariables.Exists(requiredVariable =>
                                        !objectContents.Contains(
                                            requiredVariable,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                    )
                                )
                                {
                                    return Array.Empty<string>();
                                }

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
