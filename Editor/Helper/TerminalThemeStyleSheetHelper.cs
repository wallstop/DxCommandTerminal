namespace WallstopStudios.DxCommandTerminal.Editor.Helper
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Themes;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    public static class TerminalThemeStyleSheetHelper
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

                            if (ThemeNameHelper.IsThemeName(trimmedSelector))
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
