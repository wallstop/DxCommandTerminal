namespace DxCommandTerminal.Editor.CustomEditors
{
    using System.Collections.Generic;
    using System.Linq;
    using CommandTerminal;
    using UnityEditor;
    using UnityEngine;

#if UNITY_EDITOR
    [CustomEditor(typeof(Terminal))]
    public sealed class TerminalEditor : Editor
    {
        private int _commandIndex;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            Terminal terminal = target as Terminal;
            if (terminal == null)
            {
                return;
            }

            string[] allCommands = CommandShell
                .RegisteredCommands.Value.Select(tuple => tuple.attribute.Name)
                .ToArray();

            bool anyChanged = false;
            if (terminal.disabledCommands == null)
            {
                anyChanged = true;
                terminal.disabledCommands = new List<string>();
            }

            for (int i = terminal.disabledCommands.Count - 1; i >= 0; --i)
            {
                string command = terminal.disabledCommands[i];
                if (allCommands.Contains(command))
                {
                    continue;
                }

                terminal.disabledCommands.RemoveAt(i);
                anyChanged = true;
            }

            string[] ignorableCommands = allCommands.Except(terminal.disabledCommands).ToArray();
            if (ignorableCommands.Any())
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Ignorable Commands");

                _commandIndex = EditorGUILayout.Popup(_commandIndex, ignorableCommands);
                if (
                    0 <= _commandIndex
                    && _commandIndex < ignorableCommands.Length
                    && GUILayout.Button("Ignore Command")
                )
                {
                    string command = ignorableCommands[_commandIndex];
                    terminal.disabledCommands.Add(command);
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                EditorUtility.SetDirty(terminal);
            }
        }
    }
#endif
}
