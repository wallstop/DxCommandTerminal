using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WallstopStudios.DxCommandTerminal.Editor")]

namespace CommandTerminal.UIToolkit
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using Attributes;
    using CommandTerminal;
    using Extensions;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Utils;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
#endif

    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerminalThemeSwitcher))]
    public sealed class UIToolkitTerminal : MonoBehaviour
    {
        private enum ScrollBarCaptureState
        {
            None = 0,
            DraggerActive = 1,
            TrackerActive = 2,
        }

        // Cache log callback to reduce allocations
        private static readonly Application.LogCallback UnityLogCallback = HandleUnityLog;

        public static UIToolkitTerminal Instance { get; private set; }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool IsClosed =>
            _state != TerminalState.OpenFull
            && _state != TerminalState.OpenSmall
            && Mathf.Approximately(_currentWindowHeight, _targetWindowHeight);

        [Header("Absolutely Required")]
        [SerializeField]
        private UIDocument _uiDocument;

        [Header("Window")]
        [Range(0, 1)]
        [SerializeField]
        private float _maxHeight = 0.7f;

        [SerializeField]
        [Range(0, 1)]
        private float _smallTerminalRatio = 0.4714285f;

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
        internal Font _consoleFont;

        [SerializeField]
        private string _inputCaret = ">";

        [Header("Buttons")]
        [SerializeField]
        private bool _showGUIButtons;

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
        private readonly List<SerializedProperty> _uiProperties = new();
        private readonly List<SerializedProperty> _fontProperties = new();
        private readonly List<SerializedProperty> _staticStateProperties = new();
        private readonly List<SerializedProperty> _windowProperties = new();
        private readonly List<SerializedProperty> _logUnityMessageProperties = new();
        private readonly List<SerializedProperty> _autoCompleteProperties = new();
        private SerializedObject _serializedObject;
#endif

        private TerminalState _state = TerminalState.Closed;
        private float _currentWindowHeight;
        private float _targetWindowHeight;
        private float _realWindowHeight;
        private string _commandText = string.Empty;
        private bool _unityLogAttached;
        private bool _started;
        private int? _lastWidth;
        private int? _lastHeight;
        private bool _handledInputThisFrame;
        private bool _needsFocus;
        private bool _needsScrollToEnd;
        private bool _needsAutoCompleteReset;
        private long? _lastSeenBufferVersion;
        private string _lastKnownCommandText;
        private string[] _lastCompletionBuffer = Array.Empty<string>();
        private int? _lastCompletionIndex;
        private int? _previousLastCompletionIndex;
        private string _focusedControl;
        private bool _isCommandFromCode;
        private bool _initialResetStateOnInit;

        private VisualElement _terminalContainer;
        private ScrollView _logScrollView;
        private ScrollView _autoCompleteContainer;
        private VisualElement _inputContainer;
        private TextField _commandInput;
        private Button _runButton;
        private VisualElement _stateButtonContainer;
        private VisualElement _textInput;
        private Label _inputCaretLabel;

        private float _inputContainerHeight;
        private float _commandInputHeight;

        private readonly List<VisualElement> _autoCompleteChildren = new();
        private readonly Action _focusInput;

        public UIToolkitTerminal()
        {
            _focusInput = FocusInput;
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

            string[] uiPropertiesTracked = { nameof(_uiDocument) };
            TrackProperties(uiPropertiesTracked, _uiProperties);

            string[] fontPropertiesTracked = { nameof(_consoleFont) };
            TrackProperties(fontPropertiesTracked, _fontProperties);

            string[] staticStaticPropertiesTracked =
            {
                nameof(_logBufferSize),
                nameof(_historyBufferSize),
                nameof(_ignoredLogTypes),
                nameof(disabledCommands),
                nameof(ignoreDefaultCommands),
            };
            TrackProperties(staticStaticPropertiesTracked, _staticStateProperties);

            string[] windowPropertiesTracked = { nameof(_maxHeight), nameof(_smallTerminalRatio) };
            TrackProperties(windowPropertiesTracked, _windowProperties);

            string[] logUnityMessagePropertiesTracked = { nameof(_logUnityMessages) };
            TrackProperties(logUnityMessagePropertiesTracked, _logUnityMessageProperties);

            string[] autoCompletePropertiesTracked =
            {
                nameof(_hintDisplayMode),
                nameof(disabledCommands),
                nameof(ignoreDefaultCommands),
                nameof(_makeHintsClickable),
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
                Application.logMessageReceivedThreaded += UnityLogCallback;
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
                Application.logMessageReceivedThreaded -= UnityLogCallback;
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
                Debug.LogWarning(
                    "Command Console Warning: Please assign a font. Defaulting to Courier New",
                    this
                );
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

            if (_uiDocument == null)
            {
                anyChanged = TryGetComponent(out _uiDocument);
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

            if (InputHelpers.IsKeyPressed(_closeHotkey))
            {
                _handledInputThisFrame = true;
                Close();
            }
            else if (
                // ReSharper disable once ConvertClosureToMethodGroup
                _completeCommandHotkeys?.list?.Exists(key => InputHelpers.IsKeyPressed(key)) == true
            )
            {
                _handledInputThisFrame = true;
                EnterCommand();
            }
            else if (InputHelpers.IsKeyPressed(_previousHotkey))
            {
                _handledInputThisFrame = true;
                HandlePrevious();
            }
            else if (InputHelpers.IsKeyPressed(_nextHotkey))
            {
                _handledInputThisFrame = true;
                HandleNext();
            }
            else if (InputHelpers.IsKeyPressed(_toggleFullHotkey))
            {
                _handledInputThisFrame = true;
                ToggleFull();
            }
            else if (InputHelpers.IsKeyPressed(_toggleHotkey))
            {
                _handledInputThisFrame = true;
                ToggleSmall();
            }
            else if (InputHelpers.IsKeyPressed(_reverseCompleteHotkey))
            {
                _handledInputThisFrame = true;
                CompleteCommand(searchForward: false);
            }
            else if (InputHelpers.IsKeyPressed(_completeHotkey))
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
            int logBufferSize = Mathf.Max(0, _logBufferSize);
            if (force || Terminal.Buffer == null)
            {
                Terminal.Buffer = new CommandLog(logBufferSize, _ignoredLogTypes);
            }
            else
            {
                if (Terminal.Buffer.Capacity != logBufferSize)
                {
                    Terminal.Buffer.Resize(logBufferSize);
                }
                if (
                    !Terminal.Buffer.ignoredLogTypes.SetEquals(
                        _ignoredLogTypes ?? Enumerable.Empty<TerminalLogType>()
                    )
                )
                {
                    Terminal.Buffer.ignoredLogTypes.Clear();
                    Terminal.Buffer.ignoredLogTypes.UnionWith(
                        _ignoredLogTypes ?? Enumerable.Empty<TerminalLogType>()
                    );
                }
            }

            int historyBufferSize = Mathf.Max(0, _historyBufferSize);
            if (force || Terminal.History == null)
            {
                Terminal.History = new CommandHistory(historyBufferSize);
            }
            else if (Terminal.History.Capacity != historyBufferSize)
            {
                Terminal.History.Resize(historyBufferSize);
            }

            if (force || Terminal.Shell == null)
            {
                Terminal.Shell = new CommandShell(Terminal.History);
            }

            if (force || Terminal.AutoComplete == null)
            {
                Terminal.AutoComplete = new CommandAutoComplete(Terminal.History, Terminal.Shell);
            }

            if (
                Terminal.Shell.IgnoringDefaultCommands != ignoreDefaultCommands
                || Terminal.Shell.Commands.Count <= 0
                || !Terminal.Shell.IgnoredCommands.SetEquals(
                    disabledCommands ?? Enumerable.Empty<string>()
                )
            )
            {
                Terminal.Shell.ClearAutoRegisteredCommands();
                Terminal.Shell.InitializeAutoRegisteredCommands(
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
            if (CheckForRefresh(_uiProperties))
            {
                SetupUI();
            }

            if (CheckForRefresh(_fontProperties))
            {
                SetFont();
            }

            if (CheckForRefresh(_staticStateProperties))
            {
                RefreshStaticState(force: false);
            }

            if (CheckForRefresh(_windowProperties))
            {
                SetupWindow();
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
                _autoCompleteContainer?.Clear();
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
            try
            {
                switch (newState)
                {
                    case TerminalState.Closed:
                    {
                        _targetWindowHeight = 0;
                        break;
                    }
                    case TerminalState.OpenSmall:
                    {
                        _targetWindowHeight = Screen.height * _maxHeight * _smallTerminalRatio;
                        _realWindowHeight = _targetWindowHeight;
                        break;
                    }
                    case TerminalState.OpenFull:
                    {
                        _realWindowHeight = Screen.height * _maxHeight;
                        _targetWindowHeight = _realWindowHeight;
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
                    // TODO FIXME When full terminal is toggled, events disappear when going back to short terminal
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
            while (Terminal.Shell?.TryConsumeErrorMessage(out string error) == true)
            {
                Terminal.Log(TerminalLogType.Error, $"Error: {error}");
            }
        }

        private void SetFont()
        {
            if (_uiDocument == null)
            {
                Debug.LogError("Cannot set font, no UIDocument assigned.");
                return;
            }

            if (_consoleFont == null)
            {
                Debug.LogError("Cannot set font, no console font assigned.");
                return;
            }

            VisualElement root = _uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("Cannot set font, UI root element does not exist.");
                return;
            }

            root.style.unityFontDefinition = new StyleFontDefinition(_consoleFont);
        }

        private void ResetAutoComplete()
        {
            _lastKnownCommandText = _commandText ?? string.Empty;
            if (_hintDisplayMode == HintDisplayMode.Always)
            {
                string[] buffer = _lastCompletionBuffer;
                _lastCompletionBuffer =
                    Terminal.AutoComplete?.Complete(_lastKnownCommandText) ?? Array.Empty<string>();
                if (buffer == null || buffer.Length != _lastCompletionBuffer.Length)
                {
                    _lastCompletionIndex = null;
                    _previousLastCompletionIndex = null;
                }
                else
                {
                    for (int i = 0; i < buffer.Length; ++i)
                    {
                        if (
                            !string.Equals(
                                buffer[i],
                                _lastCompletionBuffer[i],
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            _lastCompletionIndex = null;
                            _previousLastCompletionIndex = null;
                            break;
                        }
                    }
                }
            }
            else
            {
                _lastCompletionIndex = null;
                _previousLastCompletionIndex = null;
                _lastCompletionBuffer = Array.Empty<string>();
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
                        _realWindowHeight = height * _maxHeight * _smallTerminalRatio;
                        _targetWindowHeight = _realWindowHeight;
                        break;
                    }
                    case TerminalState.OpenFull:
                    {
                        _realWindowHeight = height * _maxHeight;
                        _targetWindowHeight = _realWindowHeight;
                        break;
                    }
                    default:
                    {
                        _realWindowHeight = height * _maxHeight * _smallTerminalRatio;
                        _targetWindowHeight = 0;
                        break;
                    }
                }
            }
            finally
            {
                _lastHeight = height;
                _lastWidth = width;
            }
        }

        private void SetupUI()
        {
            if (_uiDocument == null)
            {
                Debug.LogError("No UIDocument assigned, cannot setup UI.");
                return;
            }

            VisualElement uiRoot = _uiDocument.rootVisualElement;
            if (uiRoot == null)
            {
                Debug.LogError("No UI root element assigned, cannot setup UI.");
                return;
            }

            SetFont();
            uiRoot.Clear();
            VisualElement root = new();
            uiRoot.Add(root);
            root.name = "TerminalRoot";
            root.AddToClassList("terminal-root");

            _terminalContainer = new VisualElement { name = "TerminalContainer" };
            _terminalContainer.AddToClassList("terminal-container");
            _terminalContainer.style.height = new StyleLength(_realWindowHeight);
            root.Add(_terminalContainer);

            _logScrollView = new ScrollView();
            InitializeScrollView(_logScrollView);
            _logScrollView.name = "LogScrollView";
            _logScrollView.AddToClassList("log-scroll-view");
            _terminalContainer.Add(_logScrollView);

            _autoCompleteContainer = new ScrollView(ScrollViewMode.Horizontal)
            {
                name = "AutoCompletePopup",
            };
            _autoCompleteContainer.AddToClassList("autocomplete-popup");
            _terminalContainer.Add(_autoCompleteContainer);

            _inputContainer = new VisualElement { name = "InputContainer" };
            _inputContainer.AddToClassList("input-container");
            _inputContainerHeight = _inputContainer.layout.height;
            _terminalContainer.Add(_inputContainer);

            _runButton = new Button(EnterCommand)
            {
                text = _runButtonText,
                name = "RunCommandButton",
            };
            _runButton.AddToClassList("terminal-button");
            _runButton.AddToClassList("terminal-button-run");
            _runButton.style.display = DisplayStyle.None;
            _runButton.style.marginLeft = 6;
            _runButton.style.marginRight = 4;
            _runButton.style.paddingTop = 2;
            _runButton.style.paddingBottom = 2;
            _inputContainer.Add(_runButton);

            _inputCaretLabel = new Label(_inputCaret) { name = "InputCaret" };
            _inputCaretLabel.AddToClassList("terminal-input-caret");
            _inputContainer.Add(_inputCaretLabel);

            _commandInput = new TextField();
            ScheduleBlinkingCursor(_commandInput);
            _commandInput.name = "CommandInput";
            _commandInput.AddToClassList("terminal-input-field");
            _commandInput.pickingMode = PickingMode.Position;
            _commandInput.value = _commandText;
            _commandInput.RegisterCallback<ChangeEvent<string>, UIToolkitTerminal>(
                (evt, context) =>
                {
                    if (_handledInputThisFrame)
                    {
                        evt.StopImmediatePropagation();
                        return;
                    }

                    context._commandText = evt.newValue;

                    context._runButton.style.display =
                        _showGUIButtons
                        && !string.IsNullOrWhiteSpace(context._commandText)
                        && !string.IsNullOrWhiteSpace(context._runButtonText)
                            ? DisplayStyle.Flex
                            : DisplayStyle.None;
                    if (!context._isCommandFromCode)
                    {
                        context.ResetAutoComplete();
                    }

                    context._isCommandFromCode = false;
                },
                userArgs: this,
                useTrickleDown: TrickleDown.TrickleDown
            );
            _commandInput.RegisterCallback<KeyDownEvent, UIToolkitTerminal>(
                (evt, context) =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        context.EnterCommand();
                        evt.StopPropagation();
                        evt.PreventDefault();
                    }
                },
                userArgs: this,
                useTrickleDown: TrickleDown.TrickleDown
            );
            _commandInputHeight = _commandInput.layout.height;

            _inputContainer.Add(_commandInput);
            _textInput = _commandInput.Q<VisualElement>("unity-text-input");

            _stateButtonContainer = new VisualElement { name = "StateButtonContainer" };
            _stateButtonContainer.AddToClassList("state-button-container");
            root.Add(_stateButtonContainer);
            RefreshStateButtons();
        }

        private static void ScheduleBlinkingCursor(TextField textField)
        {
            const string className = "transparent-cursor";
            textField
                .schedule.Execute(() =>
                {
                    if (textField.ClassListContains(className))
                    {
                        textField.RemoveFromClassList(className);
                    }
                    else
                    {
                        textField.AddToClassList(className);
                    }
                })
                .Every(666);
        }

        private static void InitializeScrollView(ScrollView scrollView)
        {
            VisualElement parent = scrollView.Q<VisualElement>(
                className: "unity-scroller--vertical"
            );
            if (parent == null)
            {
                scrollView.RegisterCallback<GeometryChangedEvent>(ReInitialize);
                return;

                void ReInitialize(GeometryChangedEvent evt)
                {
                    InitializeScrollView(scrollView);
                    scrollView.UnregisterCallback<GeometryChangedEvent>(ReInitialize);
                }
            }
            VisualElement trackerElement = parent.Q<VisualElement>(
                className: "unity-base-slider__tracker"
            );
            VisualElement draggerElement = parent.Q<VisualElement>(
                className: "unity-base-slider__dragger"
            );

            ScrollBarCaptureState scrollBarCaptureState = ScrollBarCaptureState.None;

            RegisterCallbacks();
            return;

            void RegisterCallbacks()
            {
                // Hover Events
                trackerElement.RegisterCallback<MouseEnterEvent>(OnTrackerMouseEnter);
                trackerElement.RegisterCallback<MouseLeaveEvent>(OnTrackerMouseLeave);
                draggerElement.RegisterCallback<MouseEnterEvent>(OnDraggerMouseEnter);
                draggerElement.RegisterCallback<MouseLeaveEvent>(OnDraggerMouseLeave);

                trackerElement.RegisterCallback<PointerDownEvent>(OnTrackerPointerDown);
                trackerElement.RegisterCallback<PointerUpEvent>(OnTrackerPointerUp);
                draggerElement.RegisterCallback<PointerDownEvent>(OnDraggerPointerDown);
                parent.RegisterCallback<PointerCaptureOutEvent>(OnDraggerPointerCaptureOut);
            }

            void OnTrackerPointerDown(PointerDownEvent evt)
            {
                scrollBarCaptureState = ScrollBarCaptureState.TrackerActive;
                draggerElement.AddToClassList("tracker-active");
                draggerElement.RemoveFromClassList("tracker-hovered");
            }

            void OnTrackerPointerUp(PointerUpEvent evt)
            {
                scrollBarCaptureState = ScrollBarCaptureState.None;
                draggerElement.RemoveFromClassList("tracker-active");
            }

            void OnDraggerPointerDown(PointerDownEvent evt)
            {
                scrollBarCaptureState = ScrollBarCaptureState.DraggerActive;
                trackerElement.AddToClassList("dragger-active");
                draggerElement.AddToClassList("dragger-active");
                trackerElement.RemoveFromClassList("dragger-hovered");
            }

            void OnDraggerPointerCaptureOut(PointerCaptureOutEvent evt)
            {
                scrollBarCaptureState = ScrollBarCaptureState.None;
                trackerElement.RemoveFromClassList("dragger-active");
                draggerElement.RemoveFromClassList("tracker-active");
                draggerElement.RemoveFromClassList("dragger-active");
            }

            void OnTrackerMouseEnter(MouseEnterEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.None)
                {
                    draggerElement.AddToClassList("tracker-hovered");
                }
            }

            void OnTrackerMouseLeave(MouseLeaveEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.None)
                {
                    draggerElement.RemoveFromClassList("tracker-hovered");
                }
            }

            void OnDraggerMouseEnter(MouseEnterEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.None)
                {
                    trackerElement.AddToClassList("dragger-hovered");
                }
            }

            void OnDraggerMouseLeave(MouseLeaveEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.None)
                {
                    trackerElement.RemoveFromClassList("dragger-hovered");
                }
            }
        }

        private void RefreshUI()
        {
            if (_terminalContainer == null)
            {
                return;
            }

            if (_handledInputThisFrame)
            {
                return;
            }

            _terminalContainer.style.height = _currentWindowHeight;
            _terminalContainer.style.width = Screen.width;
            _inputContainer.style.height = Mathf.Min(_currentWindowHeight, _inputContainerHeight);
            _commandInput.style.height = Mathf.Min(_currentWindowHeight, _commandInputHeight);
            _inputCaretLabel.style.display =
                _currentWindowHeight < _commandInputHeight ? DisplayStyle.None : DisplayStyle.Flex;

            RefreshLogs();
            RefreshAutoCompleteHints();
            if (_commandInput.value != _commandText)
            {
                _isCommandFromCode = true;
                _commandInput.value = _commandText;
            }
            else if (_needsFocus && _textInput.focusable)
            {
                if (_textInput.focusController.focusedElement != _textInput)
                {
                    _textInput.schedule.Execute(_focusInput).ExecuteLater(0);
                    FocusInput();
                }

                _needsFocus = false;
            }
            else if (_needsScrollToEnd)
            {
                ScrollToEnd();
                _needsScrollToEnd = false;
            }
            RefreshStateButtons();
        }

        private void FocusInput()
        {
            if (_textInput == null)
            {
                return;
            }

            _textInput.Focus();
            int textEndPosition = _commandInput.value.Length;
            _commandInput.cursorIndex = textEndPosition;
            _commandInput.selectIndex = textEndPosition;
        }

        private void RefreshLogs()
        {
            IReadOnlyList<LogItem> logs = Terminal.Buffer?.Logs;
            if (logs == null)
            {
                return;
            }

            if (_logScrollView == null)
            {
                return;
            }

            VisualElement content = _logScrollView.contentContainer;
            bool dirty = _lastSeenBufferVersion != Terminal.Buffer.Version;
            if (content.childCount != logs.Count)
            {
                dirty = true;
                if (content.childCount < logs.Count)
                {
                    for (int i = 0; i < logs.Count - content.childCount; ++i)
                    {
                        Label logLabel = new();
                        logLabel.AddToClassList("terminal-output-label");
                        content.Add(logLabel);
                    }
                }
                else if (logs.Count < content.childCount)
                {
                    for (int i = content.childCount - 1; logs.Count <= i; --i)
                    {
                        content.RemoveAt(i);
                    }
                }

                _needsScrollToEnd = true;
            }

            if (dirty)
            {
                for (int i = 0; i < logs.Count && i < content.childCount; ++i)
                {
                    if (content[i] is Label logLabel)
                    {
                        LogItem logItem = logs[i];
                        SetupLabel(logLabel, logItem);
                        logLabel.text = logItem.message;
                    }
                }

                if (logs.Count == content.childCount)
                {
                    _lastSeenBufferVersion = Terminal.Buffer.Version;
                }
            }
            return;

            static void SetupLabel(Label label, LogItem log)
            {
                label.EnableInClassList(
                    "terminal-output-label--shell",
                    log.type == TerminalLogType.ShellMessage
                );
                label.EnableInClassList(
                    "terminal-output-label--error",
                    log.type
                        is TerminalLogType.Exception
                            or TerminalLogType.Error
                            or TerminalLogType.Assert
                );
                label.EnableInClassList(
                    "terminal-output-label--warning",
                    log.type == TerminalLogType.Warning
                );
                label.EnableInClassList(
                    "terminal-output-label--message",
                    log.type == TerminalLogType.Message
                );
                label.EnableInClassList(
                    "terminal-output-label--input",
                    log.type == TerminalLogType.Input
                );
            }
        }

        private void ScrollToEnd()
        {
            if (0 < _logScrollView?.verticalScroller.highValue)
            {
                _logScrollView.verticalScroller.value = _logScrollView.verticalScroller.highValue;
            }
        }

        private void RefreshAutoCompleteHints()
        {
            bool shouldDisplay =
                _lastCompletionBuffer is { Length: > 0 }
                && _hintDisplayMode is HintDisplayMode.Always or HintDisplayMode.AutoCompleteOnly
                && _autoCompleteContainer != null;

            if (!shouldDisplay)
            {
                if (0 < _autoCompleteContainer?.childCount)
                {
                    _autoCompleteContainer.Clear();
                }

                _previousLastCompletionIndex = null;
                return;
            }

            int bufferLength = _lastCompletionBuffer.Length;
            int currentChildCount = _autoCompleteContainer.childCount;

            bool dirty = _lastCompletionIndex != _previousLastCompletionIndex;
            bool contentsChanged = currentChildCount != bufferLength;
            if (contentsChanged)
            {
                dirty = true;
                if (currentChildCount < bufferLength)
                {
                    for (int i = currentChildCount; i < bufferLength; ++i)
                    {
                        string hint = _lastCompletionBuffer[i];
                        VisualElement hintElement;

                        if (_makeHintsClickable)
                        {
                            int currentIndex = i;
                            string currentHint = hint;
                            Button hintButton = new(() =>
                            {
                                _commandText = currentHint;
                                _lastCompletionIndex = currentIndex;
                                _needsFocus = true;
                            })
                            {
                                text = hint,
                            };
                            hintElement = hintButton;
                        }
                        else
                        {
                            Label hintLabel = new(hint);
                            hintElement = hintLabel;
                        }

                        hintElement.name = $"SuggestionText{i}";
                        _autoCompleteContainer.Add(hintElement);

                        bool isSelected = i == _lastCompletionIndex;
                        hintElement.AddToClassList("terminal-button");
                        hintElement.EnableInClassList("autocomplete-item-selected", isSelected);
                        hintElement.EnableInClassList("autocomplete-item", !isSelected);
                    }
                }
                else if (bufferLength < currentChildCount)
                {
                    for (int i = currentChildCount - 1; bufferLength <= i; --i)
                    {
                        _autoCompleteContainer.RemoveAt(i);
                    }
                }
            }

            try
            {
                if (_autoCompleteContainer.childCount == bufferLength)
                {
                    UpdateAutoCompleteView();
                }

                if (dirty)
                {
                    for (int i = 0; i < _autoCompleteContainer.childCount && i < bufferLength; ++i)
                    {
                        VisualElement hintElement = _autoCompleteContainer[i];
                        // if (contentsChanged)
                        // {
                        // }

                        switch (hintElement)
                        {
                            case Button button:
                                button.text = _lastCompletionBuffer[i];
                                break;
                            case Label label:
                                label.text = _lastCompletionBuffer[i];
                                break;
                        }

                        bool isSelected = i == _lastCompletionIndex;

                        hintElement.EnableInClassList("autocomplete-item-selected", isSelected);
                        hintElement.EnableInClassList("autocomplete-item", !isSelected);
                    }
                }
            }
            finally
            {
                _previousLastCompletionIndex = _lastCompletionIndex;
            }
        }

        private void UpdateAutoCompleteView()
        {
            if (_lastCompletionIndex == null)
            {
                return;
            }

            if (_autoCompleteContainer?.contentContainer == null)
            {
                return;
            }

            int childCount = _autoCompleteContainer.childCount;
            if (childCount == 0)
            {
                return;
            }

            if (childCount <= _lastCompletionIndex)
            {
                _lastCompletionIndex =
                    (_lastCompletionIndex % childCount + childCount) % childCount;
            }

            if (_previousLastCompletionIndex == _lastCompletionIndex)
            {
                return;
            }

            VisualElement current = _autoCompleteContainer[_lastCompletionIndex.Value];
            float viewportWidth = _autoCompleteContainer.contentViewport.resolvedStyle.width;

            // Use layout properties relative to the content container
            float targetElementLeft = current.layout.x;
            float targetElementWidth = current.layout.width;
            float targetElementRight = targetElementLeft + targetElementWidth;

            const float epsilon = 0.01f;

            bool isFullyVisible =
                epsilon <= targetElementLeft && targetElementRight <= viewportWidth + epsilon;

            if (isFullyVisible)
            {
                return;
            }

            bool isIncrementing;
            if (_previousLastCompletionIndex == childCount - 1 && _lastCompletionIndex == 0)
            {
                isIncrementing = true;
            }
            else if (_previousLastCompletionIndex == 0 && _lastCompletionIndex == childCount - 1)
            {
                isIncrementing = false;
            }
            else
            {
                isIncrementing = _previousLastCompletionIndex < _lastCompletionIndex;
            }

            _autoCompleteChildren.Clear();
            for (int i = 0; i < childCount; ++i)
            {
                _autoCompleteChildren.Add(_autoCompleteContainer[i]);
            }

            int shiftAmount;
            if (isIncrementing)
            {
                shiftAmount = -1 * _lastCompletionIndex.Value;
                _lastCompletionIndex = 0;
            }
            else
            {
                shiftAmount = 0;
                float accumulatedWidth = 0;
                for (int i = 1; i <= childCount; ++i)
                {
                    shiftAmount++;
                    int index = -i % childCount;
                    index = (index + childCount) % childCount;
                    VisualElement element = _autoCompleteChildren[index];
                    accumulatedWidth += element.layout.width;
                    if (viewportWidth < accumulatedWidth)
                    {
                        if (element != current)
                        {
                            --shiftAmount;
                        }

                        break;
                    }
                }

                _lastCompletionIndex = (shiftAmount - 1 + childCount) % childCount;
            }

            _autoCompleteChildren.Shift(shiftAmount);
            _lastCompletionBuffer.Shift(shiftAmount);

            _autoCompleteContainer.Clear();
            foreach (VisualElement element in _autoCompleteChildren)
            {
                _autoCompleteContainer.Add(element);
            }
        }

        private void RefreshStateButtons()
        {
            if (_stateButtonContainer == null)
            {
                return;
            }

            _stateButtonContainer.style.top = _currentWindowHeight;
            DisplayStyle displayStyle = _showGUIButtons ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = 0; i < _stateButtonContainer.childCount; ++i)
            {
                VisualElement child = _stateButtonContainer[i];
                child.style.display = displayStyle;
            }

            if (!_showGUIButtons)
            {
                return;
            }

            Button firstButton;
            Button secondButton;
            if (_stateButtonContainer.childCount == 0)
            {
                firstButton = new Button(FirstClicked) { name = "StateButton1" };
                firstButton.AddToClassList("terminal-button");
                firstButton.style.display = displayStyle;
                _stateButtonContainer.Add(firstButton);

                secondButton = new Button(SecondClicked) { name = "StateButton2" };
                secondButton.AddToClassList("terminal-button");
                secondButton.style.display = displayStyle;
                _stateButtonContainer.Add(secondButton);
            }
            else
            {
                firstButton = _stateButtonContainer[0] as Button;
                if (firstButton == null)
                {
                    return;
                }
                secondButton = _stateButtonContainer[1] as Button;
                if (secondButton == null)
                {
                    return;
                }
            }

            _inputCaretLabel.text = _inputCaret;

            switch (_state)
            {
                case TerminalState.Closed:
                    if (!string.IsNullOrWhiteSpace(_smallButtonText))
                    {
                        firstButton.text = _smallButtonText;
                    }
                    if (!string.IsNullOrWhiteSpace(_fullButtonText))
                    {
                        secondButton.text = _fullButtonText;
                    }
                    break;
                case TerminalState.OpenSmall:
                    if (!string.IsNullOrWhiteSpace(_closeButtonText))
                    {
                        firstButton.text = _closeButtonText;
                    }
                    if (!string.IsNullOrWhiteSpace(_fullButtonText))
                    {
                        secondButton.text = _fullButtonText;
                    }
                    break;
                case TerminalState.OpenFull:
                    if (!string.IsNullOrWhiteSpace(_closeButtonText))
                    {
                        firstButton.text = _closeButtonText;
                    }
                    if (!string.IsNullOrWhiteSpace(_smallButtonText))
                    {
                        secondButton.text = _smallButtonText;
                    }
                    break;
                default:
                    throw new InvalidEnumArgumentException(
                        nameof(_state),
                        (int)_state,
                        typeof(TerminalState)
                    );
            }
            return;

            void FirstClicked()
            {
                switch (_state)
                {
                    case TerminalState.Closed:
                        if (!string.IsNullOrWhiteSpace(_smallButtonText))
                        {
                            SetState(TerminalState.OpenSmall);
                        }
                        break;
                    case TerminalState.OpenSmall:
                    case TerminalState.OpenFull:
                        if (!string.IsNullOrWhiteSpace(_closeButtonText))
                        {
                            SetState(TerminalState.Closed);
                        }
                        break;
                    default:
                        throw new InvalidEnumArgumentException(
                            nameof(_state),
                            (int)_state,
                            typeof(TerminalState)
                        );
                }
            }

            void SecondClicked()
            {
                switch (_state)
                {
                    case TerminalState.Closed:
                    case TerminalState.OpenSmall:
                        if (!string.IsNullOrWhiteSpace(_fullButtonText))
                        {
                            SetState(TerminalState.OpenFull);
                        }
                        break;
                    case TerminalState.OpenFull:
                        if (!string.IsNullOrWhiteSpace(_smallButtonText))
                        {
                            SetState(TerminalState.OpenSmall);
                        }
                        break;
                    default:
                        throw new InvalidEnumArgumentException(
                            nameof(_state),
                            (int)_state,
                            typeof(TerminalState)
                        );
                }
            }
        }

