namespace CommandTerminal.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using CommandTerminal.Helper;
    using Helper;
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
        private readonly List<string> _themes = new();
        private readonly SortedDictionary<string, SortedDictionary<string, Font>> _fontsByPrefix =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<TerminalLogType, int> _seenLogTypes = new();

        private int _themeIndex = -1;
        private int _fontKey = -1;
        private int _secondFontKey = -1;

        public override void OnInspectorGUI()
        {
            UIToolkitTerminal terminal = target as UIToolkitTerminal;
            if (terminal == null)
            {
                return;
            }

            serializedObject.Update();

            bool anyChanged = false;
            if (
                _lastSeen != terminal
                || _allCommands.Count == 0
                || _defaultCommands.Count == 0
                || _nonDefaultCommands.Count == 0
                || _themes.Count == 0
            )
            {
                _themeIndex = -1;
                _fontKey = -1;
                _secondFontKey = -1;
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
                _themes.Clear();
                _themes.AddRange(StyleSheetHelper.GetAvailableThemes(terminal._uiDocument));
                Debug.Log($"Found {_themes.Count} themes.");
                if (
                    !_themes
                        .ToHashSet()
                        .SetEquals(terminal._loadedThemes ?? Enumerable.Empty<string>())
                )
                {
                    terminal._loadedThemes ??= new List<string>();
                    terminal._loadedThemes.Clear();
                    terminal._loadedThemes.AddRange(_themes);
                    anyChanged = true;
                }
                _lastSeen = terminal;
            }

            bool uiDocumentChanged = CheckForUIDocumentProblems(terminal);
            anyChanged |= uiDocumentChanged;

            bool themesChanged = CheckForThemingAndFontChanges(terminal);
            anyChanged |= themesChanged;

            DrawPropertiesExcluding(serializedObject, "m_Script");

            bool propertiesDirty = CheckForSimpleProperties(terminal);
            anyChanged |= propertiesDirty;

            RenderCommandManipulationHeader();

            bool ignoredCommandsUpdated = CheckForIgnoredCommandUpdates(terminal);
            anyChanged |= ignoredCommandsUpdated;

            bool commandsUpdated = CheckForDisabledCommandProblems(terminal);
            anyChanged |= commandsUpdated;

            serializedObject.ApplyModifiedProperties();
            if (anyChanged)
            {
                EditorUtility.SetDirty(terminal);
            }
        }

        private bool CheckForThemingAndFontChanges(UIToolkitTerminal terminal)
        {
            bool anyChanged = false;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Theming", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            try
            {
                if (_themeIndex < 0)
                {
                    _themeIndex = _themes.IndexOf(terminal._currentTheme);
                }

                if (_themeIndex < 0)
                {
                    GUILayout.Label("Select Theme:");
                }

                _themeIndex = EditorGUILayout.Popup(
                    _themeIndex,
                    _themes
                        .Select(theme =>
                            theme
                                .Replace("-theme", string.Empty, StringComparison.OrdinalIgnoreCase)
                                .Replace("theme-", string.Empty, StringComparison.OrdinalIgnoreCase)
                        )
                        .ToArray()
                );

                if (0 <= _themeIndex && _themeIndex < _themes.Count)
                {
                    string selectedTheme = _themes[_themeIndex];
                    GUIContent setThemeContent = new(
                        "Set Theme",
                        $"Will set the current theme to {selectedTheme}"
                    );
                    if (GUILayout.Button(setThemeContent))
                    {
                        terminal.SetTheme(selectedTheme);
                        anyChanged = true;
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            CollectFonts();
            bool fontsUpdated = RenderSelectableFonts(terminal);
            return anyChanged || fontsUpdated;
        }

        private bool CheckForSimpleProperties(UIToolkitTerminal terminal)
        {
            bool anyChanged = false;
            if (terminal._toggleHotkey == null)
            {
                terminal._toggleHotkey = string.Empty;
                anyChanged = true;
            }

            if (terminal._ignoredLogTypes == null)
            {
                terminal._ignoredLogTypes = new List<TerminalLogType>();
                anyChanged = true;
            }

            if (terminal._completeCommandHotkeys == null)
            {
                terminal._completeCommandHotkeys = new ListWrapper<string>();
                anyChanged = true;
            }

            if (terminal._disabledCommands == null)
            {
                terminal._disabledCommands = new List<string>();
                anyChanged = true;
            }

            if (terminal._loadedThemes == null)
            {
                terminal._loadedThemes = new List<string>();
                anyChanged = true;
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
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Command Manipulation", EditorStyles.boldLabel);
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
                    _commandIndex = EditorGUILayout.Popup(_commandIndex, ignorableCommands);

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

        private void TryMatchExistingFont(UIToolkitTerminal terminal)
        {
            if ((0 <= _fontKey || 0 <= _secondFontKey) || terminal._consoleFont == null)
            {
                return;
            }

            int keyIndex = 0;
            foreach (
                KeyValuePair<string, SortedDictionary<string, Font>> prefixEntry in _fontsByPrefix
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

        private bool TrySetupDefaultFont(UIToolkitTerminal terminal)
        {
            if (terminal._consoleFont != null || terminal._loadedFonts is not { Count: > 0 })
            {
                return false;
            }

            Font defaultFont = terminal._loadedFonts.FirstOrDefault(font =>
                font.name.Contains("SourceCodePro", StringComparison.OrdinalIgnoreCase)
                && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
            );
            if (defaultFont == null)
            {
                defaultFont = terminal._loadedFonts.FirstOrDefault(font =>
                    font.name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                    && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
                );
            }
            terminal._consoleFont = defaultFont;
            return true;
        }

        private bool RenderSelectableFonts(UIToolkitTerminal terminal)
        {
            if (_fontsByPrefix is not { Count: > 0 })
            {
                return false;
            }

            TrySetupDefaultFont(terminal);
            TryMatchExistingFont(terminal);

            bool anyChanged = false;
            int currentFontKey = _fontKey;
            EditorGUILayout.BeginHorizontal();
            try
            {
                if (_fontKey < 0 || _secondFontKey < 0)
                {
                    GUILayout.Label("Select Font:");
                }

                string[] fontKeys = _fontsByPrefix.Keys.ToArray();
                _fontKey = EditorGUILayout.Popup(_fontKey, fontKeys);

                if (currentFontKey != _fontKey)
                {
                    _secondFontKey = -1;
                }

                if (0 <= _fontKey && _fontKey < fontKeys.Length)
                {
                    string selectedFontKey = fontKeys[_fontKey];
                    SortedDictionary<string, Font> availableFonts = _fontsByPrefix[selectedFontKey];
                    string[] secondFontKeys = availableFonts.Keys.ToArray();
                    Font selectedFont = null;
                    switch (secondFontKeys.Length)
                    {
                        case > 1:
                        {
                            _secondFontKey = EditorGUILayout.Popup(_secondFontKey, secondFontKeys);

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

            return anyChanged;
        }
    }
#endif
}
