#if UNITY_EDITOR // Important: Only compile this code in the Unity Editor
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Collections.Generic;
using System.Linq; // Optional: If you want to store results

public static class ThemeSettingsEditorInspector
{
    /// <summary>
    /// Retrieves and logs StyleSheets directly from a ThemeSettings asset using SerializedObject.
    /// </summary>
    /// <param name="themeSettingsAsset">The ThemeSettings asset to inspect. MUST be of type ThemeSettings.</param>
    public static void LogStyleSheetsFromThemeSettings(ThemeStyleSheet themeSettingsAsset)
    {
        if (themeSettingsAsset == null)
        {
            Debug.LogError("Input asset is null. Cannot inspect.");
            return;
        }

        Debug.Log(
            $"--- Inspecting ThemeSettings Asset: '{themeSettingsAsset.name}' (Type: {themeSettingsAsset.GetType()}) ---"
        );

        // Create a SerializedObject based *specifically* on the ThemeSettings asset
        SerializedObject serializedThemeSettings = new SerializedObject(themeSettingsAsset);

        // Find the internal list of themes. This field name is standard in Unity's ThemeSettings.
        // *** This is the critical step where the error likely occurred if the target object wasn't ThemeSettings ***
        SerializedProperty themesListProperty = serializedThemeSettings.FindProperty(
            "m_FlattenedImportedStyleSheets"
        );

        if (themesListProperty == null)
        {
            // If this happens, the object being inspected is NOT a standard ThemeSettings asset,
            // or Unity has drastically changed the internal structure (less likely).
            Debug.LogError(
                $"Could not find the internal 'm_Themes' list property on the provided asset '{themeSettingsAsset.name}'. "
                    + $"Ensure the asset is a valid ThemeSettings asset and the Unity version hasn't fundamentally changed its structure."
            );
            serializedThemeSettings.Dispose();
            return;
        }

        if (!themesListProperty.isArray)
        {
            Debug.LogError(
                $"Property 'm_Themes' on asset '{themeSettingsAsset.name}' is not an array/list."
            );
            serializedThemeSettings.Dispose();
            return;
        }

        if (themesListProperty.arraySize == 0)
        {
            Debug.LogWarning(
                $"ThemeSettings asset '{themeSettingsAsset.name}' has no themes defined in its 'm_Themes' list."
            );
        }

        // Iterate through each theme structure found within the 'm_Themes' list
        for (int i = 0; i < themesListProperty.arraySize; i++)
        {
            SerializedProperty themeProperty = themesListProperty.GetArrayElementAtIndex(i);
            if (themeProperty == null)
                continue;

            var styleSheet = themeProperty.objectReferenceValue as StyleSheet;
            if (styleSheet == null)
                continue;

            if (string.IsNullOrWhiteSpace(styleSheet.name))
                continue;
            if (!styleSheet.name.Contains("Theme"))
                continue;

            // Inside each theme structure, find its name ('m_Name') and its list of StyleSheets ('m_StyleSheet')
            string themeName = styleSheet.name;
            themeName = string.IsNullOrEmpty(themeName) ? $"Default/Unnamed Theme {i}" : themeName;

            Debug.Log($"-- Found Theme: '{themeName}' --");
            foreach (
                string rootObject in StyleSheetParser.ExtractRootSelectors(styleSheet)
                    ?? Enumerable.Empty<string>()
            )
            {
                Debug.Log($"   - Root Object: '{rootObject}'");
            }
        }

        // Dispose the SerializedObject when done
        serializedThemeSettings.Dispose();
    }

    /// <summary>
    /// Helper to get StyleSheets from a UIDocument's configured ThemeSettings in the editor.
    /// </summary>
    /// <param name="uiDocument">The UIDocument component.</param>
    public static void LogStyleSheetsFromUIDocument(UIDocument uiDocument)
    {
        if (uiDocument == null)
        {
            Debug.LogError("UIDocument component is null.");
            return;
        }
        if (uiDocument.panelSettings == null)
        {
            Debug.LogError($"UIDocument '{uiDocument.name}' does not have PanelSettings assigned.");
            return;
        }
        // Get the reference to the ThemeSettings asset from PanelSettings
        var themeSettings = uiDocument.panelSettings.themeStyleSheet;

        if (themeSettings == null)
        {
            // It's possible to use PanelSettings without assigning a specific ThemeSettings asset
            Debug.LogWarning(
                $"PanelSettings '{uiDocument.panelSettings.name}' does not have a ThemeSettings asset assigned. Checking PanelSettings.themeStyleSheet instead."
            );
            // Check the single themeStyleSheet property on PanelSettings as a fallback
            if (uiDocument.panelSettings.themeStyleSheet != null)
            {
                Debug.Log($"--- PanelSettings.themeStyleSheet (Singular) ---");
                StyleSheet singleSheet = uiDocument.panelSettings.themeStyleSheet;
                Debug.Log($"   - StyleSheet Name: '{singleSheet.name}', Object: {singleSheet}");
            }
            else
            {
                Debug.Log(
                    "PanelSettings.themeStyleSheet is also null. No theme stylesheets configured via PanelSettings."
                );
            }
            return;
        }

        // We have a ThemeSettings asset, proceed to inspect it
        LogStyleSheetsFromThemeSettings(themeSettings);
    }
}

// --- Example Usage (e.g., in an Editor Window or Custom Inspector) ---
public class MyEditorScriptMenuItems : Editor
{
    [MenuItem("Tools/Log Theme StyleSheets from Selected UIDocument")]
    static void LogSelectedUIDocumentStyleSheets()
    {
        UIDocument selectedDoc = Selection.activeGameObject?.GetComponent<UIDocument>();
        if (selectedDoc != null)
        {
            ThemeSettingsEditorInspector.LogStyleSheetsFromUIDocument(selectedDoc);
        }
        else
        {
            Debug.LogWarning(
                "Please select a GameObject with a UIDocument component in the scene."
            );
        }
    }

    [MenuItem("Tools/Log Theme StyleSheets from Selected ThemeSettings Asset")]
    static void LogSelectedThemeSettingsAssetStyleSheets()
    {
        ThemeStyleSheet selectedAsset = Selection.activeObject as ThemeStyleSheet;
        if (selectedAsset != null)
        {
            ThemeSettingsEditorInspector.LogStyleSheetsFromThemeSettings(selectedAsset);
        }
        else
        {
            Debug.LogWarning("Please select a ThemeSettings asset in the Project window.");
        }
    }
}
#endif // UNITY_EDITOR
