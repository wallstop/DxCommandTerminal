﻿namespace CommandTerminal.UIToolkit
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

        public static UIToolkitTerminal Instance { get; private set; }

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

        private TerminalState _state = TerminalState.Closed;
        private float _currentOpenT;
        private float _openTarget;
        private float _realWindowSize;
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
        private VisualElement _autoCompleteContainer;
        private VisualElement _inputContainer;
        private TextField _commandInput;
        private Button _runButton;
        private VisualElement _stateButtonContainer;
        private VisualElement _textInput;

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

            string[] windowStylePropertiesTracked = { nameof(_consoleFont) };
            TrackProperties(windowStylePropertiesTracked, _windowStyleProperties);

            string[] inputPropertiesTracked = { nameof(_inputCaret) };
            TrackProperties(inputPropertiesTracked, _inputProperties);

            string[] buttonPropertiesTracked =
            {
                nameof(_buttonForegroundColor),
                nameof(_runButtonText),
                nameof(_closeButtonText),
                nameof(_smallButtonText),
                nameof(_fullButtonText),
            };
            TrackProperties(buttonPropertiesTracked, _buttonProperties);

            string[] labelPropertiesTracked = { nameof(_consoleFont) };
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

            if (Terminal.IsKeyPressed(_closeHotkey))
            {
                _handledInputThisFrame = true;
                Close();
            }
            else if (
                // ReSharper disable once ConvertClosureToMethodGroup
                _completeCommandHotkeys?.list?.Exists(key => Terminal.IsKeyPressed(key)) == true
            )
            {
                _handledInputThisFrame = true;
                EnterCommand();
            }
            else if (Terminal.IsKeyPressed(_previousHotkey))
            {
                _handledInputThisFrame = true;
                HandlePrevious();
            }
            else if (Terminal.IsKeyPressed(_nextHotkey))
            {
                _handledInputThisFrame = true;
                HandleNext();
            }
            else if (Terminal.IsKeyPressed(_toggleFullHotkey))
            {
                _handledInputThisFrame = true;
                ToggleFull();
            }
            else if (Terminal.IsKeyPressed(_toggleHotkey))
            {
                _handledInputThisFrame = true;
                ToggleSmall();
            }
            else if (Terminal.IsKeyPressed(_reverseCompleteHotkey))
            {
                _handledInputThisFrame = true;
                CompleteCommand(searchForward: false);
            }
            else if (Terminal.IsKeyPressed(_completeHotkey))
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

            int historyBufferSize = Math.Max(0, _historyBufferSize);
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
                        _openTarget = 0;
                        break;
                    }
                    case TerminalState.OpenSmall:
                    {
                        _openTarget = Screen.height * _maxHeight * _smallTerminalRatio;
                        _realWindowSize = Mathf.Max(_realWindowSize, _openTarget);
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
            while (Terminal.Shell?.TryConsumeErrorMessage(out string error) == true)
            {
                Terminal.Log(TerminalLogType.Error, $"Error: {error}");
            }
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
                }
                else
                {
                    for (int i = 0; i < buffer.Length; ++i)
                    {
                        if (!string.Equals(buffer[i], _lastCompletionBuffer[i]))
                        {
                            _lastCompletionIndex = null;
                            break;
                        }
                    }
                }
            }
            else
            {
                _lastCompletionIndex = null;
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
                        _realWindowSize = height * _maxHeight * _smallTerminalRatio;
                        _openTarget = _realWindowSize;
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
                _lastHeight = height;
                _lastWidth = width;
            }
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

            _terminalContainer = new VisualElement { name = "TerminalContainer" };
            _terminalContainer.AddToClassList("terminal-container");
            _terminalContainer.style.height = new StyleLength(_realWindowSize);
            root.Add(_terminalContainer);

            _logScrollView = new ScrollView();
            InitializeScrollView(_logScrollView);
            _logScrollView.name = "LogScrollView";
            _logScrollView.AddToClassList("log-scroll-view");
            _terminalContainer.Add(_logScrollView);

            _autoCompleteContainer = new VisualElement { name = "AutoCompletePopup" };
            _autoCompleteContainer.AddToClassList("autocomplete-popup");
            _terminalContainer.Add(_autoCompleteContainer);

            _inputContainer = new VisualElement { name = "InputContainer" };
            _inputContainer.AddToClassList("input-container");
            _terminalContainer.Add(_inputContainer);

            if (_showGUIButtons)
            {
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
            }

            if (!string.IsNullOrEmpty(_inputCaret))
            {
                Label caretLabel = new(_inputCaret) { name = "InputCaret" };
                caretLabel.AddToClassList("terminal-input-caret");
                _inputContainer.Add(caretLabel);
            }

            _commandInput = new TextField();
            ScheduleBlinkingCursor(_commandInput);
            _commandInput.name = "CommandInput";
            _commandInput.AddToClassList("terminal-input-field");
            _commandInput.pickingMode = PickingMode.Position;
            _commandInput.value = _commandText;
            _commandInput.RegisterCallback<ChangeEvent<string>, UIToolkitTerminal>(
                (evt, context) =>
                {
                    context._commandText = evt.newValue;
                    context._runButton.style.display =
                        !string.IsNullOrWhiteSpace(context._commandText)
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
            _terminalContainer.style.top = _currentOpenT - _realWindowSize;
            _terminalContainer.style.height = _realWindowSize;
            _terminalContainer.style.width = Screen.width;
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
                    _textInput.schedule.Execute(FocusInput).ExecuteLater(0);
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
                _lastSeenBufferVersion = Terminal.Buffer.Version;
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
                _lastSeenBufferVersion = Terminal.Buffer.Version;
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
            if (0 < _logScrollView.verticalScroller.highValue)
            {
                _logScrollView.verticalScroller.value = _logScrollView.verticalScroller.highValue;
            }
        }

        private void RefreshAutoCompleteHints()
        {
            bool shouldDisplay =
                _lastCompletionBuffer is { Length: > 0 }
                && _hintDisplayMode is HintDisplayMode.Always or HintDisplayMode.AutoCompleteOnly;

            if (!shouldDisplay)
            {
                if (0 < _autoCompleteContainer.childCount)
                {
                    _autoCompleteContainer.Clear();
                }
                return;
            }

            int bufferLength = _lastCompletionBuffer.Length;
            int currentChildCount = _autoCompleteContainer.childCount;

            if (currentChildCount != bufferLength)
            {
                _autoCompleteContainer.Clear();

                // Rebuild the list
                for (int i = 0; i < bufferLength; i++)
                {
                    string hint = _lastCompletionBuffer[i];
                    VisualElement hintElement;

                    if (_makeHintsClickable)
                    {
                        int currentIndex = i;
                        string currentHint = hint;
                        Button hintButton = new Button(() =>
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
                        Label hintLabel = new Label(hint);
                        hintElement = hintLabel;
                    }

                    hintElement.name = $"SuggestionText{i}";
                    _autoCompleteContainer.Add(hintElement);

                    bool isSelected = (i == _lastCompletionIndex);
                    hintElement.AddToClassList("terminal-button");
                    hintElement.EnableInClassList("autocomplete-item-selected", isSelected);
                    hintElement.EnableInClassList("autocomplete-item", !isSelected);
                }
            }
            else
            {
                for (int i = 0; i < currentChildCount; i++)
                {
                    VisualElement hintElement = _autoCompleteContainer[i];

                    bool isSelected = (i == _lastCompletionIndex);

                    hintElement.EnableInClassList("autocomplete-item-selected", isSelected);
                    hintElement.EnableInClassList("autocomplete-item", !isSelected);
                }
            }
        }

        private void RefreshStateButtons()
        {
            if (!_showGUIButtons)
            {
                if (0 < _stateButtonContainer.childCount)
                {
                    _stateButtonContainer.Clear();
                }

                return;
            }

            Button firstButton;
            Button secondButton;
            if (_stateButtonContainer.childCount == 0)
            {
                firstButton = new Button(FirstClicked) { name = "StateButton1" };
                firstButton.AddToClassList("terminal-button");
                _stateButtonContainer.Add(firstButton);

                secondButton = new Button(SecondClicked) { name = "StateButton2" };
                secondButton.AddToClassList("terminal-button");
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
            _stateButtonContainer.style.top = _currentOpenT + 4;
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

            _commandText = _commandText.Trim();
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
                _lastKnownCommandText ??= _commandText;
                string[] completionBuffer =
                    Terminal.AutoComplete?.Complete(_lastKnownCommandText) ?? Array.Empty<string>();
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
                }
            }
            finally
            {
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
        }

        private static void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            Terminal.Buffer?.HandleLog(message, stackTrace, (TerminalLogType)type);
        }
    }
}
