﻿namespace CommandTerminal.UIToolkit
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using Attributes;
    using CommandTerminal;
    using CommandTerminal.Extensions;
    using CommandTerminal.Utils;
    using JetBrains.Annotations;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;
    using UnityEngine.Serialization;
    using UnityEngine.UIElements;
    using Button = UnityEngine.UIElements.Button;

    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerminalThemeSwitcher))]
    public sealed class UIToolkitTerminal : MonoBehaviour
    {
        private static readonly Dictionary<string, string> CachedSubstrings = new();
        private static readonly Dictionary<Color, Texture2D> CachedTextures = new();

        private static readonly Dictionary<string, string> SpecialKeyCodeMap = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            { "`", "backquote" },
            { "-", "minus" },
            { "=", "equals" },
            { "[", "leftBracket" },
            { "]", "rightBracket" },
            { ";", "semicolon" },
            { "'", "quote" },
            { "\\", "backslash" },
            { ",", "comma" },
            { ".", "period" },
            { "/", "slash" },
            { "1", "digit1" },
            { "2", "digit2" },
            { "3", "digit3" },
            { "4", "digit4" },
            { "5", "digit5" },
            { "6", "digit6" },
            { "7", "digit7" },
            { "8", "digit8" },
            { "9", "digit9" },
            { "0", "digit0" },
            { "up", "upArrow" },
            { "left", "leftArrow" },
            { "right", "rightArrow" },
            { "down", "downArrow" },
            { " ", "space" },
        };

        private static readonly Dictionary<string, string> SpecialShiftedKeyCodeMap = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            { "~", "backquote" },
            { "!", "digit1" },
            { "@", "digit2" },
            { "#", "digit3" },
            { "$", "digit4" },
            { "^", "digit6" },
            { "%", "digit5" },
            { "&", "digit7" },
            { "*", "digit8" },
            { "(", "digit9" },
            { ")", "digit0" },
            { "_", "minus" },
            { "+", "equals" },
            { "{", "leftBracket" },
            { "}", "rightBracket" },
            { ":", "semicolon" },
            { "\"", "quote" },
            { "|", "backslash" },
            { "<", "comma" },
            { ">", "period" },
            { "?", "slash" },
        };

        private static readonly Dictionary<string, string> AlternativeSpecialShiftedKeyCodeMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "!", "1" },
                { "@", "2" },
                { "#", "3" },
                { "$", "4" },
                { "^", "5" },
                { "%", "6" },
                { "&", "7" },
                { "*", "8" },
                { "(", "9" },
                { ")", "0" },
            };

        public static UIToolkitTerminal Instance { get; private set; }

        public static CommandLog Buffer { get; private set; }
        public static CommandShell Shell { get; private set; }

        public static CommandHistory History { get; private set; }

        // ReSharper disable once MemberCanBePrivate.Global
        public static CommandAutoComplete AutoComplete { get; private set; }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool IsClosed =>
            _state != TerminalState.OpenFull
            && _state != TerminalState.OpenSmall
            && Mathf.Approximately(_currentOpenT, _openTarget);

        [Header("Window")]
        [Range(0, 1)]
        [SerializeField]
        private float _maxHeight = 0.7f;

        [SerializeField]
        [Range(0, 1)]
        private float _smallTerminalRatio = 0.33f;

        [Range(100, 1000)]
        [SerializeField]
        private float _toggleSpeed = 360;

        [SerializeField]
        private int _logBufferSize = 256;

        [SerializeField]
        private int _historyBufferSize = 512;

        [Header("Hotkeys")]
#if ENABLE_INPUT_SYSTEM
        [SerializeField]
        [Tooltip("If you are binding your own input actions, this needs to be set to false.")]
        private bool _useHotkeys = true;
#endif

        [DxShowIf(nameof(_useHotkeys))]
        [SerializeField]
        private string _toggleHotkey = "`";

        [DxShowIf(nameof(_useHotkeys))]
        [SerializeField]
        private string _toggleFullHotkey = "#`";

        [DxShowIf(nameof(_useHotkeys))]
        [SerializeField]
        private string _completeHotkey = "tab";

        [DxShowIf(nameof(_useHotkeys))]
        [SerializeField]
        private string _reverseCompleteHotkey = "#tab";

        [DxShowIf(nameof(_useHotkeys))]
        [SerializeField]
        private string _previousHotkey = "up";

        [DxShowIf(nameof(_useHotkeys))]
        [SerializeField]
        private ListWrapper<string> _completeCommandHotkeys = new()
        {
            list = { "enter", "return" },
        };

        [DxShowIf(nameof(_useHotkeys))]
        [SerializeField]
        private string _closeHotkey = "escape";

        [DxShowIf(nameof(_useHotkeys))]
        [SerializeField]
        private string _nextHotkey = "down";

        [Header("Input")]
        [SerializeField]
        private Font _consoleFont;

        [SerializeField]
        private string _inputCaret = ">";

        [Header("Buttons")]
        [SerializeField]
        private bool _showGUIButtons;

        [DxShowIf(nameof(_showGUIButtons))]
        [SerializeField]
        private Color _buttonBackground = new(0, 0, 0, 0.5f);

        [DxShowIf(nameof(_showGUIButtons))]
        [SerializeField]
        private Color _buttonForegroundColor = Color.white;

        [DxShowIf(nameof(_showGUIButtons))]
        [SerializeField]
        private string _runButtonText = "run";

        [DxShowIf(nameof(_showGUIButtons))]
        [SerializeField]
        private string _closeButtonText = "close";

        [DxShowIf(nameof(_showGUIButtons))]
        [SerializeField]
        private string _smallButtonText = "small";

        [DxShowIf(nameof(_showGUIButtons))]
        [SerializeField]
        private string _fullButtonText = "full";

        [Header("Hints")]
        [SerializeField]
        private HintDisplayMode _hintDisplayMode = HintDisplayMode.AutoCompleteOnly;

        [SerializeField]
        private bool _makeHintsClickable;

        [SerializeField]
        private Color _unselectedHintBackground = new(0f, 0f, 0f, 0.25f);

        [SerializeField]
        private Color _unselectedHintTextColor = Color.grey;

        [SerializeField]
        private Color _selectedHintBackground = new(0f, 0f, 0f, 0.5f);

        [SerializeField]
        private Color _selectedHintTextColor = Color.white;

        [Header("Theme")]
        [SerializeField]
        private Color _inputBackground = new(0, 0, 0, 0.5f);

        [FormerlySerializedAs("_backgroundColor")]
        [SerializeField]
        private Color _background = Color.black;

        [SerializeField]
        private Color _textColor = Color.white;

        [SerializeField]
        private Color _shellMessageColor = Color.white;

        [SerializeField]
        private Color _inputTextColor = Color.cyan;

        [SerializeField]
        private Color _warningColor = Color.yellow;

        [SerializeField]
        private Color _errorColor = Color.red;

        [Header("System")]
        [SerializeField]
        private bool _trackChangesInEditor = true;

        [Tooltip("Will reset static command state in OnEnable and Start when set to true")]
        public bool resetStateOnInit;

        [SerializeField]
        private bool _skipSameCommandsInHistory = true;

        [SerializeField]
        public bool ignoreDefaultCommands;

        [SerializeField]
        private bool _logUnityMessages = true;

        [SerializeField]
        private List<TerminalLogType> _ignoredLogTypes = new();

        [SerializeField]
        public List<string> disabledCommands = new();

