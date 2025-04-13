namespace CommandTerminal.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using CommandTerminal;
    using UIToolkit;
    using UnityEditor;
    using UnityEngine;

    [CustomEditor(typeof(UIToolkitTerminal))]
    public sealed class UIToolkitTerminalEditor : Editor
    {
        private int _commandIndex;
        private bool _initialized;

        private readonly HashSet<string> _allCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _defaultCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _nonDefaultCommands = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly HashSet<string> _seenCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _intermediateResults = new(
            StringComparer.OrdinalIgnoreCase
        );

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            UIToolkitTerminal terminal = target as UIToolkitTerminal;
            if (terminal == null)
            {
                return;
            }

            if (!_initialized)
            {
                _allCommands.Clear();
                _allCommands.UnionWith(
                    CommandShell
                        .RegisteredCommands.Value.Select(tuple => tuple.attribute)
                        .Select(attribute => attribute.Name)
                );
                _defaultCommands.Clear();
                _defaultCommands.UnionWith(
                    CommandShell
                        .RegisteredCommands.Value.Select(tuple => tuple.attribute)
                        .Where(tuple => tuple.Default)
                        .Select(attribute => attribute.Name)
                );
                _nonDefaultCommands.Clear();
                _nonDefaultCommands.UnionWith(
                    CommandShell
                        .RegisteredCommands.Value.Select(tuple => tuple.attribute)
                        .Where(tuple => !tuple.Default)
                        .Select(attribute => attribute.Name)
                );
                _initialized = true;
            }

            bool anyChanged = false;
            if (terminal.disabledCommands == null)
            {
                anyChanged = true;
                terminal.disabledCommands = new List<string>();
            }

            _intermediateResults.Clear();
            _intermediateResults.UnionWith(_nonDefaultCommands);
            if (!terminal.ignoreDefaultCommands)
            {
                _intermediateResults.UnionWith(_defaultCommands);
            }
            _intermediateResults.ExceptWith(terminal.disabledCommands);

            if (0 < _intermediateResults.Count)
            {
                string[] ignorableCommands = _intermediateResults.ToArray();
                Array.Sort(ignorableCommands);

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

            _seenCommands.Clear();
            _seenCommands.UnionWith(terminal.disabledCommands);

            if (
                _seenCommands.Count != terminal.disabledCommands.Count
                || terminal.disabledCommands.Exists(command => !_allCommands.Contains(command))
            )
            {
                if (GUILayout.Button("Cleanup Disabled Commands"))
                {
                    _seenCommands.Clear();
                    for (int i = terminal.disabledCommands.Count - 1; 0 <= i; --i)
                    {
                        string command = terminal.disabledCommands[i];
                        if (!_seenCommands.Add(command))
                        {
                            terminal.disabledCommands.RemoveAt(i);
                            anyChanged = true;
                            continue;
                        }

                        if (!_allCommands.Contains(command))
                        {
                            terminal.disabledCommands.RemoveAt(i);
                            anyChanged = true;
                        }
                    }
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
