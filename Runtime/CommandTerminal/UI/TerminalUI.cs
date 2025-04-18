using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WallstopStudios.DxCommandTerminal.Editor")]

namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using Attributes;
    using Backend;
    using Extensions;
    using Helper;
    using Input;
    using Themes;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    [DisallowMultipleComponent]
    public sealed class TerminalUI : MonoBehaviour
    {
        private enum ScrollBarCaptureState
        {
            None = 0,
            DraggerActive = 1,
            TrackerActive = 2,
        }

        // Cache log callback to reduce allocations
        private static readonly Application.LogCallback UnityLogCallback = HandleUnityLog;

        public static TerminalUI Instance { get; private set; }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool IsClosed =>
            _state != TerminalState.OpenFull
            && _state != TerminalState.OpenSmall
            && Mathf.Approximately(_currentWindowHeight, _targetWindowHeight);

        public string CurrentTheme =>
            !string.IsNullOrWhiteSpace(_runtimeTheme) ? _runtimeTheme : _persistedTheme;

        public Font CurrentFont => _runtimeFont != null ? _runtimeFont : _persistedFont;

        [SerializeField]
        [Tooltip("Unique Id for this terminal, mainly for use with persisted configuration")]
        internal string id = Guid.NewGuid().ToString();

        [SerializeField]
        [HideInInspector]
        internal UIDocument _uiDocument;

        [SerializeField]
        [HideInInspector]
        internal string _persistedTheme = "dark-theme";

        [Header("Window")]
        [Range(0, 1)]
        public float maxHeight = 0.7f;

        [SerializeField]
        [Range(0, 1)]
        public float smallTerminalRatio = 0.4714285f;

        [Range(100, 1000)]
        [SerializeField]
        private float _toggleSpeed = 360;

        [SerializeField]
        private int _logBufferSize = 256;

        [SerializeField]
        private int _historyBufferSize = 512;

        [Header("Input")]
        [SerializeField]
        internal Font _persistedFont;

        [SerializeField]
        private string _inputCaret = ">";

        [Header("Buttons")]
        public bool showGUIButtons;

        [DxShowIf(nameof(showGUIButtons))]
        public string runButtonText = "run";

        [DxShowIf(nameof(showGUIButtons))]
        [SerializeField]
        public string closeButtonText = "close";

        [DxShowIf(nameof(showGUIButtons))]
        public string smallButtonText = "small";

        [DxShowIf(nameof(showGUIButtons))]
        public string fullButtonText = "full";

        [Header("Hints")]
        public HintDisplayMode hintDisplayMode = HintDisplayMode.AutoCompleteOnly;

        public bool makeHintsClickable = true;

        [Header("System")]
        [SerializeField]
        private int _cursorBlinkRateMilliseconds = 666;

#if UNITY_EDITOR
        [SerializeField]
        private bool _trackChangesInEditor = true;
#endif

        [Tooltip("Will reset static command state in OnEnable and Start when set to true")]
        public bool resetStateOnInit;

        public bool skipSameCommandsInHistory = true;

        [SerializeField]
        public bool ignoreDefaultCommands;

        [SerializeField]
        private bool _logUnityMessages;

        [SerializeField]
        internal List<TerminalLogType> _ignoredLogTypes = new();

        [SerializeField]
        internal List<string> _disabledCommands = new();

        [SerializeField]
        [HideInInspector]
        internal TerminalFontPack _fontPack;

        [SerializeField]
        [HideInInspector]
        internal TerminalThemePack _themePack;

#if UNITY_EDITOR
        private readonly Dictionary<string, object> _propertyValues = new();
        private readonly List<SerializedProperty> _uiProperties = new();
        private readonly List<SerializedProperty> _cursorBlinkProperties = new();
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
        private bool _unityLogAttached;
        private bool _started;
        private bool _needsFocus;
        private bool _needsScrollToEnd;
        private bool _needsAutoCompleteReset;
        private long? _lastSeenBufferVersion;
        private string _lastKnownCommandText;
        private int? _lastCompletionIndex;
        private int? _previousLastCompletionIndex;
        private string _focusedControl;
        private bool _isCommandFromCode;
        private bool _initialResetStateOnInit;
        private bool _commandIssuedThisFrame;
        private string _runtimeTheme;
        private Font _runtimeFont;

        private VisualElement _terminalContainer;
        private ScrollView _logScrollView;
        private ScrollView _autoCompleteContainer;
        private VisualElement _inputContainer;
        private TextField _commandInput;
        private Button _runButton;
        private VisualElement _stateButtonContainer;
        private VisualElement _textInput;
        private Label _inputCaretLabel;
        private bool _lastKnownHintsClickable;
        private IVisualElementScheduledItem _cursorBlinkSchedule;

        private readonly List<string> _lastCompletionBuffer = new();
        private readonly List<string> _lastCompletionBufferTempCache = new();
        private readonly HashSet<string> _lastCompletionBufferTempSet = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly List<VisualElement> _autoCompleteChildren = new();

        // Cached for performance (avoids allocations)
        private readonly Action _focusInput;
#if UNITY_EDITOR
        private readonly EditorApplication.CallbackFunction _checkForChanges;
#endif
        private ITerminalInput _input;

        public TerminalUI()
        {
            _focusInput = FocusInput;
#if UNITY_EDITOR
            _checkForChanges = CheckForChanges;
#endif
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

            if (!TryGetComponent(out _input))
            {
                _input = DefaultTerminalInput.Instance;
            }

            Instance = this;

#if UNITY_EDITOR
            _serializedObject = new SerializedObject(this);

            string[] uiPropertiesTracked = { nameof(_uiDocument) };
            TrackProperties(uiPropertiesTracked, _uiProperties);

            string[] cursorBlinkPropertiesTracked = { nameof(_cursorBlinkRateMilliseconds) };
            TrackProperties(cursorBlinkPropertiesTracked, _cursorBlinkProperties);

            string[] fontPropertiesTracked = { nameof(_persistedFont) };
            TrackProperties(fontPropertiesTracked, _fontProperties);

            string[] staticStaticPropertiesTracked =
            {
                nameof(_logBufferSize),
                nameof(_historyBufferSize),
                nameof(_ignoredLogTypes),
                nameof(_disabledCommands),
                nameof(ignoreDefaultCommands),
                nameof(_fontPack),
            };
            TrackProperties(staticStaticPropertiesTracked, _staticStateProperties);

            string[] windowPropertiesTracked = { nameof(maxHeight), nameof(smallTerminalRatio) };
            TrackProperties(windowPropertiesTracked, _windowProperties);

            string[] logUnityMessagePropertiesTracked = { nameof(_logUnityMessages) };
            TrackProperties(logUnityMessagePropertiesTracked, _logUnityMessageProperties);

            string[] autoCompletePropertiesTracked =
            {
                nameof(hintDisplayMode),
                nameof(_disabledCommands),
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
                            case List<Font> fontList:
                                value = fontList.ToList();
                                break;
                        }
                        _propertyValues[property.name] = value;
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"Failed to track/find window property {propertyName}, updates to this property will be ignored.",
                            this
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

            SetupUI();

#if UNITY_EDITOR
            EditorApplication.update += _checkForChanges;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= _checkForChanges;
#endif
            if (_uiDocument != null)
            {
                _uiDocument.rootVisualElement?.Clear();
            }

            if (_unityLogAttached)
            {
                Application.logMessageReceivedThreaded -= UnityLogCallback;
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
        }

        private void Start()
        {
            if (_started)
            {
                SetState(TerminalState.Closed);
            }

            RefreshStaticState(force: resetStateOnInit);
            ResetWindowIdempotent();
            ConsumeAndLogErrors();
            ResetAutoComplete();
            _started = true;
            _lastKnownHintsClickable = makeHintsClickable;
        }

        private void LateUpdate()
        {
            ResetWindowIdempotent();

            HandleOpenness();
            RefreshUI();
            _commandIssuedThisFrame = false;
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
                    _disabledCommands ?? Enumerable.Empty<string>()
                )
            )
            {
                Terminal.Shell.ClearAutoRegisteredCommands();
                Terminal.Shell.InitializeAutoRegisteredCommands(
                    ignoredCommands: _disabledCommands,
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

            if (CheckForRefresh(_cursorBlinkProperties))
            {
                ScheduleBlinkingCursor();
            }

            if (CheckForRefresh(_fontProperties))
            {
                SetFont(_persistedFont);
            }

            if (CheckForRefresh(_staticStateProperties))
            {
                RefreshStaticState(force: false);
            }

            if (CheckForRefresh(_windowProperties))
            {
                ResetWindowIdempotent();
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
            _commandIssuedThisFrame = true;
            _state = newState;
            ResetWindowIdempotent();
            if (_state != TerminalState.Closed)
            {
                _needsFocus = true;
            }
            else
            {
                _input.CommandText = string.Empty;
                ResetAutoComplete();
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
            _lastKnownCommandText = _input.CommandText ?? string.Empty;
            if (hintDisplayMode == HintDisplayMode.Always)
            {
                _lastCompletionBufferTempCache.Clear();
                Terminal.AutoComplete?.Complete(
                    _lastKnownCommandText,
                    _lastCompletionBufferTempCache
                );
                bool equivalent =
                    _lastCompletionBufferTempCache.Count == _lastCompletionBuffer.Count;
                if (equivalent)
                {
                    _lastCompletionBufferTempSet.Clear();
                    foreach (string completion in _lastCompletionBuffer)
                    {
                        _lastCompletionBufferTempSet.Add(completion);
                    }

                    foreach (string completion in _lastCompletionBufferTempCache)
                    {
                        if (!_lastCompletionBufferTempSet.Contains(completion))
                        {
                            equivalent = false;
                            break;
                        }
                    }
                }

                if (!equivalent)
                {
                    _lastCompletionIndex = null;
                    _previousLastCompletionIndex = null;
                    _lastCompletionBuffer.Clear();
                    foreach (string completion in _lastCompletionBufferTempCache)
                    {
                        _lastCompletionBuffer.Add(completion);
                    }
                }
            }
            else
            {
                _lastCompletionIndex = null;
                _previousLastCompletionIndex = null;
                _lastCompletionBuffer.Clear();
            }
        }

        private void ResetWindowIdempotent()
        {
            int height = Screen.height;
            switch (_state)
            {
                case TerminalState.OpenSmall:
                {
                    _realWindowHeight = height * maxHeight * smallTerminalRatio;
                    _targetWindowHeight = _realWindowHeight;
                    break;
                }
                case TerminalState.OpenFull:
                {
                    _realWindowHeight = height * maxHeight;
                    _targetWindowHeight = _realWindowHeight;
                    break;
                }
                default:
                {
                    _realWindowHeight = height * maxHeight * smallTerminalRatio;
                    _targetWindowHeight = 0;
                    break;
                }
            }
        }

        private void SetupUI()
        {
            if (_uiDocument == null)
            {
                Debug.LogError("No UIDocument assigned, cannot setup UI.", this);
                return;
            }

            VisualElement uiRoot = _uiDocument.rootVisualElement;
            if (uiRoot == null)
            {
                Debug.LogError("No UI root element assigned, cannot setup UI.", this);
                return;
            }

            SetFont(_persistedFont);
            uiRoot.Clear();
            VisualElement root = new();
            uiRoot.Add(root);
            root.name = "TerminalRoot";
            root.AddToClassList("terminal-root");

            InitializeTheme();
            InitializeFont();

            if (!string.IsNullOrWhiteSpace(_runtimeTheme))
            {
                root.AddToClassList(_runtimeTheme);
            }
            else
            {
                Debug.LogError("Failed to load any themes!", this);
            }

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
            _terminalContainer.Add(_inputContainer);

            _runButton = new Button(EnterCommand)
            {
                text = runButtonText,
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
            ScheduleBlinkingCursor();
            _commandInput.name = "CommandInput";
            _commandInput.AddToClassList("terminal-input-field");
            _commandInput.pickingMode = PickingMode.Position;
            _commandInput.value = _input.CommandText;
            _commandInput.RegisterCallback<ChangeEvent<string>, TerminalUI>(
                (evt, context) =>
                {
                    if (_commandIssuedThisFrame)
                    {
                        evt.StopImmediatePropagation();
                        return;
                    }

                    context._input.CommandText = evt.newValue;

                    context._runButton.style.display =
                        showGUIButtons
                        && !string.IsNullOrWhiteSpace(context._input.CommandText)
                        && !string.IsNullOrWhiteSpace(context.runButtonText)
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
            _commandInput.RegisterCallback<KeyDownEvent, TerminalUI>(
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

        private void InitializeTheme()
        {
            if (_themePack == null)
            {
                Debug.LogError("No theme pack assigned, cannot initialize theme.", this);
                return;
            }

            _runtimeTheme = _persistedTheme;
            List<string> themeNames = _themePack._themeNames;
            if (themeNames.Contains(_runtimeTheme))
            {
                return;
            }

            if (themeNames is { Count: > 0 })
            {
                _runtimeTheme = themeNames.FirstOrDefault(theme =>
                    theme.Contains("dark", StringComparison.OrdinalIgnoreCase)
                );
                if (_runtimeTheme == null)
                {
                    _runtimeTheme = themeNames.FirstOrDefault(theme =>
                        theme.Contains("light", StringComparison.OrdinalIgnoreCase)
                    );
                }
                if (_runtimeTheme == null)
                {
                    _runtimeTheme = themeNames.FirstOrDefault();
                }
                Debug.LogWarning($"Persisted theme not found, defaulting to '{_runtimeTheme}'.");
            }
            else
            {
                Debug.LogError("No available terminal themes.", this);
            }
        }

        private void InitializeFont()
        {
            if (_fontPack == null)
            {
                Debug.LogError("No font pack assigned, cannot initialize font.", this);
                return;
            }

            _runtimeFont = _persistedFont;
            if (_runtimeFont != null)
            {
                return;
            }

            List<Font> loadedFonts = _fontPack._fonts;
            if (loadedFonts is { Count: > 0 })
            {
                _runtimeFont = loadedFonts.FirstOrDefault(font =>
                    font.name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                    && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
                );
                if (_runtimeFont == null)
                {
                    _runtimeFont = loadedFonts.FirstOrDefault(font =>
                        font.name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                    );
                }
                if (_runtimeFont == null)
                {
                    _runtimeFont = loadedFonts.FirstOrDefault(font =>
                        font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
                    );
                }
                if (_runtimeFont == null)
                {
                    _runtimeFont = loadedFonts.FirstOrDefault();
                }
            }

            if (_runtimeFont == null)
            {
                Debug.LogError("No font assigned, defaulting to Courier New 16pt", this);
                _runtimeFont = Font.CreateDynamicFontFromOSFont("Courier New", 16);
            }
            else
            {
                Debug.LogWarning($"No font assigned, defaulting to {_runtimeFont.name}.", this);
            }
        }

        private void ScheduleBlinkingCursor()
        {
            _cursorBlinkSchedule?.Pause();
            _cursorBlinkSchedule = null;

            if (_commandInput == null)
            {
                return;
            }

            bool shouldRenderCursor = true;
            _cursorBlinkSchedule = _commandInput
                .schedule.Execute(() =>
                {
                    _commandInput.EnableInClassList("transparent-cursor", shouldRenderCursor);
                    _commandInput.EnableInClassList("styled-cursor", !shouldRenderCursor);
                    shouldRenderCursor = !shouldRenderCursor;
                })
                .Every(_cursorBlinkRateMilliseconds);
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

            if (_commandIssuedThisFrame)
            {
                return;
            }

            _terminalContainer.style.height = _currentWindowHeight;
            _terminalContainer.style.width = Screen.width;
            DisplayStyle commandInputStyle =
                _currentWindowHeight <= 30 ? DisplayStyle.None : DisplayStyle.Flex;

            _needsFocus |=
                _inputContainer.resolvedStyle.display != commandInputStyle
                && commandInputStyle == DisplayStyle.Flex;
            _inputContainer.style.display = commandInputStyle;

            RefreshLogs();
            RefreshAutoCompleteHints();
            string commandInput = _input.CommandText;
            if (!string.Equals(_commandInput.value, commandInput))
            {
                _isCommandFromCode = true;
                _commandInput.value = commandInput;
            }
            else if (
                _needsFocus
                && _textInput.focusable
                && _textInput.resolvedStyle.display != DisplayStyle.None
                && _commandInput.resolvedStyle.display != DisplayStyle.None
            )
            {
                if (_textInput.focusController.focusedElement != _textInput)
                {
                    _textInput.schedule.Execute(_focusInput).ExecuteLater(0);
                    FocusInput();
                }

                _needsFocus = false;
            }
            else if (
                _needsScrollToEnd
                && _logScrollView != null
                && _logScrollView.style.display != DisplayStyle.None
            )
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
                        Label logText = new();
                        logText.AddToClassList("terminal-output-label");
                        content.Add(logText);
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
                    VisualElement item = content[i];
                    switch (item)
                    {
                        case TextField logText:
                        {
                            LogItem logItem = logs[i];
                            SetupLogText(logText, logItem);
                            logText.value = logItem.message;
                            break;
                        }
                        case Label logLabel:
                        {
                            LogItem logItem = logs[i];
                            SetupLogText(logLabel, logItem);
                            logLabel.text = logItem.message;
                            break;
                        }
                        case Button button:
                        {
                            LogItem logItem = logs[i];
                            SetupLogText(button, logItem);
                            button.text = logItem.message;
                            break;
                        }
                    }
                }

                if (logs.Count == content.childCount)
                {
                    _lastSeenBufferVersion = Terminal.Buffer.Version;
                }
            }
            return;

            static void SetupLogText(VisualElement logText, LogItem log)
            {
                logText.EnableInClassList(
                    "terminal-output-label--shell",
                    log.type == TerminalLogType.ShellMessage
                );
                logText.EnableInClassList(
                    "terminal-output-label--error",
                    log.type
                        is TerminalLogType.Exception
                            or TerminalLogType.Error
                            or TerminalLogType.Assert
                );
                logText.EnableInClassList(
                    "terminal-output-label--warning",
                    log.type == TerminalLogType.Warning
                );
                logText.EnableInClassList(
                    "terminal-output-label--message",
                    log.type == TerminalLogType.Message
                );
                logText.EnableInClassList(
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
                0 < _lastCompletionBuffer.Count
                && hintDisplayMode is HintDisplayMode.Always or HintDisplayMode.AutoCompleteOnly
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

            int bufferLength = _lastCompletionBuffer.Count;
            if (_lastKnownHintsClickable != makeHintsClickable)
            {
                _autoCompleteContainer.Clear();
                _lastKnownHintsClickable = makeHintsClickable;
            }

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

                        if (makeHintsClickable)
                        {
                            int currentIndex = i;
                            string currentHint = hint;
                            Button hintButton = new(() =>
                            {
                                _input.CommandText = currentHint;
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
                            Label hintText = new(hint);
                            hintElement = hintText;
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

            bool shouldUpdateCompletionIndex = false;
            try
            {
                shouldUpdateCompletionIndex = _autoCompleteContainer.childCount == bufferLength;
                if (shouldUpdateCompletionIndex)
                {
                    UpdateAutoCompleteView();
                }

                if (dirty)
                {
                    for (int i = 0; i < _autoCompleteContainer.childCount && i < bufferLength; ++i)
                    {
                        VisualElement hintElement = _autoCompleteContainer[i];
                        switch (hintElement)
                        {
                            case Button button:
                                button.text = _lastCompletionBuffer[i];
                                break;
                            case Label label:
                                label.text = _lastCompletionBuffer[i];
                                break;
                            case TextField textField:
                                textField.value = _lastCompletionBuffer[i];
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
                if (shouldUpdateCompletionIndex)
                {
                    _previousLastCompletionIndex = _lastCompletionIndex;
                }
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
                    accumulatedWidth +=
                        element.resolvedStyle.width
                        + element.resolvedStyle.marginLeft
                        + element.resolvedStyle.marginRight
                        + element.resolvedStyle.borderLeftWidth
                        + element.resolvedStyle.borderRightWidth;

                    if (accumulatedWidth <= viewportWidth)
                    {
                        continue;
                    }

                    if (element != current)
                    {
                        --shiftAmount;
                    }

                    break;
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
            DisplayStyle displayStyle = showGUIButtons ? DisplayStyle.Flex : DisplayStyle.None;

            for (int i = 0; i < _stateButtonContainer.childCount; ++i)
            {
                VisualElement child = _stateButtonContainer[i];
                child.style.display = displayStyle;
            }

            if (!showGUIButtons)
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
                    if (!string.IsNullOrWhiteSpace(smallButtonText))
                    {
                        firstButton.text = smallButtonText;
                    }
                    if (!string.IsNullOrWhiteSpace(fullButtonText))
                    {
                        secondButton.text = fullButtonText;
                    }
                    break;
                case TerminalState.OpenSmall:
                    if (!string.IsNullOrWhiteSpace(closeButtonText))
                    {
                        firstButton.text = closeButtonText;
                    }
                    if (!string.IsNullOrWhiteSpace(fullButtonText))
                    {
                        secondButton.text = fullButtonText;
                    }
                    break;
                case TerminalState.OpenFull:
                    if (!string.IsNullOrWhiteSpace(closeButtonText))
                    {
                        firstButton.text = closeButtonText;
                    }
                    if (!string.IsNullOrWhiteSpace(smallButtonText))
                    {
                        secondButton.text = smallButtonText;
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
                        if (!string.IsNullOrWhiteSpace(smallButtonText))
                        {
                            SetState(TerminalState.OpenSmall);
                        }
                        break;
                    case TerminalState.OpenSmall:
                    case TerminalState.OpenFull:
                        if (!string.IsNullOrWhiteSpace(closeButtonText))
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
                        if (!string.IsNullOrWhiteSpace(fullButtonText))
                        {
                            SetState(TerminalState.OpenFull);
                        }
                        break;
                    case TerminalState.OpenFull:
                        if (!string.IsNullOrWhiteSpace(smallButtonText))
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

        public Font SetRandomFont(bool persist = false)
        {
            if (_fontPack == null)
            {
                return _runtimeFont;
            }

            List<Font> loadedFonts = _fontPack._fonts;
            if (loadedFonts is not { Count: > 0 })
            {
                return _runtimeFont;
            }

            int currentFontIndex = loadedFonts.IndexOf(_runtimeFont);

            int newFontIndex;
            do
            {
                newFontIndex = ThreadLocalRandom.Instance.Next(loadedFonts.Count);
            } while (newFontIndex == currentFontIndex && loadedFonts.Count != 1);

            Font newFont = loadedFonts[newFontIndex];
            SetFont(newFont, persist);
            return newFont;
        }

        public void SetFont(Font font, bool persist = false)
        {
            if (!persist && CurrentFont == font)
            {
                return;
            }

            if (font == null)
            {
                Debug.LogError("Cannot set null font.", this);
                return;
            }

            if (_uiDocument == null)
            {
                Debug.LogError("Cannot set font, no UIDocument assigned.");
                return;
            }

            Font currentFont = _persistedFont;

            _runtimeFont = font;
            Debug.Log(
                currentFont == null
                    ? $"Setting font to {font.name}."
                    : $"Changing font from {currentFont.name} to {font.name}.",
                this
            );

            if (persist)
            {
                _persistedFont = font;
            }

            if (Application.isPlaying)
            {
                VisualElement root = _uiDocument.rootVisualElement;
                if (root == null)
                {
                    return;
                }

                root.style.unityFontDefinition = new StyleFontDefinition(font);
            }
        }

        public string SetRandomTheme(bool persist = false)
        {
            if (_themePack == null)
            {
                return _runtimeTheme;
            }

            List<string> loadedThemes = _themePack._themeNames;
            if (loadedThemes is not { Count: > 0 })
            {
                return _runtimeTheme;
            }

            int currentThemeIndex = loadedThemes.IndexOf(_runtimeTheme);

            int newThemeIndex;
            do
            {
                newThemeIndex = ThreadLocalRandom.Instance.Next(loadedThemes.Count);
            } while (newThemeIndex == currentThemeIndex && loadedThemes.Count != 1);

            string newTheme = loadedThemes[newThemeIndex];
            SetTheme(newTheme, persist);
            return newTheme;
        }

        public void SetTheme(string theme, bool persist = false)
        {
            if (!persist && string.Equals(theme, CurrentTheme, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(theme))
            {
                return;
            }

            if (_themePack == null)
            {
                return;
            }

            List<string> themeNames = _themePack._themeNames;
            if (!themeNames.Contains(theme))
            {
                return;
            }

            string currentTheme = _persistedTheme;
            _runtimeTheme = theme;
            Debug.Log($"Changing theme from {currentTheme} to {theme}.", this);
            if (persist)
            {
                _persistedTheme = theme;
            }

            if (Application.isPlaying)
            {
                if (_uiDocument == null)
                {
                    return;
                }

                VisualElement terminalRoot = _uiDocument.rootVisualElement?.Q<VisualElement>(
                    "TerminalRoot"
                );
                if (terminalRoot == null)
                {
                    return;
                }

                string[] loadedThemes = terminalRoot
                    .GetClasses()
                    .Where(className =>
                        className.Contains("-theme", StringComparison.OrdinalIgnoreCase)
                        || className.Contains("theme-", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray();

                foreach (string loadedTheme in loadedThemes)
                {
                    terminalRoot.RemoveFromClassList(loadedTheme);
                }

                terminalRoot.AddToClassList(theme);
            }
        }

        public void HandlePrevious()
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }

            _input.CommandText =
                Terminal.History?.Previous(skipSameCommandsInHistory) ?? string.Empty;
            ResetAutoComplete();
            _needsFocus = true;
        }

        public void HandleNext()
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }

            _input.CommandText = Terminal.History?.Next(skipSameCommandsInHistory) ?? string.Empty;
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

            string commandText = _input.CommandText ?? string.Empty;
            if (commandText.NeedsTrim())
            {
                commandText = commandText.Trim();
            }

            _input.CommandText = commandText;
            try
            {
                if (string.IsNullOrWhiteSpace(commandText))
                {
                    return;
                }

                Terminal.Log(TerminalLogType.Input, commandText);
                Terminal.Shell?.RunCommand(commandText);
                while (Terminal.Shell?.TryConsumeErrorMessage(out string error) == true)
                {
                    Terminal.Log(TerminalLogType.Error, $"Error: {error}");
                }

                _input.CommandText = string.Empty;
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
                _lastKnownCommandText ??= _input.CommandText ?? string.Empty;
                _lastCompletionBufferTempCache.Clear();
                Terminal.AutoComplete?.Complete(
                    _lastKnownCommandText,
                    _lastCompletionBufferTempCache
                );
                bool equivalentBuffers = true;
                try
                {
                    int completionLength = _lastCompletionBufferTempCache.Count;
                    equivalentBuffers =
                        _lastCompletionBuffer.Count == _lastCompletionBufferTempCache.Count;
                    if (equivalentBuffers)
                    {
                        _lastCompletionBufferTempSet.Clear();
                        foreach (string item in _lastCompletionBuffer)
                        {
                            _lastCompletionBufferTempSet.Add(item);
                        }

                        foreach (string newCompletionItem in _lastCompletionBufferTempCache)
                        {
                            if (!_lastCompletionBufferTempSet.Contains(newCompletionItem))
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

                            _input.CommandText = _lastCompletionBuffer[_lastCompletionIndex.Value];
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
                            _input.CommandText = _lastCompletionBufferTempCache[0];
                        }
                        else
                        {
                            _lastCompletionIndex = null;
                        }
                    }
                }
                finally
                {
                    if (!equivalentBuffers)
                    {
                        _lastCompletionBuffer.Clear();
                        foreach (string item in _lastCompletionBufferTempCache)
                        {
                            _lastCompletionBuffer.Add(item);
                        }
                        _previousLastCompletionIndex = null;
                    }

                    _previousLastCompletionIndex ??= _lastCompletionIndex;
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