#if UNITY_EDITOR
        private readonly Dictionary<TerminalLogType, int> _seenLogTypes = new();
        private readonly Dictionary<string, object> _propertyValues = new();
        private readonly List<SerializedProperty> _staticStateProperties = new();
        private readonly List<SerializedProperty> _windowProperties = new();
        private readonly List<SerializedProperty> _windowStyleProperties = new();
        private readonly List<SerializedProperty> _buttonProperties = new();
        private readonly List<SerializedProperty> _inputProperties = new();
        private readonly List<SerializedProperty> _labelProperties = new();
        private readonly List<SerializedProperty> _logUnityMessageProperties = new();
        private readonly List<SerializedProperty> _autoCompleteProperties = new();
        private SerializedObject _serializedObject;
#endif
        private readonly Dictionary<string, GUILayoutOption[]> _buttonTextOptions = new();
        private readonly Dictionary<string, float> _buttonWidths = new();

        private TerminalState _state = TerminalState.Closed;
        private TextEditor _editorState;
        private bool _inputFix;
        private bool _moveCursor;
        private Rect _window;
        private float _currentOpenT;
        private float _openTarget;
        private float _realWindowSize;
        private string _commandText = string.Empty;

        private Vector2 _scrollPosition;
        private GUIStyle _windowStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _runButtonStyle;
        private GUIStyle _labelStyle;

        private GUIStyle _inputCaretStyle;
        private GUIStyle _unselectedHintStyle;
        private GUIStyle _firstUnselectedHintStyle;
        private GUIStyle _selectedHintStyle;
        private GUIStyle _firstSelectedHintStyle;
        private GUIStyle _inputStyle;
        private GUILayoutOption[] _inputCaretOptions;
        private bool _unityLogAttached;
        private bool _started;

        private int? _lastWidth;
        private int? _lastHeight;
        private bool _handledInputThisFrame;
        private bool _needsFocus;
        private bool _needsAutoCompleteReset;

        private string _lastKnownCommandText;
        private string[] _lastCompletionBuffer = Array.Empty<string>();
        private float[] _completionElementWidthBuffer = Array.Empty<float>();
        private GUILayoutOption[][] _completionElementStyles = Array.Empty<GUILayoutOption[]>();
        private readonly List<int> _completionIndexWindow = new();
        private int? _lastCompletionIndex;
        private int? _previousLastCompletionIndex;
        private int _currentHintStartIndex;
        private string _focusedControl;

        private bool _initialResetStateOnInit;

        // UI Toolkit fields
        private VisualElement _terminalContainer;
        private ScrollView _logScrollView;
        private VisualElement _autoCompleteContainer;
        private VisualElement _inputContainer;
        private TextField _commandInput;
        private Button _runButton;
        private VisualElement _stateButtonContainer;
        private VisualElement _textInput;

        [StringFormatMethod("format")]
        public static bool Log(string format, params object[] parameters)
        {
            return Log(TerminalLogType.ShellMessage, format, parameters);
        }

        [StringFormatMethod("format")]
        public static bool Log(TerminalLogType type, string format, params object[] parameters)
        {
            CommandLog buffer = Buffer;
            if (buffer == null)
            {
                return false;
            }

            string formattedMessage = parameters is { Length: > 0 }
                ? string.Format(format, parameters)
                : format;
            return buffer.HandleLog(formattedMessage, type);
        }

        private void Awake()
        {
            switch (_logBufferSize)
            {
                case <= 0:
                    Debug.LogError(
                        $"Invalid buffer size '{_logBufferSize}', must be greater than zero. Defaulting to 0 (empty buffer).",
                        this
                    );
                    break;
                case < 10:
                    Debug.LogWarning(
                        $"Unsupported buffer size '{_logBufferSize}', recommended size is > 10.",
                        this
                    );
                    break;
            }

            switch (_historyBufferSize)
            {
                case <= 0:
                    Debug.LogError(
                        $"Invalid buffer size '{_historyBufferSize}', must be greater than zero. Defaulting to 0 (empty buffer).",
                        this
                    );
                    break;
                case < 10:
                    Debug.LogWarning(
                        $"Unsupported buffer size '{_historyBufferSize}', recommended size is > 10.",
                        this
                    );
                    break;
            }

            Instance = this;

#if UNITY_EDITOR
            _serializedObject = new SerializedObject(this);

            string[] staticStaticPropertiesTracked =
            {
                nameof(_logBufferSize),
                nameof(_historyBufferSize),
                nameof(_ignoredLogTypes),
                nameof(disabledCommands),
                nameof(ignoreDefaultCommands),
            };
            TrackProperties(staticStaticPropertiesTracked, _staticStateProperties);

            string[] windowPropertiesTracked =
            {
                nameof(_maxHeight),
                nameof(_smallTerminalRatio),
                nameof(_showGUIButtons),
            };
            TrackProperties(windowPropertiesTracked, _windowProperties);

            string[] windowStylePropertiesTracked =
            {
                nameof(_background),
                nameof(_textColor),
                nameof(_consoleFont),
            };
            TrackProperties(windowStylePropertiesTracked, _windowStyleProperties);

            string[] inputPropertiesTracked =
            {
                nameof(_inputBackground),
                nameof(_inputTextColor),
                nameof(_inputCaret),
                nameof(_unselectedHintBackground),
                nameof(_unselectedHintTextColor),
                nameof(_selectedHintBackground),
                nameof(_selectedHintTextColor),
            };
            TrackProperties(inputPropertiesTracked, _inputProperties);

            string[] buttonPropertiesTracked =
            {
                nameof(_buttonBackground),
                nameof(_buttonForegroundColor),
                nameof(_runButtonText),
                nameof(_closeButtonText),
                nameof(_smallButtonText),
                nameof(_fullButtonText),
            };
            TrackProperties(buttonPropertiesTracked, _buttonProperties);

            string[] labelPropertiesTracked = { nameof(_consoleFont), nameof(_textColor) };
            TrackProperties(labelPropertiesTracked, _labelProperties);

            string[] logUnityMessagePropertiesTracked = { nameof(_logUnityMessages) };
            TrackProperties(logUnityMessagePropertiesTracked, _logUnityMessageProperties);

            string[] autoCompletePropertiesTracked =
            {
                nameof(_hintDisplayMode),
                nameof(disabledCommands),
                nameof(ignoreDefaultCommands),
            };
            TrackProperties(autoCompletePropertiesTracked, _autoCompleteProperties);

            void TrackProperties(string[] properties, List<SerializedProperty> storage)
            {
                foreach (string propertyName in properties)
                {
                    SerializedProperty property = _serializedObject.FindProperty(propertyName);
                    if (property != null)
                    {
                        storage.Add(property);
                        object value = property.GetValue();
                        switch (value)
                        {
                            case List<string> stringList:
                                value = stringList.ToList();
                                break;
                            case List<TerminalLogType> logTypeList:
                                value = logTypeList.ToList();
                                break;
                        }
                        _propertyValues[property.name] = value;
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"Failed to track/find window property {propertyName}, updates to this property will be ignored."
                        );
                    }
                }
            }
