namespace WallstopStudios.DxCommandTerminal.Editor
{
#if UNITY_EDITOR
    using Backend;
    using UI;
    using UnityEditor;
    using UnityEngine;

    internal static class TerminalUIRuntimeModeMenu
    {
        private const string MenuRoot = "Tools/DxCommandTerminal/Runtime Mode/";

        [MenuItem(MenuRoot + "Editor", false, 0)]
        private static void SetEditorMode()
        {
            SetSelectedRuntimeMode(TerminalRuntimeModeFlags.Editor);
        }

        [MenuItem(MenuRoot + "Development", false, 1)]
        private static void SetDevelopmentMode()
        {
            SetSelectedRuntimeMode(TerminalRuntimeModeFlags.Development);
        }

        [MenuItem(MenuRoot + "Production", false, 2)]
        private static void SetProductionMode()
        {
            SetSelectedRuntimeMode(TerminalRuntimeModeFlags.Production);
        }

        [MenuItem(MenuRoot + "Editor+Development", false, 10)]
        private static void SetEditorDevMode()
        {
            SetSelectedRuntimeMode(
                TerminalRuntimeModeFlags.Editor | TerminalRuntimeModeFlags.Development
            );
        }

        [MenuItem(MenuRoot + "All", false, 11)]
        private static void SetAllMode()
        {
            SetSelectedRuntimeMode(TerminalRuntimeModeFlags.All);
        }

        [MenuItem(MenuRoot + "Toggle Auto-Discover Parsers", false, 50)]
        private static void ToggleAutoDiscover()
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is GameObject go && go.TryGetComponent<TerminalUI>(out var ui))
                {
                    var so = new SerializedObject(ui);
                    var prop = so.FindProperty("_autoDiscoverParsersInEditor");
                    if (prop != null)
                    {
                        prop.boolValue = !prop.boolValue;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(ui);
                    }
                }
            }
        }

        private static void SetSelectedRuntimeMode(TerminalRuntimeModeFlags mode)
        {
            foreach (var obj in Selection.objects)
            {
                if (obj is GameObject go && go.TryGetComponent<TerminalUI>(out var ui))
                {
                    var so = new SerializedObject(ui);
                    var prop = so.FindProperty("_runtimeModes");
                    if (prop != null)
                    {
                        prop.intValue = (int)mode;
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(ui);
                    }
                }
            }
        }
    }
#endif
}
