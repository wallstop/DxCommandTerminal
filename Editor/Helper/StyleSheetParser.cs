#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;

public static class StyleSheetParser
{
    /// <summary>
    /// Extracts selector strings (like .class, #id, TypeName) defined in a StyleSheet asset.
    /// This reads and parses the source .uss file. Editor Only.
    /// </summary>
    /// <param name="styleSheetAsset">The StyleSheet asset to parse.</param>
    /// <returns>A List of unique selector strings found, or null if an error occurs.</returns>
    public static List<string> ExtractRootSelectors(StyleSheet styleSheetAsset)
    {
        if (styleSheetAsset == null)
        {
            return null;
        }

        // 1. Get the path to the .uss file
        string assetPath = AssetDatabase.GetAssetPath(styleSheetAsset);
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        // Ensure it's actually a .uss file (though GetAssetPath on StyleSheet should guarantee this)
        if (!assetPath.EndsWith(".uss", System.StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            // 2. Read the file content
            string ussContent = File.ReadAllText(assetPath);

            // 3. Basic Cleaning: Remove Comments
            // Remove block comments /* ... */
            ussContent = Regex.Replace(
                ussContent,
                @"/\*.*?\*/",
                string.Empty,
                RegexOptions.Singleline
            );
            // Remove line comments // ...
            ussContent = Regex.Replace(ussContent, @"//.*$", string.Empty, RegexOptions.Multiline);

            // 4. Parse Selectors
            // We are looking for text preceding an opening curly brace '{'
            // This regex finds blocks of non-brace characters followed by '{'.
            // It handles multiple selectors separated by commas.
            HashSet<string> selectors = new HashSet<string>(); // Use HashSet for automatic uniqueness
            // Match everything from the start of a line (or after a '}') up to the next '{'
            // Regex: Match non-'{' '}' characters greedily, ending just before a '{'
            // This needs refinement to handle potential nested blocks like @media, but works for simple root selectors.
            // Let's try a simpler approach: find all occurrences of '{' and extract the preceding selector line.

            int lastIndex = 0;
            while (lastIndex < ussContent.Length)
            {
                int braceIndex = ussContent.IndexOf('{', lastIndex);
                if (braceIndex == -1)
                    break; // No more rule blocks

                // Extract the text between the last rule block (or start) and this '{'
                // Find the end of the previous rule block '}' if any
                int previousRuleEnd = ussContent.LastIndexOf(
                    '}',
                    braceIndex - 1,
                    braceIndex - lastIndex
                );
                int selectorStartIndex = (previousRuleEnd == -1) ? lastIndex : previousRuleEnd + 1;

                string selectorPart = ussContent
                    .Substring(selectorStartIndex, braceIndex - selectorStartIndex)
                    .Trim();

                if (!string.IsNullOrWhiteSpace(selectorPart))
                {
                    // Split by comma for multiple selectors (e.g., .a, .b { ... })
                    string[] individualSelectors = selectorPart.Split(',');
                    foreach (string sel in individualSelectors)
                    {
                        string trimmedSelector = sel.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedSelector))
                        {
                            selectors.Add(trimmedSelector);
                        }
                    }
                }

                // Find the matching closing brace to skip the rule block content (simple handling, doesn't account for nesting)
                int nextBraceIndex = ussContent.IndexOf('}', braceIndex + 1);
                lastIndex = (nextBraceIndex == -1) ? ussContent.Length : nextBraceIndex + 1; // Move past this rule block
                // A more robust parser would handle nested braces for @media etc.
            }

            return selectors.ToList();
        }
        catch (System.Exception ex)
        {
            return null;
        }
    }

    // --- Example Usage ---
    [MenuItem("Assets/Log Selectors from StyleSheet", true)]
    private static bool ValidateLogSelectors()
    {
        // Enable the menu item only if a StyleSheet is selected
        return Selection.activeObject is StyleSheet;
    }

    [MenuItem("Assets/Log Selectors from StyleSheet", false, 20)]
    private static void LogSelectorsFromSelected()
    {
        StyleSheet selectedSheet = Selection.activeObject as StyleSheet;
        if (selectedSheet != null)
        {
            List<string> selectors = ExtractRootSelectors(selectedSheet);
            if (selectors != null)
            {
                Debug.Log(
                    $"Selectors found in '{selectedSheet.name}' ({selectors.Count}):\n"
                        + string.Join("\n", selectors)
                );

                // Specifically check for ".some-object" as per your example
                if (selectors.Contains(".some-object"))
                {
                    Debug.Log("Found '.some-object' selector!");
                }
            }
        }
    }
}
#endif