#endif
        }

        private void OnEnable()
        {
            RefreshStaticState(force: resetStateOnInit);
            ConsumeAndLogErrors();

            if (_logUnityMessages && !_unityLogAttached)
            {
                Application.logMessageReceivedThreaded += HandleUnityLog;
                _unityLogAttached = true;
            }

#if UNITY_EDITOR
            EditorApplication.update += CheckForChanges;
#endif
            SetupUI();
        }

        private void OnDisable()
        {
            if (_unityLogAttached)
            {
                Application.logMessageReceivedThreaded -= HandleUnityLog;
                _unityLogAttached = false;
            }

            SetState(TerminalState.Closed);
#if UNITY_EDITOR
            EditorApplication.update -= CheckForChanges;
#endif
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;
        }

        private void Start()
        {
            if (_started)
            {
                SetState(TerminalState.Closed);
            }

            if (_consoleFont == null)
            {
                _consoleFont = Font.CreateDynamicFontFromOSFont("Courier New", 16);
                Debug.LogWarning("Command Console Warning: Please assign a font.", this);
            }

            if (
                _useHotkeys
                && (
                    _completeCommandHotkeys?.list?.Exists(command =>
                        string.Equals(command, _toggleHotkey, StringComparison.OrdinalIgnoreCase)
                    ) ?? false
                )
            )
            {
                Debug.LogError(
                    $"Invalid Toggle Hotkey {_toggleHotkey} - cannot be in the set of complete command "
                        + $"hotkeys: [{string.Join(",", _completeCommandHotkeys?.list ?? Enumerable.Empty<string>())}]"
                );
            }

            RefreshStaticState(force: resetStateOnInit);
            SetupWindow();
            SetupWindowStyle();
            SetupButtons();
            SetupInput();
            SetupLabels();
            ConsumeAndLogErrors();
            ResetAutoComplete();
            _started = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            bool anyChanged = false;
            if (_toggleHotkey == null)
            {
                anyChanged = true;
                _toggleHotkey = string.Empty;
            }

            if (_ignoredLogTypes == null)
            {
                anyChanged = true;
                _ignoredLogTypes = new List<TerminalLogType>();
            }

            if (_completeCommandHotkeys == null)
            {
                anyChanged = true;
                _completeCommandHotkeys = new ListWrapper<string>();
            }

            _seenLogTypes.Clear();
            for (int i = _ignoredLogTypes.Count - 1; 0 <= i; --i)
            {
                TerminalLogType logType = _ignoredLogTypes[i];
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
                _ignoredLogTypes.RemoveAt(i);
            }

            if (anyChanged)
            {
                EditorUtility.SetDirty(this);
            }
        }
#endif

#if ENABLE_INPUT_SYSTEM
        private void Update()
        {
            if (!_useHotkeys || _handledInputThisFrame)
            {
                return;
            }

            if (IsKeyPressed(_closeHotkey))
            {
                _handledInputThisFrame = true;
                Close();
            }
            else if (_completeCommandHotkeys?.list?.Exists(key => IsKeyPressed(key)) == true)
            {
                _handledInputThisFrame = true;
                EnterCommand();
            }
            else if (IsKeyPressed(_previousHotkey))
            {
                _handledInputThisFrame = true;
                HandlePrevious();
            }
            else if (IsKeyPressed(_nextHotkey))
            {
                _handledInputThisFrame = true;
                HandleNext();
            }
            else if (IsKeyPressed(_toggleFullHotkey))
            {
                _handledInputThisFrame = true;
                ToggleFull();
            }
            else if (IsKeyPressed(_toggleHotkey))
            {
                _handledInputThisFrame = true;
                ToggleSmall();
            }
            else if (IsKeyPressed(_reverseCompleteHotkey))
            {
                _handledInputThisFrame = true;
                CompleteCommand(searchForward: false);
            }
            else if (IsKeyPressed(_completeHotkey))
            {
                _handledInputThisFrame = true;
                CompleteCommand(searchForward: true);
            }
        }
