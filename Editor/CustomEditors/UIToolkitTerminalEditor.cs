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

        private readonly SortedDictionary<string, SortedDictionary<string, Font>> _fontsByPrefix =
            new(StringComparer.OrdinalIgnoreCase);

        private int _fontKey = -1;
        private int _secondFontKey = -1;

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

                EditorGUILayout.BeginHorizontal();
                try
                {
                    _commandIndex = EditorGUILayout.Popup(
                        "Commands",
                        _commandIndex,
                        ignorableCommands
                    );
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
                finally
                {
                    EditorGUILayout.EndHorizontal();
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

            if (_fontsByPrefix.Count == 0)
            {
                Font[] fonts = CommandTerminalFontLoader.LoadFonts();
                foreach (Font font in fonts)
                {
                    string fontName = font.name;
                    int indexOfSplit = fontName.IndexOf('-', StringComparison.OrdinalIgnoreCase);
                    if (indexOfSplit < 0)
                    {
                        indexOfSplit = fontName.IndexOf('_', StringComparison.OrdinalIgnoreCase);
                    }

                    string key;
                    string secondKey;
                    if (0 <= indexOfSplit)
                    {
                        key = fontName[..indexOfSplit];
                        secondKey = fontName[Mathf.Min(indexOfSplit + 1, fontName.Length)..];
                    }
                    else
                    {
                        key = fontName;
                        secondKey = string.Empty;
                    }

                    if (
                        !_fontsByPrefix.TryGetValue(
                            key,
                            out SortedDictionary<string, Font> fontMapping
                        )
                    )
                    {
                        fontMapping = new SortedDictionary<string, Font>(
                            StringComparer.OrdinalIgnoreCase
                        );
                        _fontsByPrefix[key] = fontMapping;
                    }

                    fontMapping[secondKey] = font;
                }
            }

            if (_fontsByPrefix is { Count: > 0 })
            {
                if (_fontKey < 0 && _secondFontKey < 0 && terminal._consoleFont != null)
                {
                    int keyIndex = 0;
                    foreach (
                        KeyValuePair<
                            string,
                            SortedDictionary<string, Font>
                        > prefixEntry in _fontsByPrefix
                    )
                    {
                        int subKeyIndex = 0;
                        foreach (KeyValuePair<string, Font> subEntry in prefixEntry.Value)
                        {
                            if (subEntry.Value == terminal._consoleFont)
                            {
                                _fontKey = keyIndex;
                                _secondFontKey = subKeyIndex;
                                break;
                            }
                            subKeyIndex++;
                        }

                        if (0 <= _secondFontKey)
                        {
                            break;
                        }

                        keyIndex++;
                    }
                }

                GUILayout.BeginHorizontal();
                try
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("Font Selection");
                    GUILayout.FlexibleSpace();
                }
                finally
                {
                    GUILayout.EndHorizontal();
                }

                int currentFontKey = _fontKey;
                EditorGUILayout.BeginHorizontal();
                try
                {
                    string[] fontKeys = _fontsByPrefix.Keys.ToArray();
                    _fontKey = EditorGUILayout.Popup(_fontKey, fontKeys);
                    if (currentFontKey != _fontKey)
                    {
                        _secondFontKey = -1;
                    }

                    if (0 <= _fontKey && _fontKey < fontKeys.Length)
                    {
                        string selectedFontKey = fontKeys[_fontKey];
                        SortedDictionary<string, Font> availableFonts = _fontsByPrefix[
                            selectedFontKey
                        ];
                        string[] secondFontKeys = availableFonts.Keys.ToArray();
                        switch (secondFontKeys.Length)
                        {
                            case > 1:
                            {
                                _secondFontKey = EditorGUILayout.Popup(
                                    _secondFontKey,
                                    secondFontKeys
                                );
                                if (0 <= _secondFontKey && _secondFontKey < secondFontKeys.Length)
                                {
                                    if (GUILayout.Button("Set Font"))
                                    {
                                        terminal._consoleFont = availableFonts[
                                            secondFontKeys[_secondFontKey]
                                        ];
                                    }
                                }

                                break;
                            }
                            case 1 when GUILayout.Button("Set Font"):
                            {
                                terminal._consoleFont = availableFonts.Values.Single();
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    GUILayout.EndHorizontal();
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
