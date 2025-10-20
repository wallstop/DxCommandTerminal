#if UNITY_EDITOR
namespace WallstopStudios.DxCommandTerminal.Editor.Diagnostics
{
    using Backend;
    using Service;
    using UnityEditor;

    public sealed class TerminalRuntimeInspectorWindow : EditorWindow
    {
        [MenuItem("Window/DX Command Terminal/Runtime Inspector")]
        private static void Open()
        {
            TerminalRuntimeInspectorWindow window = GetWindow<TerminalRuntimeInspectorWindow>();
            window.titleContent = new UnityEngine.GUIContent("Terminal Runtime Inspector");
            window.Show();
        }

        private void OnGUI()
        {
            ITerminalRuntimeScope runtimeScope = TerminalUI.ServiceLocator?.RuntimeScope;
            ITerminalRuntime runtime = runtimeScope?.ActiveRuntime;
            if (runtime == null)
            {
                EditorGUILayout.HelpBox("No active terminal runtime detected.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Active Runtime", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Commands",
                runtime.Shell?.Commands?.Count.ToString() ?? "n/a"
            );
            EditorGUILayout.LabelField(
                "History Entries",
                runtime.History?.Count.ToString() ?? "n/a"
            );
            EditorGUILayout.LabelField("Log Capacity", runtime.Log?.Capacity.ToString() ?? "n/a");
            EditorGUILayout.LabelField(
                "Autocomplete",
                runtime.AutoComplete != null ? "Available" : "Missing"
            );
        }
    }
}
#endif