#endif

        private void LateUpdate()
        {
            if (_lastHeight != Screen.height || _lastWidth != Screen.width)
            {
                SetupWindow();
            }

            _handledInputThisFrame = false;
            HandleOpenness();
            RefreshUI();
        }

        private void RefreshStaticState(bool force)
        {
            int logBufferSize = Math.Max(0, _logBufferSize);
            if (force || Buffer == null)
            {
                Buffer = new CommandLog(logBufferSize, _ignoredLogTypes);
            }
            else
            {
                if (Buffer.Capacity != logBufferSize)
                {
                    Buffer.Resize(logBufferSize);
                }
                if (
                    !Buffer.ignoredLogTypes.SetEquals(
                        _ignoredLogTypes ?? Enumerable.Empty<TerminalLogType>()
                    )
                )
                {
                    Buffer.ignoredLogTypes.Clear();
                    Buffer.ignoredLogTypes.UnionWith(
                        _ignoredLogTypes ?? Enumerable.Empty<TerminalLogType>()
                    );
                }
            }

            int historyBufferSize = Math.Max(0, _historyBufferSize);
            if (force || History == null)
            {
                History = new CommandHistory(historyBufferSize);
            }
            else if (History.Capacity != historyBufferSize)
            {
                History.Resize(historyBufferSize);
            }

            if (force || Shell == null)
            {
                Shell = new CommandShell(History);
            }

            if (force || AutoComplete == null)
            {
                AutoComplete = new CommandAutoComplete(History, Shell);
            }

            if (
                Shell.IgnoringDefaultCommands != ignoreDefaultCommands
                || Shell.Commands.Count <= 0
                || !Shell.IgnoredCommands.SetEquals(disabledCommands ?? Enumerable.Empty<string>())
            )
            {
                Shell.ClearAutoRegisteredCommands();
                Shell.InitializeAutoRegisteredCommands(
                    ignoredCommands: disabledCommands,
                    ignoreDefaultCommands: ignoreDefaultCommands
                );

                if (_started)
                {
                    ResetAutoComplete();
                }
            }
        }

#if UNITY_EDITOR
        private void CheckForChanges()
        {
            if (!_trackChangesInEditor)
            {
                return;
            }

            if (!_started)
            {
                return;
            }

            if (Instance != this)
            {
                return;
            }

            _serializedObject.Update();
            if (CheckForRefresh(_staticStateProperties))
            {
                RefreshStaticState(force: false);
            }

            if (CheckForRefresh(_windowProperties))
            {
                SetupWindow();
            }

            if (CheckForRefresh(_windowStyleProperties))
            {
                SetupWindowStyle();
            }

            if (CheckForRefresh(_inputProperties))
            {
                SetupInput();
            }

            if (CheckForRefresh(_buttonProperties))
            {
                SetupButtons();
            }

            if (CheckForRefresh(_labelProperties))
            {
                SetupLabels();
            }

            if (CheckForRefresh(_logUnityMessageProperties))
            {
                if (_logUnityMessages && !_unityLogAttached)
                {
                    _unityLogAttached = true;
                    Application.logMessageReceivedThreaded += HandleUnityLog;
                }
                else if (!_logUnityMessages && _unityLogAttached)
                {
                    Application.logMessageReceivedThreaded -= HandleUnityLog;
                    _unityLogAttached = false;
                }
            }

            if (CheckForRefresh(_autoCompleteProperties))
            {
                ResetAutoComplete();
            }

            return;

            bool CheckForRefresh(List<SerializedProperty> properties)
            {
                bool needRefresh = false;
                foreach (SerializedProperty property in properties)
                {
                    object propertyValue = property.GetValue();
                    object previousValue = _propertyValues[property.name];
                    if (
                        propertyValue is List<string> currentStringList
                        && previousValue is List<string> previousStringList
                    )
                    {
                        if (!currentStringList.SequenceEqual(previousStringList))
                        {
                            needRefresh = true;
                            _propertyValues[property.name] = currentStringList.ToList();
                        }

                        continue;
                    }
                    if (
                        propertyValue is List<TerminalLogType> currentLogTypeList
                        && previousValue is List<TerminalLogType> previousLogTypeList
                    )
                    {
                        if (!currentLogTypeList.SequenceEqual(previousLogTypeList))
                        {
                            needRefresh = true;
                            _propertyValues[property.name] = currentLogTypeList.ToList();
                        }

                        continue;
                    }

                    if (Equals(propertyValue, previousValue))
                    {
                        continue;
                    }

                    needRefresh = true;
                    _propertyValues[property.name] = propertyValue;
                }

                return needRefresh;
            }
        }
