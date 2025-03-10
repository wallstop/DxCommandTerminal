namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using Attributes;
    using JetBrains.Annotations;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Assertions;
    using Utils;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;
#endif

    public enum TerminalState
    {
        Unknown = 0,
        Closed = 1,
        OpenSmall = 2,
        OpenFull = 3,
    }

    [DisallowMultipleComponent]
    public sealed class Terminal : MonoBehaviour
    {
        private const string CommandControlName = "command_text_field";

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

        public static Terminal Instance { get; private set; }

        public static CommandLog Buffer { get; private set; }
        public static CommandShell Shell { get; private set; }

        public static CommandHistory History { get; private set; }

        // ReSharper disable once MemberCanBePrivate.Global
        public static CommandAutoComplete AutoComplete { get; private set; }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool IsClosed =>
            _state == TerminalState.Closed && Mathf.Approximately(_currentOpenT, _openTarget);

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
        private int _bufferSize = 512;

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

        [SerializeField]
        private bool _showGUIButtons;

        [SerializeField]
        private bool _rightAlignButtons;

        [Header("Hints")]
        [SerializeField]
        private bool _displayHints;

        [Range(0, 1)]
        [SerializeField]
        private float _unselectedHintContrast;

        [Range(0, 1)]
        [SerializeField]
        private float _selectedHintContrast;

        [SerializeField]
        private float _unselectedHintAlpha = 0.25f;

        [SerializeField]
        private float _selectedHintAlpha = 0.75f;

        [SerializeField]
        private Color _unselectedHintColor = Color.grey;

        [SerializeField]
        private Color _selectedHintColor = Color.white;

        [Header("Theme")]
        [Range(0, 1)]
        [SerializeField]
        private float _inputContrast;

        [Range(0, 1)]
        [SerializeField]
        private float _inputAlpha = 0.5f;

        [SerializeField]
        private Color _backgroundColor = Color.black;

        [SerializeField]
        private Color _foregroundColor = Color.white;

        [SerializeField]
        private Color _shellColor = Color.white;

        [SerializeField]
        private Color _inputColor = Color.cyan;

        [SerializeField]
        private Color _warningColor = Color.yellow;

        [SerializeField]
        private Color _errorColor = Color.red;

        [Header("System")]
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
#endif
        private TerminalState _state;
        private TextEditor _editorState;
#if !ENABLE_INPUT_SYSTEM
        private bool _inputFix;
#endif
        private bool _moveCursor;
        private Rect _window;
        private float _currentOpenT;
        private float _openTarget;
        private float _realWindowSize;
        private string _commandText;
#if !ENABLE_INPUT_SYSTEM
        private string _cachedCommandText;
#endif
        private Vector2 _scrollPosition;
        private GUIStyle _windowStyle;
        private GUIStyle _labelStyle;

        private GUIStyle _inputCaretStyle;
        private GUIStyle _unselectedHintStyle;
        private GUIStyle _selectedHintStyle;
        private GUIStyle _inputStyle;
        private GUILayoutOption[] _inputCaretOptions;
        private GUILayoutOption[] _runButtonOptions;
        private bool _unityLogAttached;
        private bool _started;

        private int? _lastWidth;
        private int? _lastHeight;
        private bool _handledInputThisFrame;
        private bool _needsFocus;

        private string _lastKnownCommandText;
        private string[] _lastCompletionBuffer = Array.Empty<string>();
        private float[] _completionElementWidthBuffer = Array.Empty<float>();
        private GUILayoutOption[][] _completionElementStyles = Array.Empty<GUILayoutOption[]>();
        private readonly List<int> _completionIndexWindow = new();
        private int? _lastCompletionIndex;
        private int? _previousLastCompletionIndex;
        private int _currentHintStartIndex;

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
            switch (_bufferSize)
            {
                case <= 0:
                    Debug.LogError(
                        $"Invalid buffer size '{_bufferSize}', must be greater than zero. Defaulting to 0 (empty buffer).",
                        this
                    );
                    break;
                case < 10:
                    Debug.LogWarning(
                        $"Unsupported buffer size '{_bufferSize}', recommended size is > 10.",
                        this
                    );
                    break;
            }

            Instance = this;
            Buffer = new CommandLog(Math.Max(0, _bufferSize), _ignoredLogTypes);
            History = new CommandHistory();
            Shell = new CommandShell(History);
            AutoComplete = new CommandAutoComplete(History, Shell);

            Shell.RegisterCommands(
                ignoredCommands: disabledCommands,
                ignoreDefaultCommands: ignoreDefaultCommands
            );
        }

        private void OnEnable()
        {
            if (_logUnityMessages && !_unityLogAttached)
            {
                _unityLogAttached = true;
                Application.logMessageReceivedThreaded += HandleUnityLog;
            }

            ConsumeAndLogErrors();
        }

        private void OnDisable()
        {
            if (_unityLogAttached)
            {
                Application.logMessageReceivedThreaded -= HandleUnityLog;
                _unityLogAttached = false;
            }

            SetState(TerminalState.Closed);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;
            Buffer = null;
            Shell = null;
            History = null;
            AutoComplete = null;
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

            _commandText = string.Empty;
            ResetAutoComplete();
#if !ENABLE_INPUT_SYSTEM
            _cachedCommandText = _commandText;
#endif
            if (_useHotkeys)
            {
                Assert.IsFalse(
                    _completeCommandHotkeys?.list?.Exists(command =>
                        string.Equals(command, _toggleHotkey, StringComparison.OrdinalIgnoreCase)
                    ) ?? false,
                    $"Invalid Toggle Hotkey {_toggleHotkey} - cannot be in the set of complete command "
                        + $"hotkeys: [{string.Join(",", _completeCommandHotkeys?.list ?? Enumerable.Empty<string>())}]"
                );
            }

            SetupWindow();
            SetupWindowStyle();
            SetupInput();
            SetupLabels();
            ConsumeAndLogErrors();

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

            if (Application.isPlaying && enabled && gameObject.activeInHierarchy)
            {
                OnDisable();
                OnEnable();
                Start();
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
                Close();
                _handledInputThisFrame = true;
            }
            else if (_completeCommandHotkeys?.list?.Exists(IsKeyPressed) == true)
            {
                EnterCommand();
                _handledInputThisFrame = true;
            }
            else if (IsKeyPressed(_previousHotkey))
            {
                HandlePrevious();
                _handledInputThisFrame = true;
            }
            else if (IsKeyPressed(_nextHotkey))
            {
                HandleNext();
                _handledInputThisFrame = true;
            }
            else if (IsKeyPressed(_toggleFullHotkey))
            {
                ToggleFull();
                _handledInputThisFrame = true;
            }
            else if (IsKeyPressed(_toggleHotkey))
            {
                ToggleSmall();
                _handledInputThisFrame = true;
            }
            else if (IsKeyPressed(_reverseCompleteHotkey))
            {
                CompleteCommand(searchForward: false);
                _handledInputThisFrame = true;
            }
            else if (IsKeyPressed(_completeHotkey))
            {
                CompleteCommand(searchForward: true);
                _handledInputThisFrame = true;
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
        }

        private void OnGUI()
        {
#if !ENABLE_INPUT_SYSTEM
            if (Event.current.Equals(Event.KeyboardEvent(_toggleHotkey)))
            {
                ToggleSmall();
            }
            else if (Event.current.Equals(Event.KeyboardEvent(_toggleFullHotkey)))
            {
                ToggleFull();
            }
#endif
            if (_showGUIButtons)
            {
                DrawGUIButtons();
            }

            if (IsClosed)
            {
                return;
            }

            HandleOpenness();
            _window = GUILayout.Window(88, _window, DrawConsole, string.Empty, _windowStyle);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public void ToggleState(TerminalState newState)
        {
            SetState(_state == newState ? TerminalState.Closed : newState);
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public void SetState(TerminalState newState)
        {
#if !ENABLE_INPUT_SYSTEM
            _inputFix = true;
#endif
            if (newState != TerminalState.Closed)
            {
                _needsFocus = true;
            }
#if !ENABLE_INPUT_SYSTEM
            _cachedCommandText = _commandText;
#endif
            _commandText = string.Empty;
            ResetAutoComplete();

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
                    if (_currentOpenT > _openTarget)
                    {
                        // Prevent resizing from OpenFull to OpenSmall if window y position
                        // is greater than OpenSmall's target
                        _openTarget = 0;
                        _state = TerminalState.Closed;
                        return;
                    }
                    _realWindowSize = _openTarget;
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

        private void ConsumeAndLogErrors()
        {
            while (Shell.TryConsumeErrorMessage(out string error))
            {
                Log(TerminalLogType.Error, $"Error: {error}");
            }
        }

        private void ResetAutoComplete()
        {
            _lastKnownCommandText = _commandText;
            _lastCompletionIndex = null;
            _currentHintStartIndex = 0;
            if (_displayHints)
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

            _realWindowSize = height * _maxHeight * _smallTerminalRatio;

            try
            {
                switch (_state)
                {
                    case TerminalState.OpenSmall:
                    {
                        _openTarget = height * _maxHeight * _smallTerminalRatio;
                        _realWindowSize = _openTarget;
                        _scrollPosition.y = int.MaxValue;
                        break;
                    }
                    case TerminalState.OpenFull:
                    {
                        _realWindowSize = height * _maxHeight;
                        _openTarget = _realWindowSize;
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

            _runButtonOptions = _showGUIButtons
                ? new[] { GUILayout.Width(Screen.width / 10f) }
                : Array.Empty<GUILayoutOption>();
        }

        private void SetupWindowStyle()
        {
            Texture2D backgroundTexture = new(1, 1);
            backgroundTexture.SetPixel(0, 0, _backgroundColor);
            backgroundTexture.Apply();

            _windowStyle = new GUIStyle
            {
                normal = { background = backgroundTexture, textColor = _foregroundColor },
                padding = new RectOffset(4, 4, 4, 4),
                font = _consoleFont,
            };
        }

        private void SetupLabels()
        {
            _labelStyle = new GUIStyle
            {
                font = _consoleFont,
                normal = { textColor = _foregroundColor },
                wordWrap = true,
            };
        }

        private void SetupInput()
        {
            Color darkBackground = new()
            {
                r = _backgroundColor.r - _inputContrast,
                g = _backgroundColor.g - _inputContrast,
                b = _backgroundColor.b - _inputContrast,
                a = _inputAlpha,
            };

            Texture2D inputBackgroundTexture = new(1, 1);
            inputBackgroundTexture.SetPixel(0, 0, darkBackground);
            inputBackgroundTexture.Apply();

            _inputStyle = GenerateGUIStyle(
                _inputColor,
                inputBackgroundTexture,
                TextAnchor.MiddleLeft
            );

            if (!string.IsNullOrEmpty(_inputCaret))
            {
                _inputCaretStyle = GenerateGUIStyle(
                    _inputColor,
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

            Color unselectedHintBackground = new()
            {
                r = _backgroundColor.r - _unselectedHintContrast,
                g = _backgroundColor.g - _unselectedHintContrast,
                b = _backgroundColor.b - _unselectedHintContrast,
                a = _unselectedHintAlpha,
            };

            Texture2D unselectedHintBackgroundTexture = new(1, 1);
            unselectedHintBackgroundTexture.SetPixel(0, 0, unselectedHintBackground);
            unselectedHintBackgroundTexture.Apply();

            _unselectedHintStyle = GenerateGUIStyle(
                _unselectedHintColor,
                unselectedHintBackgroundTexture,
                TextAnchor.MiddleCenter,
                paddingX: 4,
                paddingY: 0,
                marginX: 4,
                marginY: 4
            );

            Color selectedHintBackground = new()
            {
                r = _backgroundColor.r - _selectedHintContrast,
                g = _backgroundColor.g - _selectedHintContrast,
                b = _backgroundColor.b - _selectedHintContrast,
                a = _selectedHintAlpha,
            };

            Texture2D selectedHintBackgroundTexture = new(1, 1);
            selectedHintBackgroundTexture.SetPixel(0, 0, selectedHintBackground);
            selectedHintBackgroundTexture.Apply();

            _selectedHintStyle = GenerateGUIStyle(
                _selectedHintColor,
                selectedHintBackgroundTexture,
                TextAnchor.MiddleCenter,
                paddingX: 4,
                paddingY: 0,
                marginX: 4,
                marginY: 4
            );

            return;

            GUIStyle GenerateGUIStyle(
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
        }

        private void DrawConsole(int window2D)
        {
            GUILayout.BeginVertical();
            try
            {
                _scrollPosition = GUILayout.BeginScrollView(
                    _scrollPosition,
                    false,
                    false,
                    GUIStyle.none,
                    GUIStyle.none
                );
                try
                {
                    GUILayout.FlexibleSpace();
                    DrawLogs();
                }
                finally
                {
                    GUILayout.EndScrollView();
                }

#if !ENABLE_INPUT_SYSTEM
                if (Event.current.Equals(Event.KeyboardEvent(_closeHotkey)))
                {
                    Close();
                }
                else if (
                    _completeCommandHotkeys?.Exists(command =>
                        Event.current.Equals(Event.KeyboardEvent(command))
                    ) == true
                )
                {
                    EnterCommand();
                }
                else if (Event.current.Equals(Event.KeyboardEvent(_previousHotkey)))
                {
                    HandlePrevious();
                }
                else if (Event.current.Equals(Event.KeyboardEvent(_nextHotkey)))
                {
                    HandleNext();
                }
                else if (Event.current.Equals(Event.KeyboardEvent(_toggleHotkey)))
                {
                    OpenSmall();
                }
                else if (Event.current.Equals(Event.KeyboardEvent(_toggleFullHotkey)))
                {
                    OpenFull();
                }
                else if (Event.current.Equals(Event.KeyboardEvent(_completeHotkey)))
                {
                    CompleteCommand();
                }
#endif
                if (_lastCompletionBuffer is { Length: > 0 })
                {
                    RenderCompletionHints();
                }

                GUILayout.BeginHorizontal();
                try
                {
                    if (!string.IsNullOrEmpty(_inputCaret))
                    {
                        GUILayout.Label(_inputCaret, _inputCaretStyle, _inputCaretOptions);
                    }

                    GUI.SetNextControlName(CommandControlName);
                    string newCommandText = GUILayout.TextField(_commandText, _inputStyle);
                    if (!string.Equals(newCommandText, _commandText))
                    {
                        _commandText = newCommandText;
                        ResetAutoComplete();
                    }

                    if (
                        _moveCursor
                        && Event.current.type == EventType.Repaint
                        && string.Equals(GUI.GetNameOfFocusedControl(), CommandControlName)
                    )
                    {
                        if (
                            GUIUtility.GetStateObject(
                                typeof(TextEditor),
                                GUIUtility.keyboardControl
                            )
                            is TextEditor textEditor
                        )
                        {
                            int textLength = _commandText.Length;
                            textEditor.cursorIndex = textLength;
                            textEditor.selectIndex = textLength;
                        }

                        _moveCursor = false;
                    }

#if !ENABLE_INPUT_SYSTEM
                    if (_inputFix && _commandText.Length > 0)
                    {
                        _commandText = _cachedCommandText; // Otherwise the TextField picks up the ToggleHotkey character event
                        ResetAutoComplete();
                        _inputFix = false; // Prevents checking string Length every draw call
                    }
#endif
                    if (_needsFocus && Event.current.type == EventType.Repaint)
                    {
                        GUI.FocusControl(CommandControlName);
                        _needsFocus = false;
                    }

                    if (
                        _showGUIButtons && GUILayout.Button("| run", _inputStyle, _runButtonOptions)
                    )
                    {
                        EnterCommand();
                    }
                }
                finally
                {
                    GUILayout.EndHorizontal();
                }
            }
            finally
            {
                GUILayout.EndVertical();
            }
        }

        private void RenderCompletionHints()
        {
            GUILayout.BeginHorizontal();
            try
            {
                float availableWidth = Screen.width;
                float totalWidth = _completionElementWidthBuffer.Sum();
                int length = _completionElementWidthBuffer.Length;
                if (
                    0 < availableWidth
                    && availableWidth < totalWidth
                    && _lastCompletionIndex != null
                )
                {
                    int selected = _lastCompletionIndex.Value;
                    bool forward = true;
                    if (_previousLastCompletionIndex != null)
                    {
                        int prev = _previousLastCompletionIndex.Value;
                        forward = (selected == (prev + 1) % length);
                    }

                    float accumulation = 0f;
                    int index = _currentHintStartIndex;
                    _completionIndexWindow.Clear();
                    int count = 0;
                    while (count < length && accumulation < availableWidth)
                    {
                        float width = _completionElementWidthBuffer[index];
                        if (accumulation + width > availableWidth)
                        {
                            break;
                        }

                        _completionIndexWindow.Add(index);
                        accumulation += width;
                        index = (index + 1) % length;
                        count++;
                    }

                    int position = _completionIndexWindow.IndexOf(selected);
                    bool needsRecalculation = position < 0;
                    if (!needsRecalculation)
                    {
                        float offset = 0f;
                        for (int i = 0; i < position; i++)
                        {
                            offset += _completionElementWidthBuffer[_completionIndexWindow[i]];
                        }

                        if (
                            offset < 0f
                            || offset + _completionElementWidthBuffer[selected] > availableWidth
                        )
                        {
                            needsRecalculation = true;
                        }
                    }

                    if (needsRecalculation)
                    {
                        if (forward)
                        {
                            _currentHintStartIndex = selected;
                        }
                        else
                        {
                            float sum = _completionElementWidthBuffer[selected];
                            int start = selected;
                            do
                            {
                                int previous = (start - 1 + length) % length;
                                if (sum + _completionElementWidthBuffer[previous] > availableWidth)
                                {
                                    break;
                                }

                                sum += _completionElementWidthBuffer[previous];
                                start = previous;
                                if (start == selected)
                                {
                                    break;
                                }
                            } while (true);

                            _currentHintStartIndex = start;
                        }
                    }
                }

                for (int i = _currentHintStartIndex; i < _lastCompletionBuffer.Length; ++i)
                {
                    GUILayout.Label(
                        _lastCompletionBuffer[i],
                        _lastCompletionIndex == i ? _selectedHintStyle : _unselectedHintStyle,
                        _completionElementStyles[i]
                    );
                }

                for (int i = 0; i < _currentHintStartIndex && i < _lastCompletionBuffer.Length; ++i)
                {
                    GUILayout.Label(
                        _lastCompletionBuffer[i],
                        _lastCompletionIndex == i ? _selectedHintStyle : _unselectedHintStyle,
                        _completionElementStyles[i]
                    );
                }
            }
            finally
            {
                GUILayout.EndHorizontal();
                _previousLastCompletionIndex = _lastCompletionIndex;
            }
        }

#if ENABLE_INPUT_SYSTEM
        private static bool IsKeyPressed(string key)
        {
            if (1 < key.Length && key.StartsWith("#", StringComparison.OrdinalIgnoreCase))
            {
                string expected = key.Substring(1);
                if (expected.Length == 1)
                {
                    expected = expected.ToLowerInvariant();
                }

                return Keyboard.current.shiftKey.isPressed
                    && Keyboard.current.TryGetChildControl<KeyControl>(
                        SpecialKeyCodeMap.GetValueOrDefault(expected, expected)
                    )
                        is { wasPressedThisFrame: true };
            }
            if (key.StartsWith("shift+", StringComparison.OrdinalIgnoreCase))
            {
                string expected = key.Substring("shift+".Length);
                if (expected.Length == 1)
                {
                    expected = expected.ToLowerInvariant();
                }
                return Keyboard.current.shiftKey.isPressed
                    && Keyboard.current.TryGetChildControl<KeyControl>(
                        SpecialKeyCodeMap.GetValueOrDefault(expected, expected)
                    )
                        is { wasPressedThisFrame: true };
            }
            else if (SpecialShiftedKeyCodeMap.TryGetValue(key, out string expected))
            {
                return Keyboard.current.shiftKey.isPressed
                    && Keyboard.current.TryGetChildControl<KeyControl>(expected)
                        is { wasPressedThisFrame: true };
            }
            else if (key.Length == 1 && key.ToLowerInvariant() != key)
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
            _commandText = History?.Previous() ?? string.Empty;
            ResetAutoComplete();
            _moveCursor = true;
            GUI.FocusControl(CommandControlName);
        }

        public void HandleNext()
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }
            _commandText = History?.Next() ?? string.Empty;
            ResetAutoComplete();
            _moveCursor = true;
            GUI.FocusControl(CommandControlName);
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
                        _lastCompletionBuffer == null
                        || _lastCompletionBuffer.SequenceEqual(
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
                GUI.FocusControl(CommandControlName);
            }
        }

        private void DrawLogs()
        {
            IReadOnlyList<LogItem> logs = Buffer?.Logs;
            if (logs == null)
            {
                return;
            }

            foreach (LogItem log in logs)
            {
                _labelStyle.normal.textColor = GetLogColor(log.type);
                GUILayout.Label(log.message, _labelStyle);
            }
        }

        private void DrawGUIButtons()
        {
            int size = _consoleFont.fontSize;
            float xPosition = _rightAlignButtons ? Screen.width - 7 * size : 0;

            /*
                7 is the number of chars in the button plus some padding, 2 is the line height.
                The layout will resize according to the font size.
             */
            GUILayout.BeginArea(new Rect(xPosition, _currentOpenT, 7 * size, size * 2));
            try
            {
                GUILayout.BeginHorizontal();
                try
                {
                    if (GUILayout.Button("Small", _windowStyle))
                    {
                        ToggleState(TerminalState.OpenSmall);
                    }
                    else if (GUILayout.Button("Full", _windowStyle))
                    {
                        ToggleState(TerminalState.OpenFull);
                    }
                }
                finally
                {
                    GUILayout.EndHorizontal();
                }
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        private void HandleOpenness()
        {
            float dt = _toggleSpeed * Time.unscaledDeltaTime;

            if (_currentOpenT < _openTarget)
            {
                _currentOpenT += dt;
                if (_currentOpenT > _openTarget)
                {
                    _currentOpenT = _openTarget;
                }
            }
            else if (_currentOpenT > _openTarget)
            {
                _currentOpenT -= dt;
                if (_currentOpenT < _openTarget)
                {
                    _currentOpenT = _openTarget;
                }
            }
            else
            {
#if !ENABLE_INPUT_SYSTEM
                _inputFix = false;
#endif
                return; // Already at target
            }

            _window = new Rect(0, _currentOpenT - _realWindowSize, Screen.width, _realWindowSize);
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
                TerminalLogType.Message => _foregroundColor,
                TerminalLogType.Warning => _warningColor,
                TerminalLogType.Input => _inputColor,
                TerminalLogType.ShellMessage => _shellColor,
                _ => _errorColor,
            };
        }
    }
}