#if ENABLE_INPUT_SYSTEM
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

            _commandText = Terminal.History?.Previous(_skipSameCommandsInHistory) ?? string.Empty;
            ResetAutoComplete();
            _needsFocus = true;
        }

        public void HandleNext()
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }

            _commandText = Terminal.History?.Next(_skipSameCommandsInHistory) ?? string.Empty;
            ResetAutoComplete();
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

            _commandText = _commandText?.Trim();
            try
            {
                if (string.IsNullOrWhiteSpace(_commandText))
                {
                    return;
                }

                Terminal.Log(TerminalLogType.Input, _commandText);
                Terminal.Shell?.RunCommand(_commandText);
                while (Terminal.Shell?.TryConsumeErrorMessage(out string error) == true)
                {
                    Terminal.Log(TerminalLogType.Error, $"Error: {error}");
                }

                _commandText = string.Empty;
                _needsFocus = true;
                _needsScrollToEnd = true;
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
                string[] completionBuffer;
                if (_lastKnownCommandText == null)
                {
                    _lastKnownCommandText = _commandText ?? string.Empty;
                    completionBuffer =
                        Terminal.AutoComplete?.Complete(_lastKnownCommandText)
                        ?? Array.Empty<string>();
                }
                else
                {
                    completionBuffer = _lastCompletionBuffer ?? Array.Empty<string>();
                }

                try
                {
                    int completionLength = completionBuffer.Length;
                    bool equivalentBuffers =
                        _lastCompletionBuffer != null
                        && _lastCompletionBuffer.Length == completionBuffer.Length;
                    if (equivalentBuffers)
                    {
                        for (int i = 0; i < completionLength; i++)
                        {
                            if (
                                !string.Equals(
                                    completionBuffer[i],
                                    _lastCompletionBuffer[i],
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                equivalentBuffers = false;
                                break;
                            }
                        }
                    }
                    if (equivalentBuffers)
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
                }
            }
            finally
            {
                _needsFocus = true;
            }
        }

        private void HandleOpenness()
        {
            float heightChangeThisFrame = _toggleSpeed * Time.unscaledDeltaTime;

            if (_currentWindowHeight < _targetWindowHeight)
            {
                _currentWindowHeight = Mathf.Min(
                    _currentWindowHeight + heightChangeThisFrame,
                    _targetWindowHeight
                );
            }
            else if (_targetWindowHeight < _currentWindowHeight)
            {
                _currentWindowHeight = Mathf.Max(
                    _currentWindowHeight - heightChangeThisFrame,
                    _targetWindowHeight
                );
            }
        }

        private static void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            Terminal.Buffer?.HandleLog(message, stackTrace, (TerminalLogType)type);
        }
    }
}