#endif

        public void ToggleState(TerminalState newState)
        {
            SetState(_state == newState ? TerminalState.Closed : newState);
        }

        public void SetState(TerminalState newState)
        {
            _inputFix = true;
            try
            {
                switch (newState)
                {
                    case TerminalState.Closed:
                    {
                        _openTarget = 0;
                        break;
                    }
                    case TerminalState.OpenSmall:
                    {
                        _openTarget = Screen.height * _maxHeight * _smallTerminalRatio;
                        _realWindowSize = Mathf.Max(_realWindowSize, _openTarget);
                        _scrollPosition.y = int.MaxValue;
                        break;
                    }
                    case TerminalState.OpenFull:
                    {
                        _realWindowSize = Screen.height * _maxHeight;
                        _openTarget = _realWindowSize;
                        break;
                    }
                    default:
                    {
                        throw new InvalidEnumArgumentException(
                            nameof(newState),
                            (int)newState,
                            typeof(TerminalState)
                        );
                    }
                }

                _state = newState;
            }
            finally
            {
                if (_state != TerminalState.Closed)
                {
                    _needsFocus = true;
                }
                else
                {
                    _commandText = string.Empty;
                    ResetAutoComplete();
                }
            }
        }

        private static void ConsumeAndLogErrors()
        {
            while (Shell?.TryConsumeErrorMessage(out string error) == true)
            {
                Log(TerminalLogType.Error, $"Error: {error}");
            }
        }

        private void ResetAutoComplete()
        {
            _lastKnownCommandText = _commandText ?? string.Empty;
            _lastCompletionIndex = null;
            _currentHintStartIndex = 0;
            if (_hintDisplayMode == HintDisplayMode.Always)
            {
                _lastCompletionBuffer =
                    AutoComplete?.Complete(_lastKnownCommandText) ?? Array.Empty<string>();
            }
            else
            {
                _lastCompletionBuffer = Array.Empty<string>();
            }
            CalculateAutoCompleteHintSize();
        }

        private void CalculateAutoCompleteHintSize()
        {
            _completionElementWidthBuffer =
                _lastCompletionBuffer.Length == 0
                    ? Array.Empty<float>()
                    : new float[_lastCompletionBuffer.Length];
            _completionElementStyles =
                _lastCompletionBuffer.Length == 0
                    ? Array.Empty<GUILayoutOption[]>()
                    : new GUILayoutOption[_lastCompletionBuffer.Length][];
            for (int i = 0; i < _lastCompletionBuffer.Length; ++i)
            {
                string completion = _lastCompletionBuffer[i];
                GUIContent completionContent = new(completion);
                GUIStyle style =
                    _lastCompletionIndex == i ? _selectedHintStyle : _unselectedHintStyle;
                Vector2 size = style.CalcSize(completionContent);
                _completionElementWidthBuffer[i] = size.x + style.margin.left + style.margin.right;
                _completionElementStyles[i] = new[] { GUILayout.Width(size.x) };
            }
        }

        private void SetupWindow()
        {
            int height = Screen.height;
            int width = Screen.width;

            try
            {
                switch (_state)
                {
                    case TerminalState.OpenSmall:
                    {
                        _realWindowSize = height * _maxHeight * _smallTerminalRatio;
                        _openTarget = _realWindowSize;
                        _scrollPosition.y = int.MaxValue;
                        break;
                    }
                    case TerminalState.OpenFull:
                    {
                        _realWindowSize = height * _maxHeight;
                        _openTarget = _realWindowSize;
                        break;
                    }
                    default:
                    {
                        _realWindowSize = height * _maxHeight * _smallTerminalRatio;
                        _openTarget = 0;
                        break;
                    }
                }
            }
            finally
            {
                _window = new Rect(0, _currentOpenT - _realWindowSize, width, _realWindowSize);
                _lastHeight = height;
                _lastWidth = width;
            }
        }

        private void SetupWindowStyle()
        {
            Texture2D backgroundTexture = GetOrCacheTexture(_background);

            if (_windowStyle == null)
            {
                _windowStyle = new GUIStyle
                {
                    normal = { background = backgroundTexture, textColor = _textColor },
                    padding = new RectOffset(4, 4, 4, 4),
                    font = _consoleFont,
                };
            }
            else
            {
                _windowStyle.normal.background = backgroundTexture;
                _windowStyle.normal.textColor = _textColor;
                _windowStyle.font = _consoleFont;
            }
        }

        private void SetupLabels()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle
                {
                    font = _consoleFont,
                    normal = { textColor = _textColor },
                    wordWrap = true,
                };
            }
            else
            {
                _labelStyle.font = _consoleFont;
                _labelStyle.normal.textColor = _textColor;
            }
        }

        private void SetupButtons()
        {
            Texture2D backgroundTexture = GetOrCacheTexture(_buttonBackground);

            const int paddingX = 4;
            const int paddingY = 4;
            const int marginX = 4;
            const int marginY = 4;
            _buttonStyle = GenerateGUIStyle(
                _buttonForegroundColor,
                backgroundTexture,
                TextAnchor.MiddleCenter,
                paddingX,
                paddingY,
                marginX,
                marginY
            );

            _runButtonStyle = GenerateGUIStyle(
                _buttonForegroundColor,
                backgroundTexture,
                TextAnchor.MiddleCenter,
                paddingX,
                paddingY,
                marginX,
                marginY: 0
            );
            _runButtonStyle.margin.left = 0;

            GUIContent runContent = new(_runButtonText);
            Vector2 runSize = _runButtonStyle.CalcSize(runContent);
            _buttonTextOptions[_runButtonText] = new[] { GUILayout.Width(runSize.x) };
            _buttonWidths[_runButtonText] = runSize.x;

            GUIContent closeContent = new(_closeButtonText);
            Vector2 closeSize = _buttonStyle.CalcSize(closeContent);
            _buttonTextOptions[_closeButtonText] = new[] { GUILayout.Width(closeSize.x) };
            _buttonWidths[_closeButtonText] = closeSize.x;

            GUIContent smallContent = new(_smallButtonText);
            Vector2 smallSize = _buttonStyle.CalcSize(smallContent);
            _buttonTextOptions[_smallButtonText] = new[] { GUILayout.Width(smallSize.x) };
            _buttonWidths[_smallButtonText] = smallSize.x;

            GUIContent fullContent = new(_fullButtonText);
            Vector2 fullSize = _buttonStyle.CalcSize(fullContent);
            _buttonTextOptions[_fullButtonText] = new[] { GUILayout.Width(fullSize.x) };
            _buttonWidths[_fullButtonText] = fullSize.x;
        }

        private void SetupInput()
        {
            Texture2D inputBackgroundTexture = GetOrCacheTexture(_inputBackground);

            _inputStyle = GenerateGUIStyle(
                _inputTextColor,
                inputBackgroundTexture,
                TextAnchor.MiddleLeft
            );

            if (!string.IsNullOrEmpty(_inputCaret))
            {
                _inputCaretStyle = GenerateGUIStyle(
                    _inputTextColor,
                    inputBackgroundTexture,
                    TextAnchor.MiddleRight
                );
                _inputCaretStyle.padding.right = 0;

                GUIContent inputCaretContent = new(_inputCaret);
                Vector2 size = _inputCaretStyle.CalcSize(inputCaretContent);
                _inputCaretOptions = new[] { GUILayout.Width(size.x) };
            }
            else
            {
                _inputCaretOptions = Array.Empty<GUILayoutOption>();
            }

            Texture2D unselectedHintBackgroundTexture = GetOrCacheTexture(
                _unselectedHintBackground
            );

            _unselectedHintStyle = GenerateGUIStyle(
                _unselectedHintTextColor,
                unselectedHintBackgroundTexture,
                TextAnchor.MiddleCenter,
                paddingX: 4,
                paddingY: 0,
                marginX: 4,
                marginY: 4
            );

            _firstUnselectedHintStyle = GenerateGUIStyle(
                _unselectedHintTextColor,
                unselectedHintBackgroundTexture,
                TextAnchor.MiddleCenter,
                paddingX: 4,
                paddingY: 0,
                marginX: 4,
                marginY: 4
            );
            _firstUnselectedHintStyle.margin.left = 0;

            Texture2D selectedHintBackgroundTexture = GetOrCacheTexture(_selectedHintBackground);

            _selectedHintStyle = GenerateGUIStyle(
                _selectedHintTextColor,
                selectedHintBackgroundTexture,
                TextAnchor.MiddleCenter,
                paddingX: 4,
                paddingY: 0,
                marginX: 4,
                marginY: 4
            );

            _firstSelectedHintStyle = GenerateGUIStyle(
                _selectedHintTextColor,
                selectedHintBackgroundTexture,
                TextAnchor.MiddleCenter,
                paddingX: 4,
                paddingY: 0,
                marginX: 4,
                marginY: 4
            );
            _firstSelectedHintStyle.margin.left = 0;
        }

        private GUIStyle GenerateGUIStyle(
            Color textColor,
            Texture2D texture,
            TextAnchor alignment,
            int paddingX = 4,
            int paddingY = 4,
            int marginX = 0,
            int marginY = 0
        )
        {
            return new GUIStyle
            {
                padding = new RectOffset(paddingX, paddingX, paddingY, paddingY),
                margin = new RectOffset(marginX, marginX, marginY, marginY),
                font = _consoleFont,
                normal = { textColor = textColor, background = texture },
                fixedHeight = _consoleFont.lineHeight + paddingY * 2,
                alignment = alignment,
            };
        }

        // UI Toolkit setup
        private void SetupUI()
        {
            UIDocument uiDoc = GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                // TODO: Handle this better
                uiDoc = gameObject.AddComponent<UIDocument>();
            }
            VisualElement uiRoot = uiDoc.rootVisualElement;
            VisualElement root = new();
            uiRoot.Add(root);
            root.name = "TerminalRoot";
            root.AddToClassList("terminal-root");

            _terminalContainer = new VisualElement();
            _terminalContainer.name = "TerminalContainer";
            _terminalContainer.AddToClassList("terminal-container");
            _terminalContainer.style.height = new StyleLength(_realWindowSize);
            root.Add(_terminalContainer);

            _logScrollView = new ScrollView();
            _logScrollView.name = "LogScrollView";
            _logScrollView.AddToClassList("log-scroll-view");
            _terminalContainer.Add(_logScrollView);

            _autoCompleteContainer = new VisualElement();
            _autoCompleteContainer.name = "AutoCompletePopup";
            _autoCompleteContainer.AddToClassList("autocomplete-popup");
            _terminalContainer.Add(_autoCompleteContainer);

            _inputContainer = new VisualElement();
            _inputContainer.name = "InputContainer";
            _inputContainer.AddToClassList("input-container");
            _terminalContainer.Add(_inputContainer);

            if (
                _showGUIButtons
                && !string.IsNullOrWhiteSpace(_commandText)
                && !string.IsNullOrWhiteSpace(_runButtonText)
            )
            {
                _runButton = new Button(() =>
                {
                    EnterCommand();
                })
                {
                    text = _runButtonText,
                };
                _runButton.name = "RunCommandButton";
                _runButton.AddToClassList("terminal-button-run");
                _inputContainer.Add(_runButton);
            }

            if (!string.IsNullOrEmpty(_inputCaret))
            {
                Label caretLabel = new Label(_inputCaret);
                caretLabel.name = "InputCaret";
                caretLabel.AddToClassList("terminal-input-caret");
                _inputContainer.Add(caretLabel);
            }

            _commandInput = new TextField();
            _commandInput.name = "CommandInput";
            _commandInput.AddToClassList("terminal-input-field");
            _commandInput.pickingMode = PickingMode.Position;
            _commandInput.value = _commandText;
            _commandInput.RegisterCallback<ChangeEvent<string>>(
                evt =>
                {
                    _commandText = evt.newValue;
                    //_needsAutoCompleteReset = true;
                    /*
                        Something is FUCKED here, I can't figure it out. When we have "Make Hints Clickable" on,
                        back-spacing from an auto-completed word highlights the word for a few frames. I think this is
                        an IMGUI bug where the TextEditor state / control focus is lost, I've verified through some
                        debug logs where I track cursor / selection indexes. They get arbitrarily reset to 0.
                        I thought it was due to focusing the control too much, but that doesn't appear to be the case,
                        again, verified through debug logs. Anyway, oh well, maybe I'll fix it later.
                     */
                    //ResetAutoComplete();
                },
                TrickleDown.TrickleDown
            );
            _commandInput.RegisterCallback<KeyDownEvent>(
                evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        EnterCommand();
                        evt.StopPropagation();
                        evt.PreventDefault();
                    }
                },
                TrickleDown.TrickleDown
            );
            _inputContainer.Add(_commandInput);
            _textInput = _commandInput.Q<VisualElement>("unity-text-input");

            _stateButtonContainer = new VisualElement();
            _stateButtonContainer.name = "StateButtonContainer";
            _stateButtonContainer.AddToClassList("state-button-container");
            root.Add(_stateButtonContainer);
            RefreshStateButtons();
        }

        private void RefreshUI()
        {
            if (_terminalContainer == null)
            {
                return;
            }
            _terminalContainer.style.top = _currentOpenT - _realWindowSize;
            _terminalContainer.style.height = _realWindowSize;
            _terminalContainer.style.width = Screen.width;
            RefreshLogs();
            RefreshAutoCompleteHints();
            if (_commandInput.value != _commandText)
            {
                _commandInput.value = _commandText;
            }
            else if (_needsFocus && _textInput.focusable)
            {
                _textInput.schedule.Execute(() => _textInput.Focus()).ExecuteLater(0);
                _textInput.Focus();
                _moveCursor = true;
                _needsFocus = false;
            }
            RefreshStateButtons();
        }

        private void RefreshLogs()
        {
            IReadOnlyList<LogItem> logs = Buffer?.Logs;
            if (logs == null)
            {
                return;
            }
            VisualElement content = _logScrollView.contentContainer;
            if (content.childCount != logs.Count)
            {
                content.Clear();
                foreach (LogItem log in logs)
                {
                    Label logLabel = new Label(log.message);
                    SetupLabel(logLabel, log);
                    content.Add(logLabel);
                }
            }
            else
            {
                for (int i = 0; i < logs.Count; i++)
                {
                    if (content[i] is Label logLabel)
                    {
                        LogItem logItem = logs[i];
                        SetupLabel(logLabel, logItem);
                        logLabel.text = logs[i].message;
                    }
                }
            }
            _logScrollView.scrollOffset = new Vector2(0, int.MaxValue);

            void SetupLabel(Label label, LogItem log)
            {
                label.ClearClassList();
                label.AddToClassList("terminal-output-label"); // Base class
                switch (log.type)
                {
                    case TerminalLogType.ShellMessage:
                    {
                        label.AddToClassList("terminal-output-label--shell"); // Modifier class
                        break;
                    }
                    case TerminalLogType.Exception:
                    case TerminalLogType.Assert:
                    case TerminalLogType.Error:
                    {
                        label.AddToClassList("terminal-output-label--error"); // Modifier class
                        break;
                    }
                    case TerminalLogType.Warning:
                    {
                        label.AddToClassList("terminal-output-label--warning"); // Modifier class
                        break;
                    }
                    case TerminalLogType.Message:
                    {
                        label.AddToClassList("terminal-output-label--message"); // Modifier class
                        break;
                    }
                    case TerminalLogType.Input:
                    {
                        label.AddToClassList("terminal-output-label--input"); // Modifier class
                        break;
                    }
                    default:
                    {
                        throw new InvalidEnumArgumentException(
                            nameof(log.type),
                            (int)log.type,
                            typeof(TerminalLogType)
                        );
                    }
                }
            }
        }

        private void RefreshAutoCompleteHints()
        {
            _autoCompleteContainer.Clear();
            if (
                _lastCompletionBuffer != null
                && _lastCompletionBuffer.Length > 0
                && (
                    _hintDisplayMode == HintDisplayMode.Always
                    || _hintDisplayMode == HintDisplayMode.AutoCompleteOnly
                )
            )
            {
                for (int i = 0; i < _lastCompletionBuffer.Length; i++)
                {
                    string hint = _lastCompletionBuffer[i];
                    VisualElement hintElement;
                    if (_makeHintsClickable)
                    {
                        Button hintButton = new Button(() =>
                        {
                            _commandText = hint;
                            _lastCompletionIndex = i;
                            _moveCursor = true;
                        })
                        {
                            text = hint,
                        };
                        hintElement = hintButton;
                        _autoCompleteContainer.Add(hintButton);
                    }
                    else
                    {
                        Label hintLabel = new Label(hint);
                        hintElement = hintLabel;
                        _autoCompleteContainer.Add(hintLabel);
                    }

                    hintElement.name = $"SuggestionText{i}";
                    hintElement.AddToClassList("autocomplete-item");
                }
            }
        }

        private void RefreshStateButtons()
        {
            _stateButtonContainer.Clear();
            if (!_showGUIButtons)
            {
                return;
            }
            Button firstButton = null;
            Button secondButton = null;
            switch (_state)
            {
                case TerminalState.Closed:
                    if (!string.IsNullOrWhiteSpace(_smallButtonText))
                    {
                        firstButton = new Button(() =>
                        {
                            SetState(TerminalState.OpenSmall);
                        })
                        {
                            text = _smallButtonText,
                        };
                    }
                    if (!string.IsNullOrWhiteSpace(_fullButtonText))
                    {
                        secondButton = new Button(() =>
                        {
                            SetState(TerminalState.OpenFull);
                        })
                        {
                            text = _fullButtonText,
                        };
                    }
                    break;
                case TerminalState.OpenSmall:
                    if (!string.IsNullOrWhiteSpace(_closeButtonText))
                    {
                        firstButton = new Button(() =>
                        {
                            SetState(TerminalState.Closed);
                        })
                        {
                            text = _closeButtonText,
                        };
                    }
                    if (!string.IsNullOrWhiteSpace(_fullButtonText))
                    {
                        secondButton = new Button(() =>
                        {
                            SetState(TerminalState.OpenFull);
                        })
                        {
                            text = _fullButtonText,
                        };
                    }
                    break;
                case TerminalState.OpenFull:
                    if (!string.IsNullOrWhiteSpace(_closeButtonText))
                    {
                        firstButton = new Button(() =>
                        {
                            SetState(TerminalState.Closed);
                        })
                        {
                            text = _closeButtonText,
                        };
                    }
                    if (!string.IsNullOrWhiteSpace(_smallButtonText))
                    {
                        secondButton = new Button(() =>
                        {
                            SetState(TerminalState.OpenSmall);
                        })
                        {
                            text = _smallButtonText,
                        };
                    }
                    break;
                default:
                    throw new InvalidEnumArgumentException(
                        nameof(_state),
                        (int)_state,
                        typeof(TerminalState)
                    );
            }
            if (firstButton != null)
            {
                firstButton.name = "StateButton1";
                firstButton.AddToClassList("terminal-button-toggle");
                _stateButtonContainer.Add(firstButton);
            }
            if (secondButton != null)
            {
                secondButton.name = "StateButton2";
                secondButton.AddToClassList("terminal-button-toggle");
                _stateButtonContainer.Add(secondButton);
            }
            _stateButtonContainer.style.top = _currentOpenT + 4;
        }

