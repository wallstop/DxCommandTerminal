namespace CommandTerminal.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using CommandTerminal;
    using CommandTerminal.Helper;
    using Helper;
    using UIToolkit;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utils;

    [CustomEditor(typeof(UIToolkitTerminal))]
    public sealed class UIToolkitTerminalEditor : Editor
    {
        private int _commandIndex;
        private UIToolkitTerminal _lastSeen;

        private readonly HashSet<string> _allCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _defaultCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _nonDefaultCommands = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly HashSet<string> _seenCommands = new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> _intermediateResults = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly SortedDictionary<string, SortedDictionary<string, Font>> _fontsByPrefix =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<TerminalLogType, int> _seenLogTypes = new();

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

            if (
                _lastSeen != terminal
                || _allCommands.Count == 0
                || _defaultCommands.Count == 0
                || _nonDefaultCommands.Count == 0
            )
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
                _lastSeen = terminal;
            }

            bool anyChanged = false;
            if (terminal._disabledCommands == null)
            {
                anyChanged = true;
                terminal._disabledCommands = new List<string>();
            }

            bool propertiesDirty = CheckForSimpleProperties(terminal);
            anyChanged |= propertiesDirty;

            bool uiDocumentChanged = CheckForUIDocumentProblems(terminal);
            anyChanged |= uiDocumentChanged;

            RenderCommandManipulationHeader();

            bool ignoredCommandsUpdated = CheckForIgnoredCommandUpdates(terminal);
            anyChanged |= ignoredCommandsUpdated;

            bool commandsUpdated = CheckForDisabledCommandProblems(terminal);
            anyChanged |= commandsUpdated;

            EditorGUILayout.Space();
            CollectFonts();
            bool fontsUpdated = RenderSelectableFonts(terminal);
            anyChanged |= fontsUpdated;

            string[] availableThemes = StyleSheetHelper.GetAvailableThemes(terminal._uiDocument);
            Debug.Log($"Found {availableThemes.Length} available themes.");
            _ = EditorGUILayout.Popup("Themes", 0, availableThemes);

            if (anyChanged)
            {
                EditorUtility.SetDirty(terminal);
            }
        }

        private bool CheckForSimpleProperties(UIToolkitTerminal terminal)
        {
            bool anyChanged = false;
            if (terminal._toggleHotkey == null)
            {
                anyChanged = true;
                terminal._toggleHotkey = string.Empty;
            }

            if (terminal._ignoredLogTypes == null)
            {
                anyChanged = true;
                terminal._ignoredLogTypes = new List<TerminalLogType>();
            }

            if (terminal._completeCommandHotkeys == null)
            {
                anyChanged = true;
                terminal._completeCommandHotkeys = new ListWrapper<string>();
            }

            _seenLogTypes.Clear();
            for (int i = terminal._ignoredLogTypes.Count - 1; 0 <= i; --i)
            {
                TerminalLogType logType = terminal._ignoredLogTypes[i];
                int count = 0;
                if (
                    Enum.IsDefined(typeof(TerminalLogType), logType)
                    && (!_seenLogTypes.TryGetValue(logType, out count) || count <= 1)
                )
                {
                    _seenLogTypes[logType] = count + 1;
                    continue;
                }

                _seenLogTypes[logType] = count + 1;
                anyChanged = true;
                terminal._ignoredLogTypes.RemoveAt(i);
            }

            return anyChanged;
        }

        private static bool CheckForUIDocumentProblems(UIToolkitTerminal terminal)
        {
            bool anyChanged = false;
            if (terminal._uiDocument == null)
            {
                terminal._uiDocument = terminal.gameObject.AddComponent<UIDocument>();
                anyChanged = true;
            }

            if (terminal._uiDocument.panelSettings == null)
            {
                string absoluteStylesPath = DirectoryHelper.FindAbsolutePathToDirectory("Styles");
                if (!string.IsNullOrWhiteSpace(absoluteStylesPath))
                {
                    string[] panelSettingGuids = AssetDatabase.FindAssets(
                        "t:PanelSettings",
                        new[] { absoluteStylesPath }
                    );
                    foreach (string guid in panelSettingGuids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrWhiteSpace(assetPath))
                        {
                            continue;
                        }

                        if (
                            !assetPath.Contains(
                                "TerminalSettings",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            continue;
                        }

                        PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                            assetPath
                        );
                        if (panelSettings == null)
                        {
                            continue;
                        }

                        terminal._uiDocument.panelSettings = panelSettings;
                        anyChanged = true;
                        break;
                    }
                }
            }

            return anyChanged;
        }

        private static void RenderCommandManipulationHeader()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            try
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("Command Manipulation");
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }
        }

        private bool CheckForIgnoredCommandUpdates(UIToolkitTerminal terminal)
        {
            bool anyChanged = false;
            _intermediateResults.Clear();
            _intermediateResults.UnionWith(_nonDefaultCommands);
            if (!terminal.ignoreDefaultCommands)
            {
                _intermediateResults.UnionWith(_defaultCommands);
            }
            _intermediateResults.ExceptWith(terminal._disabledCommands);

            if (0 < _intermediateResults.Count)
            {
                string[] ignorableCommands = _intermediateResults.ToArray();

                EditorGUILayout.BeginHorizontal();
                try
                {
                    GUILayout.FlexibleSpace();
                    if (0 <= _commandIndex && _commandIndex < ignorableCommands.Length)
                    {
                        GUILayoutOption width = GenerateWidth(ignorableCommands[_commandIndex]);
                        _commandIndex = EditorGUILayout.Popup(
                            _commandIndex,
                            ignorableCommands,
                            width
                        );
                    }
                    else
                    {
                        _commandIndex = EditorGUILayout.Popup(_commandIndex, ignorableCommands);
                    }

                    if (0 <= _commandIndex && _commandIndex < ignorableCommands.Length)
                    {
                        GUIContent ignoreContent = new(
                            "Ignore Command",
                            $"Ignores the {ignorableCommands[_commandIndex]} command"
                        );
                        if (GUILayout.Button(ignoreContent))
                        {
                            string command = ignorableCommands[_commandIndex];
                            terminal._disabledCommands.Add(command);
                            anyChanged = true;
                        }
                    }
                }
                finally
                {
                    EditorGUILayout.EndHorizontal();
                }
            }

            return anyChanged;
        }

        private bool CheckForDisabledCommandProblems(UIToolkitTerminal terminal)
        {
            bool anyChanged = false;
            _seenCommands.Clear();
            _seenCommands.UnionWith(terminal._disabledCommands);

            if (
                _seenCommands.Count != terminal._disabledCommands.Count
                || terminal._disabledCommands.Exists(command => !_allCommands.Contains(command))
            )
            {
                EditorGUILayout.BeginHorizontal();
                try
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Cleanup Disabled Commands"))
                    {
                        _seenCommands.Clear();
                        for (int i = terminal._disabledCommands.Count - 1; 0 <= i; --i)
                        {
                            string command = terminal._disabledCommands[i];
                            if (!_seenCommands.Add(command))
                            {
                                terminal._disabledCommands.RemoveAt(i);
                                anyChanged = true;
                                continue;
                            }

                            if (!_allCommands.Contains(command))
                            {
                                terminal._disabledCommands.RemoveAt(i);
                                anyChanged = true;
                            }
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
                finally
                {
                    EditorGUILayout.EndHorizontal();
                }
            }

            return anyChanged;
        }

        private void CollectFonts()
        {
            if (_fontsByPrefix.Count != 0)
            {
                return;
            }

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
                    !_fontsByPrefix.TryGetValue(key, out SortedDictionary<string, Font> fontMapping)
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

        private bool RenderSelectableFonts(UIToolkitTerminal terminal)
        {
            bool anyChanged = false;
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
                }
                finally
                {
                    GUILayout.EndHorizontal();
                }

                int currentFontKey = _fontKey;
                EditorGUILayout.BeginHorizontal();
                try
                {
                    GUILayout.FlexibleSpace();
                    string[] fontKeys = _fontsByPrefix.Keys.ToArray();
                    if (0 <= _fontKey && _fontKey < fontKeys.Length)
                    {
                        GUILayoutOption width = GenerateWidth(fontKeys[_fontKey]);
                        _fontKey = EditorGUILayout.Popup(_fontKey, fontKeys, width);
                    }
                    else
                    {
                        _fontKey = EditorGUILayout.Popup(_fontKey, fontKeys);
                    }

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
                        Font selectedFont = null;
                        switch (secondFontKeys.Length)
                        {
                            case > 1:
                            {
                                if (0 <= _secondFontKey && _secondFontKey < secondFontKeys.Length)
                                {
                                    GUILayoutOption width = GenerateWidth(
                                        secondFontKeys[_secondFontKey]
                                    );
                                    _secondFontKey = EditorGUILayout.Popup(
                                        _secondFontKey,
                                        secondFontKeys,
                                        width
                                    );
                                }
                                else
                                {
                                    _secondFontKey = EditorGUILayout.Popup(
                                        _secondFontKey,
                                        secondFontKeys
                                    );
                                }

                                if (0 <= _secondFontKey && _secondFontKey < secondFontKeys.Length)
                                {
                                    selectedFont = availableFonts[secondFontKeys[_secondFontKey]];
                                }

                                break;
                            }
                            case 1:
                            {
                                selectedFont = availableFonts.Values.Single();
                                break;
                            }
                        }

                        if (selectedFont != null)
                        {
                            GUIContent setFontContent = new(
                                "Set Font",
                                $"Update the terminal's font to {selectedFont.name}"
                            );
                            if (GUILayout.Button(setFontContent))
                            {
                                terminal._consoleFont = selectedFont;
                                anyChanged = true;
                            }
                        }
                    }
                }
                finally
                {
                    GUILayout.EndHorizontal();
                }

                bool needUpdate =
                    terminal._loadedFonts == null
                    || terminal._loadedFonts.Count == 0
                    || terminal._loadedFonts.Exists(font => font == null);
                if (needUpdate)
                {
                    terminal._loadedFonts ??= new List<Font>();
                    terminal._loadedFonts.Clear();
                    terminal._loadedFonts.AddRange(
                        _fontsByPrefix.SelectMany(kvp => kvp.Value).Select(kvp => kvp.Value)
                    );
                    anyChanged = true;
                }
                else
                {
                    Font[] availableFonts = _fontsByPrefix
                        .SelectMany(kvp => kvp.Value)
                        .Select(kvp => kvp.Value)
                        .ToArray();
                    if (!terminal._loadedFonts.ToHashSet().SetEquals(availableFonts))
                    {
                        GUIContent buttonContent = new(
                            "Serialize Fonts",
                            $"Takes all loaded fonts ({availableFonts.Length}) and stores them for use at runtime in the Terminal"
                        );
                        if (GUILayout.Button(buttonContent))
                        {
                            terminal._loadedFonts ??= new List<Font>();
                            terminal._loadedFonts.Clear();
                            terminal._loadedFonts.AddRange(
                                _fontsByPrefix.SelectMany(kvp => kvp.Value).Select(kvp => kvp.Value)
                            );
                            anyChanged = true;
                        }
                    }
                }
            }

            return anyChanged;
        }

        private static GUILayoutOption GenerateWidth(string input)
        {
            return GUILayout.Width(input.Length * 8f + 16f);
        }
    }
#endif
}
