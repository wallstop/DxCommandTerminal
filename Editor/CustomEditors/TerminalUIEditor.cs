namespace WallstopStudios.DxCommandTerminal.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Backend;
    using DxCommandTerminal.Helper;
    using Helper;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Input;
    using Persistence;
    using Themes;
    using UI;
    using Object = UnityEngine.Object;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
#endif

    [CustomEditor(typeof(TerminalUI))]
    public sealed class TerminalUIEditor : Editor
    {
        private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(0.75);

        private int _commandIndex;
        private TerminalUI _lastSeen;

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
        private readonly List<TerminalFontPack> _fontPacks = new();
        private readonly List<TerminalThemePack> _themePacks = new();

        private int _themeIndex = -1;
        private int _fontKey = -1;
        private int _secondFontKey = -1;
        private int _themePackIndex = -1;
        private int _fontPackIndex = -1;
        private bool _isCyclingThemes;
        private bool _isCyclingFonts;
        private bool _persistThemeChanges;

        private TimeSpan? _lastFontCycleTime;
        private TimeSpan? _lastThemeCycleTime;

        private readonly Stopwatch _timer = Stopwatch.StartNew();

        private bool _editorUpdateAttached;
        private GUIStyle _impactButtonStyle;
        private GUIStyle _impactLabelStyle;

        static TerminalUIEditor()
        {
            ObjectFactory.componentWasAdded += HandleComponentAdded;
        }

        private static void HandleComponentAdded(Component addedComponent)
        {
            if (addedComponent is TerminalUI terminal && terminal != null)
            {
                CheckForUIDocumentProblems(terminal);

                terminal.gameObject.AddComponent<TerminalKeyboardController>();
                terminal.gameObject.AddComponent<TerminalThemePersister>();

                EditorUtility.SetDirty(terminal);
                EditorUtility.SetDirty(terminal.gameObject);
            }
            else
            {
                switch (addedComponent)
                {
                    case TerminalKeyboardController keyboardController
                        when keyboardController != null:
                    {
                        if (keyboardController.TryGetComponent(out terminal))
                        {
                            keyboardController.terminal = terminal;
                            EditorUtility.SetDirty(keyboardController);
                        }

                        break;
                    }
                    case TerminalThemePersister themePersister when themePersister != null:
                    {
                        if (themePersister.TryGetComponent(out terminal))
                        {
                            themePersister.terminal = terminal;
                            EditorUtility.SetDirty(themePersister);
                        }

                        break;
                    }
#if ENABLE_INPUT_SYSTEM
                    case PlayerInput playerInput when playerInput != null:
                    {
                        if (playerInput.TryGetComponent(out terminal))
                        {
                            if (
                                !playerInput.TryGetComponent(
                                    out TerminalPlayerInputController playerInputController
                                )
                            )
                            {
                                playerInputController =
                                    playerInput.gameObject.AddComponent<TerminalPlayerInputController>();
                                playerInputController.terminal = terminal;
                                EditorUtility.SetDirty(playerInputController);
                                EditorUtility.SetDirty(playerInput.gameObject);
                            }

                            if (
                                playerInput.TryGetComponent(
                                    out TerminalKeyboardController keyboardController
                                )
                            )
                            {
                                keyboardController.enabled = false;
                                EditorUtility.SetDirty(keyboardController);
                            }
                        }

                        break;
                    }
#endif
                }
            }
        }

        private void OnEnable()
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
            _fontsByPrefix.Clear();

            ResetStateIdempotent();

            if (!_editorUpdateAttached)
            {
                EditorApplication.update += EditorUpdate;
                _editorUpdateAttached = true;
            }
        }

        private static T[] LoadAll<T>()
            where T : Object
        {
            List<string> directories = new();

            string directory = DirectoryHelper.GetCallerScriptDirectory();
            if (!string.IsNullOrWhiteSpace(directory))
            {
                DirectoryInfo directoryInfo = new(directory);
                if (directoryInfo.Exists)
                {
                    directory = DirectoryHelper.AbsoluteToUnityRelativePath(directoryInfo.FullName);
                    directories.Add(directory);
                }
            }

            if (Directory.Exists(Path.Combine(Application.dataPath, "Packages")))
            {
                directories.Add("Packages");
            }

            if (Directory.Exists(Path.Combine(Application.dataPath, "Library")))
            {
                directories.Add("Library");
            }

            directories.Add("Assets");

            HashSet<T> loaded = new();
            string[] fontGuids = AssetDatabase.FindAssets(
                $"t:{typeof(T).Name}",
                directories.ToArray()
            );
            foreach (string guid in fontGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                T item = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                if (item != null)
                {
                    loaded.Add(item);
                }
            }
            return loaded.ToArray();
        }

        private void OnDisable()
        {
            if (_editorUpdateAttached)
            {
                EditorApplication.update -= EditorUpdate;
                _editorUpdateAttached = false;
            }
        }

        private void ResetStateIdempotent()
        {
            foreach (TerminalThemePack themePack in TerminalAssetPackPostProcessor.NewThemePacks)
            {
                _themePacks.Add(themePack);
            }
            TerminalAssetPackPostProcessor.NewThemePacks.Clear();
            foreach (TerminalFontPack fontPack in TerminalAssetPackPostProcessor.NewFontPacks)
            {
                _fontPacks.Add(fontPack);
            }
            TerminalAssetPackPostProcessor.NewFontPacks.Clear();

            if (_lastSeen == target)
            {
                return;
            }

            _fontPacks.Clear();
            _fontPacks.AddRange(LoadAll<TerminalFontPack>());

            _themePacks.Clear();
            _themePacks.AddRange(LoadAll<TerminalThemePack>());

            _persistThemeChanges = false;
            StopCyclingFonts();
            StopCyclingThemes();
            _themeIndex = -1;
            _fontKey = -1;
            _secondFontKey = -1;
            _lastSeen = target as TerminalUI;
        }

        private void EditorUpdate()
        {
            if (_isCyclingThemes)
            {
                if (_timer.Elapsed < _lastThemeCycleTime + CycleInterval)
                {
                    return;
                }

                TerminalUI terminal = target as TerminalUI;
                if (terminal != null && terminal._themePack._themeNames is { Count: > 0 })
                {
                    int newThemeIndex = (_themeIndex + 1) % terminal._themePack._themeNames.Count;
                    newThemeIndex =
                        (newThemeIndex + terminal._themePack._themeNames.Count)
                        % terminal._themePack._themeNames.Count;
                    terminal.SetTheme(
                        terminal._themePack._themeNames[newThemeIndex],
                        persist: _persistThemeChanges
                    );
                    _themeIndex = newThemeIndex;
                }

                _lastThemeCycleTime = _timer.Elapsed;
            }

            if (_isCyclingFonts)
            {
                if (_timer.Elapsed < _lastFontCycleTime + CycleInterval)
                {
                    return;
                }

                TerminalUI terminal = target as TerminalUI;
                if (terminal != null && terminal._fontPack._fonts is { Count: > 0 })
                {
                    Font currentFont = GetCurrentlySelectedFont(terminal);
                    int fontIndex = terminal._fontPack._fonts.IndexOf(currentFont);
                    int newFontIndex = (fontIndex + 1) % terminal._fontPack._fonts.Count;
                    Font newFont = terminal._fontPack._fonts[newFontIndex];
                    terminal.SetFont(newFont, persist: _persistThemeChanges);
                    TrySetFontKeysFromFont(newFont);
                }

                _lastFontCycleTime = _timer.Elapsed;
            }
        }

        private Font GetCurrentlySelectedFont(TerminalUI terminal)
        {
            if (_fontKey < 0 || _secondFontKey < 0)
            {
                return terminal.CurrentFont;
            }

            try
            {
                return _fontsByPrefix.ToArray()[_fontKey].Value.ToArray()[_secondFontKey].Value;
            }
            catch
            {
                return terminal.CurrentFont;
            }
        }

        public override void OnInspectorGUI()
        {
            _impactButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = Color.yellow },
                fontStyle = FontStyle.Bold,
            };
            _impactLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(1f, 0.3f, 0.3f, 1f) },
                fontStyle = FontStyle.Bold,
            };

            if (_allCommands.Count == 0 || _defaultCommands.Count == 0)
            {
                HydrateCommandCaches();
            }

            TerminalUI terminal = target as TerminalUI;
            if (terminal == null)
            {
                return;
            }

            serializedObject.Update();
            ResetStateIdempotent();

            bool anyChanged = false;

            bool uiDocumentChanged = CheckForUIDocumentProblems(terminal);
            anyChanged |= uiDocumentChanged;

            bool themesChanged = CheckForThemingAndFontChanges(terminal);
            anyChanged |= themesChanged;

            RenderCyclingPreviews();

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

        private void HydrateCommandCaches()
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
        }

        private void RenderCyclingPreviews()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.BeginHorizontal();
            try
            {
                EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
                bool oldPersistThemeChanges = _persistThemeChanges;
                _persistThemeChanges = GUILayout.Toggle(
                    _persistThemeChanges,
                    "Persist Theme Changes"
                );

                if (oldPersistThemeChanges != _persistThemeChanges)
                {
                    StopCyclingFonts();
                    StopCyclingThemes();
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.BeginHorizontal();
                try
                {
                    TryCyclingThemes();
                    TryCyclingFonts();
                }
                finally
                {
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.BeginHorizontal();
            try
            {
                TrySetRandomTheme();
                TrySetRandomFont();
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();
        }

        private void TrySetRandomTheme()
        {
            if (_isCyclingThemes)
            {
                return;
            }

            bool clicked = _persistThemeChanges
                ? GUILayout.Button("Set Random Theme", _impactButtonStyle)
                : GUILayout.Button("Set Random Theme ");

            if (clicked)
            {
                TerminalUI terminal = target as TerminalUI;
                if (
                    terminal != null
                    && terminal._themePack != null
                    && terminal._themePack._themeNames is { Count: > 0 }
                )
                {
                    int newThemeIndex;
                    do
                    {
                        newThemeIndex = ThreadLocalRandom.Instance.Next(
                            terminal._themePack._themeNames.Count
                        );
                    } while (
                        newThemeIndex == _themeIndex && terminal._themePack._themeNames.Count != 1
                    );
                    terminal.SetTheme(
                        terminal._themePack._themeNames[newThemeIndex],
                        persist: _persistThemeChanges
                    );
                    _themeIndex = newThemeIndex;
                }
            }
        }

        private void TrySetRandomFont()
        {
            if (_isCyclingFonts)
            {
                return;
            }

            bool clicked = _persistThemeChanges
                ? GUILayout.Button("Set Random Font", _impactButtonStyle)
                : GUILayout.Button("Set Random Font ");

            if (clicked)
            {
                TerminalUI terminal = target as TerminalUI;
                if (
                    terminal != null
                    && terminal._fontPack != null
                    && terminal._fontPack._fonts is { Count: > 0 }
                )
                {
                    Font currentlySelectedFont = GetCurrentlySelectedFont(terminal);
                    int oldFontIndex = terminal._fontPack._fonts.IndexOf(currentlySelectedFont);
                    int newFontIndex;
                    do
                    {
                        newFontIndex = ThreadLocalRandom.Instance.Next(
                            terminal._fontPack._fonts.Count
                        );
                    } while (newFontIndex == oldFontIndex && terminal._fontPack._fonts.Count != 1);

                    Font newFont = terminal._fontPack._fonts[newFontIndex];
                    terminal.SetFont(newFont, persist: _persistThemeChanges);
                    TrySetFontKeysFromFont(newFont);
                }
            }
        }

        private void TryCyclingFonts()
        {
            if (_isCyclingFonts)
            {
                if (GUILayout.Button("Stop Cycling Fonts"))
                {
                    StopCyclingFonts();
                }
            }
            else
            {
                bool clicked = _persistThemeChanges
                    ? GUILayout.Button("Start Cycling Fonts", _impactButtonStyle)
                    : GUILayout.Button("Start Cycling Fonts");
                if (clicked)
                {
                    StartCyclingFonts();
                }
            }
        }

        private void StartCyclingFonts()
        {
            if (_isCyclingFonts)
            {
                return;
            }
            _isCyclingFonts = true;
            _lastFontCycleTime = null;
        }

        private void StopCyclingFonts()
        {
            _isCyclingFonts = false;
        }

        private void TryCyclingThemes()
        {
            if (_isCyclingThemes)
            {
                if (GUILayout.Button("Stop Cycling Themes"))
                {
                    StopCyclingThemes();
                }
            }
            else
            {
                bool clicked = _persistThemeChanges
                    ? GUILayout.Button("Start Cycling Themes", _impactButtonStyle)
                    : GUILayout.Button("Start Cycling Themes");
                if (clicked)
                {
                    StartCyclingThemes();
                }
            }
        }

        private void StartCyclingThemes()
        {
            if (_isCyclingThemes)
            {
                return;
            }

            _isCyclingThemes = true;
            _lastThemeCycleTime = null;
        }

        private void StopCyclingThemes()
        {
            _isCyclingThemes = false;
        }

        private void TrySetFontKeysFromFont(Font font)
        {
            int firstIndex = 0;
            bool foundFont = false;
            foreach (
                KeyValuePair<string, SortedDictionary<string, Font>> fontKeyEntry in _fontsByPrefix
            )
            {
                int secondIndex = 0;
                foreach (KeyValuePair<string, Font> secondFontKeyEntry in fontKeyEntry.Value)
                {
                    if (secondFontKeyEntry.Value == font)
                    {
                        _fontKey = firstIndex;
                        _secondFontKey = secondIndex;
                        foundFont = true;
                        break;
                    }

                    ++secondIndex;
                }

                if (foundFont)
                {
                    break;
                }

                ++firstIndex;
            }
        }

        private bool CheckForThemingAndFontChanges(TerminalUI terminal)
        {
            bool anyChanged = false;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Theming", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            try
            {
                if (!_themePacks.Any())
                {
                    GUILayout.Label("NO THEME PACKS", _impactLabelStyle);
                }
                else
                {
                    if (_themePackIndex < 0)
                    {
                        _themePackIndex = _themePacks.IndexOf(terminal._themePack);
                    }

                    _themePackIndex = EditorGUILayout.Popup(
                        _themePackIndex,
                        _themePacks.Select(themePack => themePack.name).ToArray()
                    );
                    if (0 <= _themePackIndex)
                    {
                        if (GUILayout.Button("Set Theme Pack", _impactButtonStyle))
                        {
                            StyleSheet[] styleSheets = StyleSheetHelper.GetTerminalThemeStyleSheets(
                                terminal._uiDocument
                            );
                            StyleSheetHelper.ClearStyleSheets(terminal._uiDocument, styleSheets);
                            terminal._themePack = _themePacks[_themePackIndex];
                            StyleSheetHelper.AddStyleSheets(
                                terminal._uiDocument,
                                terminal._themePack._themes
                            );
                            anyChanged = true;
                        }
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            try
            {
                if (!_fontPacks.Any())
                {
                    GUILayout.Label("NO FONT PACKS", _impactLabelStyle);
                }
                else
                {
                    if (0 < _fontPackIndex)
                    {
                        _fontPackIndex = _fontPacks.IndexOf(terminal._fontPack);
                    }

                    _fontPackIndex = EditorGUILayout.Popup(
                        _fontPackIndex,
                        _fontPacks.Select(fontPack => fontPack.name).ToArray()
                    );
                    if (0 <= _fontPackIndex)
                    {
                        if (GUILayout.Button("Set Font Pack", _impactButtonStyle))
                        {
                            terminal._fontPack = _fontPacks[_themePackIndex];
                            anyChanged = true;
                        }
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            try
            {
                if (terminal._themePack == null)
                {
                    GUILayout.Label("NO THEME PACK", _impactLabelStyle);
                }
                else
                {
                    if (_themeIndex < 0)
                    {
                        _themeIndex = terminal._themePack._themeNames.IndexOf(
                            terminal.CurrentTheme
                        );
                    }

                    if (_themeIndex < 0)
                    {
                        GUILayout.Label("Select Theme:");
                    }

                    _themeIndex = EditorGUILayout.Popup(
                        _themeIndex,
                        terminal
                            ._themePack._themeNames.Select(theme =>
                                theme
                                    .Replace(
                                        "-theme",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                    .Replace(
                                        "theme-",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                            )
                            .ToArray()
                    );

                    if (0 <= _themeIndex && _themeIndex < terminal._themePack._themeNames.Count)
                    {
                        string selectedTheme = terminal._themePack._themeNames[_themeIndex];
                        GUIContent setThemeContent = new(
                            "Set Theme",
                            $"Will set the current theme to {selectedTheme}"
                        );
                        if (GUILayout.Button(setThemeContent, _impactButtonStyle))
                        {
                            terminal.SetTheme(selectedTheme, persist: true);
                            anyChanged = true;
                        }
                    }
                }
            }
            finally
            {
                EditorGUILayout.EndHorizontal();
            }

            CollectFonts(_fontsByPrefix);
            bool fontsUpdated = RenderSelectableFonts(terminal);
            return anyChanged || fontsUpdated;
        }

        private bool CheckForSimpleProperties(TerminalUI terminal)
        {
            bool anyChanged = false;

            if (terminal._ignoredLogTypes == null)
            {
                terminal._ignoredLogTypes = new List<TerminalLogType>();
                anyChanged = true;
            }

            if (terminal._disabledCommands == null)
            {
                terminal._disabledCommands = new List<string>();
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

        private static bool CheckForUIDocumentProblems(TerminalUI terminal)
        {
            bool anyChanged = false;
            if (terminal._uiDocument == null)
            {
                terminal._uiDocument = terminal.TryGetComponent(out UIDocument uiDocument)
                    ? uiDocument
                    : terminal.gameObject.AddComponent<UIDocument>();
                anyChanged = true;
            }

            if (terminal._uiDocument.panelSettings != null)
            {
                return anyChanged;
            }

            string[] panelSettingGuids;
            string absoluteStylesPath = DirectoryHelper.FindAbsolutePathToDirectory("Styles");
            if (!string.IsNullOrWhiteSpace(absoluteStylesPath))
            {
                panelSettingGuids = AssetDatabase.FindAssets(
                    "t:PanelSettings",
                    new[] { absoluteStylesPath }
                );
                TryFindTerminalSettings();
                if (terminal._uiDocument.panelSettings != null)
                {
                    return true;
                }
            }

            List<string> directories = new();
            if (Directory.Exists(Path.Combine(Application.dataPath, "Library")))
            {
                directories.Add("Library");
            }

            if (Directory.Exists(Path.Combine(Application.dataPath, "Packages")))
            {
                directories.Add("Packages");
            }

            directories.Add("Assets");
            panelSettingGuids = AssetDatabase.FindAssets("t:PanelSettings", directories.ToArray());
            TryFindTerminalSettings();
            return anyChanged;

            void TryFindTerminalSettings()
            {
                foreach (string guid in panelSettingGuids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrWhiteSpace(assetPath))
                    {
                        continue;
                    }

                    if (!assetPath.Contains("TerminalSettings", StringComparison.OrdinalIgnoreCase))
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

                    PanelSettings copiedSettings = AssetCreator.CreateAssetSafe(
                        panelSettings,
                        $"TerminalSettings-{terminal.id}"
                    );

                    if (copiedSettings == null)
                    {
                        continue;
                    }

                    ThemeStyleSheet themeStyleSheet = panelSettings.themeStyleSheet;
                    if (themeStyleSheet != null)
                    {
                        List<StyleSheet> copiedStyleSheets = new();
                        StyleSheet[] initialStyleSheets = StyleSheetHelper.GetStyleSheets(
                            themeStyleSheet
                        );
                        int count = 0;
                        foreach (StyleSheet styleSheet in initialStyleSheets)
                        {
                            if (
                                styleSheet.name.Contains(
                                    "UnityDefaultRuntimeTheme",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                copiedStyleSheets.Add(styleSheet);
                                continue;
                            }

                            StyleSheet copy = AssetCreator.CreateAssetSafe(
                                styleSheet,
                                $"StyleSheet-{styleSheet.name}-{count++}",
                                "Assets/Packages/WallstopStudios.DxCommandTerminal/Styles"
                            );
                            if (copy != null)
                            {
                                copiedStyleSheets.Add(copy);
                            }
                        }

                        StyleSheetHelper.ClearStyleSheets(themeStyleSheet, initialStyleSheets);
                        try
                        {
                            StyleSheetHelper.AddStyleSheets(themeStyleSheet, copiedStyleSheets);
                            try
                            {
                                ThemeStyleSheet copiedStyleSheet = AssetCreator.CreateAssetSafe(
                                    themeStyleSheet,
                                    $"TerminalTheme-{terminal.id}"
                                );

                                copiedSettings.themeStyleSheet =
                                    copiedStyleSheet != null ? copiedStyleSheet : null;
                            }
                            finally
                            {
                                StyleSheetHelper.ClearStyleSheets(
                                    themeStyleSheet,
                                    copiedStyleSheets
                                );
                            }
                        }
                        finally
                        {
                            StyleSheetHelper.AddStyleSheets(themeStyleSheet, initialStyleSheets);
                        }
                    }

                    terminal._uiDocument.panelSettings = copiedSettings;
                    anyChanged = true;
                    return;
                }
            }
        }

        private static void RenderCommandManipulationHeader()
        {
            EditorGUILayout.Space();
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Command Manipulation", EditorStyles.boldLabel);
        }

        private bool CheckForIgnoredCommandUpdates(TerminalUI terminal)
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

        private bool CheckForDisabledCommandProblems(TerminalUI terminal)
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

        private static void CollectFonts(
            SortedDictionary<string, SortedDictionary<string, Font>> fontsByPrefix
        )
        {
            if (fontsByPrefix.Count != 0)
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

                if (!fontsByPrefix.TryGetValue(key, out SortedDictionary<string, Font> fontMapping))
                {
                    fontMapping = new SortedDictionary<string, Font>(
                        StringComparer.OrdinalIgnoreCase
                    );
                    fontsByPrefix[key] = fontMapping;
                }

                fontMapping[secondKey] = font;
            }
        }

        private void TryMatchExistingFont(TerminalUI terminal)
        {
            if (0 <= _fontKey || 0 <= _secondFontKey || terminal.CurrentFont == null)
            {
                return;
            }

            TrySetFontKeysFromFont(terminal.CurrentFont);
        }

        private static bool TrySetupDefaultFont(TerminalUI terminal)
        {
            if (
                terminal.CurrentFont != null
                && terminal._fontPack != null
                && terminal._fontPack._fonts.Contains(terminal.CurrentFont)
            )
            {
                return false;
            }

            if (terminal._fontPack == null || terminal._fontPack._fonts is not { Count: > 0 })
            {
                return false;
            }

            Font defaultFont = terminal._fontPack._fonts.FirstOrDefault(font =>
                font.name.Contains("SourceCodePro", StringComparison.OrdinalIgnoreCase)
                && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
            );
            if (defaultFont == null)
            {
                defaultFont = terminal._fontPack._fonts.FirstOrDefault(font =>
                    font.name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                    && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
                );
            }
            if (defaultFont == null)
            {
                defaultFont = terminal._fontPack._fonts.FirstOrDefault(font =>
                    font.name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                );
            }
            if (defaultFont == null)
            {
                defaultFont = terminal._fontPack._fonts.FirstOrDefault(font =>
                    font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
                );
            }
            if (defaultFont == null)
            {
                defaultFont = terminal._fontPack._fonts.FirstOrDefault();
            }

            terminal.SetFont(defaultFont, persist: true);
            return true;
        }

        private bool RenderSelectableFonts(TerminalUI terminal)
        {
            if (_fontsByPrefix is not { Count: > 0 })
            {
                return false;
            }

            _ = TrySetupDefaultFont(terminal);
            TryMatchExistingFont(terminal);

            bool anyChanged = false;
            int currentFontKey = _fontKey;
            EditorGUILayout.BeginHorizontal();
            try
            {
                if (terminal._fontPack == null)
                {
                    GUILayout.Label("NO FONT PACK", _impactLabelStyle);
                }
                else
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
                        SortedDictionary<string, Font> availableFonts = _fontsByPrefix[
                            selectedFontKey
                        ];
                        string[] secondFontKeys = availableFonts.Keys.ToArray();
                        Font selectedFont = null;
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
                            if (GUILayout.Button(setFontContent, _impactButtonStyle))
                            {
                                terminal.SetFont(selectedFont, persist: true);
                                anyChanged = true;
                            }
                        }
                    }
                }
            }
            finally
            {
                GUILayout.EndHorizontal();
            }

            return anyChanged;
        }
    }
#endif
}