#if ENABLE_INPUT_SYSTEM
        private static bool IsKeyPressed(string key)
        {
            if (1 < key.Length && key.StartsWith("#", StringComparison.OrdinalIgnoreCase))
            {
                if (!CachedSubstrings.TryGetValue(key, out string expected))
                {
                    expected = key.Substring(1);
                    if (expected.Length == 1 && expected.NeedsLowerInvariantConversion())
                    {
                        expected = expected.ToLowerInvariant();
                    }

                    CachedSubstrings[key] = expected;
                }

                return Keyboard.current.shiftKey.isPressed
                    && (
                        Keyboard.current.TryGetChildControl<KeyControl>(
                            SpecialKeyCodeMap.GetValueOrDefault(expected, expected)
                        )
                            is { wasPressedThisFrame: true }
                        || Keyboard.current.TryGetChildControl<KeyControl>(expected)
                            is { wasPressedThisFrame: true }
                    );
            }

            const string shiftModifier = "shift+";
            if (
                shiftModifier.Length < key.Length
                && key.StartsWith(shiftModifier, StringComparison.OrdinalIgnoreCase)
            )
            {
                if (!CachedSubstrings.TryGetValue(key, out string expected))
                {
                    expected = key.Substring(shiftModifier.Length);
                    if (expected.Length == 1 && expected.NeedsLowerInvariantConversion())
                    {
                        expected = expected.ToLowerInvariant();
                    }

                    CachedSubstrings[key] = expected;
                }

                return Keyboard.current.shiftKey.isPressed
                    && (
                        Keyboard.current.TryGetChildControl<KeyControl>(
                            SpecialKeyCodeMap.GetValueOrDefault(expected, expected)
                        )
                            is { wasPressedThisFrame: true }
                        || Keyboard.current.TryGetChildControl<KeyControl>(expected)
                            is { wasPressedThisFrame: true }
                    );
            }
            else if (
                SpecialShiftedKeyCodeMap.TryGetValue(key, out string expected)
                && Keyboard.current.shiftKey.isPressed
                && Keyboard.current.TryGetChildControl<KeyControl>(expected)
                    is { wasPressedThisFrame: true }
            )
            {
                return true;
            }
            else if (
                AlternativeSpecialShiftedKeyCodeMap.TryGetValue(key, out expected)
                && Keyboard.current.shiftKey.isPressed
                && Keyboard.current.TryGetChildControl<KeyControl>(expected)
                    is { wasPressedThisFrame: true }
            )
            {
                return true;
            }
            else if (key.Length == 1 && key.NeedsLowerInvariantConversion())
            {
                key = key.ToLowerInvariant();
                return Keyboard.current.shiftKey.isPressed
                    && Keyboard.current.TryGetChildControl<KeyControl>(key)
                        is { wasPressedThisFrame: true };
            }
            else
            {
                return Keyboard.current.TryGetChildControl<KeyControl>(
                        SpecialKeyCodeMap.GetValueOrDefault(key, key)
                    )
                        is { wasPressedThisFrame: true }
                    || Keyboard.current.TryGetChildControl<KeyControl>(key)
                        is { wasPressedThisFrame: true };
            }
        }

        public void OnHandlePrevious(InputValue inputValue)
        {
            HandlePrevious();
        }

        public void OnHandleNext(InputValue inputValue)
        {
            HandleNext();
        }

        public void OnClose(InputValue inputValue)
        {
            Close();
        }

        public void OnToggleSmall(InputValue inputValue)
        {
            ToggleSmall();
        }

        public void OnToggleFull(InputValue inputValue)
        {
            ToggleFull();
        }

        public void OnCompleteCommand(InputValue input)
        {
            CompleteCommand(searchForward: true);
        }

        public void OnReverseCompleteCommand(InputValue input)
        {
            CompleteCommand(searchForward: false);
        }

        public void OnEnterCommand(InputValue inputValue)
        {
            EnterCommand();
        }
