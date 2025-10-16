#if UNITY_EDITOR
namespace WallstopStudios.DxCommandTerminal.Editor
{
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DxCommandTerminal.UI;

    [InitializeOnLoad]
    internal static class LauncherLayoutDiagnostics
    {
        private const string MenuItemPath = "DX Command Terminal/Diagnostics/Log Launcher Layout";
        private const string PreferenceKey =
            "WallstopStudios.DxCommandTerminal.Diagnostics.LauncherLayout";
        private static bool isEnabled;

        static LauncherLayoutDiagnostics()
        {
            bool savedPreference = EditorPrefs.GetBool(PreferenceKey, false);
            SetEnabled(savedPreference);
        }

        [MenuItem(MenuItemPath)]
        private static void Toggle()
        {
            SetEnabled(!isEnabled);
        }

        [MenuItem(MenuItemPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuItemPath, isEnabled);
            return true;
        }

        private static void SetEnabled(bool enabled)
        {
            if (enabled == isEnabled)
            {
                return;
            }

            isEnabled = enabled;
            EditorPrefs.SetBool(PreferenceKey, isEnabled);

            TerminalUI.LauncherLayoutComputed -= OnLauncherLayoutComputed;
            if (isEnabled)
            {
                TerminalUI.LauncherLayoutComputed += OnLauncherLayoutComputed;
            }
        }

        private static void OnLauncherLayoutComputed(TerminalUI.LauncherLayoutSnapshot snapshot)
        {
            Debug.Log($"[DxCommandTerminal] {snapshot}");
        }
    }
}
#endif