#endif

        public void HandlePrevious()
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }
            _commandText = History?.Previous(_skipSameCommandsInHistory) ?? string.Empty;
            ResetAutoComplete();
            _moveCursor = true;
            _needsFocus = true;
        }

        public void HandleNext()
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }
            _commandText = History?.Next(_skipSameCommandsInHistory) ?? string.Empty;
            ResetAutoComplete();
            _moveCursor = true;
            _needsFocus = true;
        }

        public void Close()
        {
            SetState(TerminalState.Closed);
        }

        public void ToggleSmall()
        {
            ToggleState(TerminalState.OpenSmall);
        }

        public void ToggleFull()
        {
            ToggleState(TerminalState.OpenFull);
        }

        public void EnterCommand()
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }

            _commandText = _commandText.Trim();
            try
            {
                if (string.IsNullOrWhiteSpace(_commandText))
                {
                    return;
                }

                Log(TerminalLogType.Input, _commandText);
                Shell?.RunCommand(_commandText);
                while (Shell?.TryConsumeErrorMessage(out string error) == true)
                {
                    Log(TerminalLogType.Error, $"Error: {error}");
                }

                _commandText = string.Empty;
                _scrollPosition.y = int.MaxValue;
                _needsFocus = true;
            }
            finally
            {
                ResetAutoComplete();
            }
        }

        public void CompleteCommand(bool searchForward = true)
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }

            try
            {
                _lastKnownCommandText ??= _commandText;
                string[] completionBuffer =
                    AutoComplete?.Complete(_lastKnownCommandText) ?? Array.Empty<string>();
                try
                {
                    int completionLength = completionBuffer.Length;
                    if (
                        _lastCompletionBuffer.SequenceEqual(
                            completionBuffer,
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                    {
                        if (0 < completionLength)
                        {
                            if (_lastCompletionIndex == null)
                            {
                                _lastCompletionIndex = 0;
                            }
                            else if (searchForward)
                            {
                                _lastCompletionIndex =
                                    (_lastCompletionIndex + 1) % completionLength;
                            }
                            else
                            {
                                _lastCompletionIndex =
                                    (_lastCompletionIndex - 1 + completionLength)
                                    % completionLength;
                            }

                            _commandText = completionBuffer[_lastCompletionIndex.Value];
                        }
                        else
                        {
                            _lastCompletionIndex = null;
                        }
                    }
                    else
                    {
                        if (0 < completionLength)
                        {
                            _lastCompletionIndex = 0;
                            _commandText = completionBuffer[0];
                        }
                        else
                        {
                            _lastCompletionIndex = null;
                        }
                    }
                }
                finally
                {
                    _lastCompletionBuffer = completionBuffer;
                    CalculateAutoCompleteHintSize();
                }
            }
            finally
            {
                _moveCursor = true;
                _needsFocus = true;
            }
        }

        private void HandleOpenness()
        {
            float dt = _toggleSpeed * Time.unscaledDeltaTime;

            if (_currentOpenT < _openTarget)
            {
                _currentOpenT = Mathf.Min(_currentOpenT + dt, _openTarget);
            }
            else if (_openTarget < _currentOpenT)
            {
                _currentOpenT = Mathf.Max(_currentOpenT - dt, _openTarget);
            }
            else
            {
                _inputFix = false;
                return;
            }

            float realWindowSize =
                _state == TerminalState.OpenSmall
                    ? Mathf.Max(_currentOpenT, _realWindowSize)
                    : _realWindowSize;
            _window = new Rect(0, _currentOpenT - realWindowSize, Screen.width, realWindowSize);
        }

        private void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            bool? handled = Buffer?.HandleLog(message, stackTrace, (TerminalLogType)type);
            if (handled == true)
            {
                _scrollPosition.y = int.MaxValue;
            }
        }

        private Color GetLogColor(TerminalLogType type)
        {
            return type switch
            {
                TerminalLogType.Message => _textColor,
                TerminalLogType.Warning => _warningColor,
                TerminalLogType.Input => _inputTextColor,
                TerminalLogType.ShellMessage => _shellMessageColor,
                _ => _errorColor,
            };
        }

        private static Texture2D GetOrCacheTexture(Color color)
        {
            if (CachedTextures.TryGetValue(color, out Texture2D texture))
            {
                return texture;
            }

            texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            CachedTextures[color] = texture;
            return texture;
        }
    }
}
