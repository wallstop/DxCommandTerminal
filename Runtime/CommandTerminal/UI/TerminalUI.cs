using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("WallstopStudios.DxCommandTerminal.Editor")]

namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
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
        private const string TerminalRootName = "TerminalRoot";
        private const float LauncherAutoCompleteSpacing = 6f;
        private const float LauncherEstimatedSuggestionRowHeight = 32f;
        private const float LauncherEstimatedHistoryRowHeight = 28f;

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
            && _state != TerminalState.OpenLauncher
            && Mathf.Approximately(_currentWindowHeight, _targetWindowHeight);

        private bool IsLauncherActive => _state == TerminalState.OpenLauncher;

        public string CurrentTheme =>
            !string.IsNullOrWhiteSpace(_runtimeTheme) ? _runtimeTheme : _persistedTheme;

        public string CurrentFriendlyTheme => ThemeNameHelper.GetFriendlyThemeName(CurrentTheme);

        public Font CurrentFont => _runtimeFont != null ? _runtimeFont : _persistedFont;

        [SerializeField]
        [Tooltip("Unique Id for this terminal, mainly for use with persisted configuration")]
        internal string id = Guid.NewGuid().ToString();

        [SerializeField]
        internal UIDocument _uiDocument;

        [SerializeField]
        internal string _persistedTheme = "dark-theme";

        [Header("Window")]
        [Range(0, 1)]
        public float maxHeight = 0.7f;

        [SerializeField]
        [Range(0, 1)]
        public float smallTerminalRatio = 0.4714285f;

        [Tooltip("Curve the console follows to go from closed -> open")]
        public AnimationCurve easeOutCurve = new()
        {
            keys = new[] { new Keyframe(0, 0), new Keyframe(1, 1) },
        };

        [Tooltip("Duration for the ease-out animation in seconds")]
        public float easeOutTime = 0.5f;

        [Tooltip("Curve the console follows to go from open -> closed")]
        public AnimationCurve easeInCurve = new()
        {
            keys = new[] { new Keyframe(0, 0), new Keyframe(1, 1) },
        };

        [Tooltip("Duration for the ease-in animation in seconds")]
        public float easeInTime = 0.5f;

        [Header("Launcher")]
        [ContextMenuItem("Reset Launcher Layout (Danger!)", nameof(ResetLauncherSettings))]
        [SerializeField]
        private TerminalLauncherSettings _launcherSettings = new();

        [Header("System")]
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

        [DxShowIf(nameof(showGUIButtons))]
        public string launcherButtonText = "launcher";

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
        internal TerminalFontPack _fontPack;

        [SerializeField]
        internal TerminalThemePack _themePack;

        private IInputHandler[] _inputHandlers;

#if UNITY_EDITOR
        private readonly Dictionary<string, object> _propertyValues = new();
        private readonly List<SerializedProperty> _uiProperties = new();
        private readonly List<SerializedProperty> _themeProperties = new();
        private readonly List<SerializedProperty> _cursorBlinkProperties = new();
        private readonly List<SerializedProperty> _fontProperties = new();
        private readonly List<SerializedProperty> _staticStateProperties = new();
        private readonly List<SerializedProperty> _windowProperties = new();
        private readonly List<SerializedProperty> _logUnityMessageProperties = new();
        private readonly List<SerializedProperty> _autoCompleteProperties = new();
        private SerializedObject _serializedObject;
#endif

        // Editor integration
#if UNITY_EDITOR
        [Header("Editor")]
        [Tooltip(
            "When enabled (and runtime mode allows Editor), the terminal will auto-discover IArgParser implementations on reload/start."
        )]
        [SerializeField]
        private bool _autoDiscoverParsersInEditor;
#endif

        [Header("Runtime Mode")]
        [Tooltip(
            "Controls which environment-specific features are enabled. Choose explicit modes. None is obsolete."
        )]
        [SerializeField]
#pragma warning disable CS0618 // Type or member is obsolete
        private TerminalRuntimeModeFlags _runtimeModes = TerminalRuntimeModeFlags.None;
#pragma warning restore CS0618 // Type or member is obsolete

        // Test helper to skip building UI entirely (prevents UI Toolkit panel updates)
        internal bool disableUIForTests;

        private TerminalState _state = TerminalState.Closed;
        private TerminalState _previousState = TerminalState.Closed;
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
        private float _initialWindowHeight;
        private float _animationTimer;
        private bool _isAnimating;
        private LauncherLayoutMetrics _launcherMetrics;
        private bool _launcherMetricsInitialized;

        private VisualElement _terminalContainer;
        private ScrollView _logScrollView;
        private ScrollView _autoCompleteContainer;
        private VisualElement _autoCompleteViewport;
        private VisualElement _logViewport;
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
        private readonly List<CommandHistoryEntry> _launcherHistoryEntries = new();

        private float _launcherSuggestionContentHeight;
        private float _launcherHistoryContentHeight;
        private long _lastRenderedLauncherHistoryVersion = -1;
        private long _cachedLauncherScrollVersion = -1;
        private float _cachedLauncherScrollValue;
        private bool _restoreLauncherScrollPending;

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
            TerminalRuntimeConfig.SetMode(_runtimeModes);
#if UNITY_EDITOR
            TerminalRuntimeConfig.EditorAutoDiscover = _autoDiscoverParsersInEditor;
#endif
            TerminalRuntimeConfig.TryAutoDiscoverParsers();
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

            _inputHandlers = GetComponents<IInputHandler>();

            if (!TryGetComponent(out _input))
            {
                _input = DefaultTerminalInput.Instance;
            }

            Instance = this;

#if UNITY_EDITOR
            _serializedObject = new SerializedObject(this);

            string[] uiPropertiesTracked = { nameof(_uiDocument) };
            TrackProperties(uiPropertiesTracked, _uiProperties);

            string[] themePropertiesTracked = { nameof(_themePack) };
            TrackProperties(themePropertiesTracked, _themeProperties);

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
                                value = new List<string>(stringList);
                                break;
                            case List<TerminalLogType> logTypeList:
                                value = new List<TerminalLogType>(logTypeList);
                                break;
                            case List<Font> fontList:
                                value = new List<Font>(fontList);
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
            // Drain any cross-thread logs into the main-thread buffer before refreshing UI
            Terminal.Buffer?.DrainPending();
            HandleHeightAnimation();
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
                if (!Terminal.Buffer.ignoredLogTypes.SetEquals(_ignoredLogTypes))
                {
                    Terminal.Buffer.ignoredLogTypes.Clear();
                    Terminal.Buffer.ignoredLogTypes.UnionWith(_ignoredLogTypes);
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
                || !Terminal.Shell.IgnoredCommands.SetEquals(_disabledCommands)
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

            if (CheckForRefresh(_themeProperties))
            {
                if (_uiDocument != null)
                {
                    InitializeTheme(
                        _uiDocument.rootVisualElement?.Q<VisualElement>(TerminalRootName)
                    );
                }
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
                        if (!ListsEqual(currentStringList, previousStringList))
                        {
                            needRefresh = true;
                            _propertyValues[property.name] = new List<string>(currentStringList);
                        }

                        continue;
                    }
                    if (
                        propertyValue is List<TerminalLogType> currentLogTypeList
                        && previousValue is List<TerminalLogType> previousLogTypeList
                    )
                    {
                        if (!ListsEqual(currentLogTypeList, previousLogTypeList))
                        {
                            needRefresh = true;
                            _propertyValues[property.name] = new List<TerminalLogType>(
                                currentLogTypeList
                            );
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
            if (_state == TerminalState.OpenLauncher && newState != TerminalState.OpenLauncher)
            {
                CacheLauncherScrollPosition();
            }

            _commandIssuedThisFrame = true;
            _previousState = _state;
            _state = newState;
            if (_state == TerminalState.OpenLauncher)
            {
                _restoreLauncherScrollPending = true;
            }
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

        private static bool ListsEqual<T>(List<T> a, List<T> b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a is null || b is null)
            {
                return false;
            }
            int count = a.Count;
            if (count != b.Count)
            {
                return false;
            }
            EqualityComparer<T> cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < count; ++i)
            {
                if (!cmp.Equals(a[i], b[i]))
                {
                    return false;
                }
            }
            return true;
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
                int caret =
                    _commandInput != null
                        ? _commandInput.cursorIndex
                        : (_lastKnownCommandText?.Length ?? 0);
                Terminal.AutoComplete?.Complete(
                    _lastKnownCommandText,
                    caret,
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
            int width = Screen.width;
            float oldTargetHeight = _targetWindowHeight;
            bool wasLauncher = _launcherMetricsInitialized;
            if (_state != TerminalState.OpenLauncher)
            {
                _launcherSuggestionContentHeight = 0f;
                _launcherHistoryContentHeight = 0f;
            }
            try
            {
                switch (_state)
                {
                    case TerminalState.OpenSmall:
                    {
                        _launcherMetricsInitialized = false;
                        _realWindowHeight = height * maxHeight * smallTerminalRatio;
                        _targetWindowHeight = _realWindowHeight;
                        break;
                    }
                    case TerminalState.OpenFull:
                    {
                        _launcherMetricsInitialized = false;
                        _realWindowHeight = height * maxHeight;
                        _targetWindowHeight = _realWindowHeight;
                        break;
                    }
                    case TerminalState.OpenLauncher:
                    {
                        LauncherLayoutMetrics computedMetrics = _launcherSettings.ComputeMetrics(
                            width,
                            height
                        );
                        float reservedEstimate = Mathf.Max(
                            _launcherSettings.inputReservePixels,
                            48f
                        );
                        float estimatedMinimumHeight =
                            (computedMetrics.InsetPadding * 2f) + reservedEstimate;
                        _launcherMetrics = computedMetrics;
                        _launcherMetricsInitialized = true;
                        _launcherSuggestionContentHeight = 0f;
                        _launcherHistoryContentHeight = 0f;
                        _realWindowHeight = _launcherMetrics.Height;
                        if (!wasLauncher)
                        {
                            _targetWindowHeight = Mathf.Clamp(
                                estimatedMinimumHeight,
                                0f,
                                _launcherMetrics.Height
                            );
                        }
                        else
                        {
                            _targetWindowHeight = Mathf.Clamp(
                                _targetWindowHeight,
                                0f,
                                _launcherMetrics.Height
                            );
                        }
                        break;
                    }
                    default:
                    {
                        _launcherMetricsInitialized = false;
                        _realWindowHeight = height * maxHeight * smallTerminalRatio;
                        _targetWindowHeight = 0;
                        break;
                    }
                }
            }
            finally
            {
                if (!Mathf.Approximately(oldTargetHeight, _targetWindowHeight))
                {
                    bool snapHeight =
                        (_launcherMetricsInitialized || wasLauncher) && _launcherMetrics.SnapOpen;
                    if (snapHeight)
                    {
                        _currentWindowHeight = _targetWindowHeight;
                        _isAnimating = false;
                    }
                    else
                    {
                        StartHeightAnimation();
                    }
                }
            }
        }

        private void SetupUI()
        {
            if (disableUIForTests)
            {
                return;
            }
            if (_uiDocument == null)
            {
                _uiDocument = GetComponent<UIDocument>();
            }
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

            uiRoot.Clear();
            VisualElement root = new();
            uiRoot.Add(root);
            root.name = TerminalRootName;
            root.AddToClassList("terminal-root");

            InitializeTheme(root);
            InitializeFont();
            // Ensure a font is set after initialization
            if (CurrentFont != null)
            {
                SetFont(CurrentFont, persist: false);
            }

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
            _uiDocument.rootVisualElement.style.height = new StyleLength(_realWindowHeight);
            _terminalContainer.style.height = new StyleLength(_realWindowHeight);
            root.Add(_terminalContainer);

            _logScrollView = new ScrollView();
            InitializeScrollView(_logScrollView);
            _logScrollView.name = "LogScrollView";
            _logScrollView.AddToClassList("log-scroll-view");
            _terminalContainer.Add(_logScrollView);
            _logViewport = _logScrollView.contentViewport;
            if (_logViewport != null)
            {
                _logViewport.style.flexGrow = 1f;
                _logViewport.style.flexShrink = 1f;
                _logViewport.style.minHeight = 0f;
                _logViewport.style.overflow = Overflow.Hidden;
            }
            VisualElement logContent = _logScrollView.contentContainer;
            logContent.style.flexDirection = FlexDirection.Column;
            logContent.style.alignItems = Align.Stretch;
            logContent.style.minHeight = 0f;
            logContent.RegisterCallback<GeometryChangedEvent>(OnLogContentGeometryChanged);

            _autoCompleteContainer = new ScrollView(ScrollViewMode.Horizontal)
            {
                name = "AutoCompletePopup",
            };
            _autoCompleteContainer.AddToClassList("autocomplete-popup");
            _terminalContainer.Add(_autoCompleteContainer);
            _autoCompleteViewport = _autoCompleteContainer.contentViewport;
            if (_autoCompleteViewport != null)
            {
                _autoCompleteViewport.style.flexDirection = FlexDirection.Row;
                _autoCompleteViewport.style.flexGrow = 0f;
                _autoCompleteViewport.style.flexShrink = 0f;
                _autoCompleteViewport.style.minHeight = 0f;
                _autoCompleteViewport.style.overflow = Overflow.Visible;
                _autoCompleteViewport.RegisterCallback<GeometryChangedEvent>(
                    OnAutoCompleteGeometryChanged
                );
            }
            VisualElement autoContent = _autoCompleteContainer.contentContainer;
            autoContent.style.flexDirection = FlexDirection.Row;
            autoContent.style.alignItems = Align.Center;
            autoContent.style.minHeight = 0f;
            autoContent.style.justifyContent = Justify.FlexStart;
            autoContent.style.flexWrap = Wrap.NoWrap;
            autoContent.RegisterCallback<GeometryChangedEvent>(OnAutoCompleteGeometryChanged);

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
                    if (
                        context._commandIssuedThisFrame
                        || Array.Exists(
                            context._inputHandlers,
                            handler => handler.ShouldHandleInputThisFrame
                        )
                    )
                    {
                        if (!string.Equals(context._commandInput.value, context._input.CommandText))
                        {
                            context._commandInput.value = context._input.CommandText;
                        }
                        // Ensure subsequent user keystrokes (e.g., space) trigger recompute
                        // even if this event was caused by programmatic text changes (Tab, etc.).
                        context._isCommandFromCode = false;
                        evt.StopPropagation();
                        return;
                    }

                    // Assign input text
                    context._input.CommandText = evt.newValue;

                    // If the user just typed a space right after a recognized command name,
                    // proactively clear the hint bar so stale command-name suggestions disappear
                    // before argument-context suggestions are computed/shown.
                    try
                    {
                        string prev = evt.previousValue ?? string.Empty;
                        string curr = evt.newValue ?? string.Empty;
                        bool justTypedSpace = curr.EndsWith(" ") && curr.Length == prev.Length + 1;
                        if (justTypedSpace && Terminal.Shell != null)
                        {
                            string check = curr;
                            // Remove trailing space(s) to isolate the command token
                            if (check.NeedsTrim())
                            {
                                check = check.TrimEnd();
                            }

                            if (CommandShell.TryEatArgument(ref check, out CommandArg cmd))
                            {
                                if (Terminal.Shell.Commands.ContainsKey(cmd.contents))
                                {
                                    // Clear existing suggestions immediately
                                    context._lastCompletionIndex = null;
                                    context._previousLastCompletionIndex = null;
                                    context._lastCompletionBuffer.Clear();
                                    context._autoCompleteContainer?.Clear();
                                }
                            }
                        }
                    }
                    catch
                    { /* non-fatal UI hint clearing */
                    }

                    context._runButton.style.display =
                        context.showGUIButtons
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

            _inputContainer.Add(_commandInput);
            _textInput = _commandInput.Q<VisualElement>("unity-text-input");

            _stateButtonContainer = new VisualElement { name = "StateButtonContainer" };
            _stateButtonContainer.AddToClassList("state-button-container");
            root.Add(_stateButtonContainer);
            RefreshStateButtons();
        }

        private void InitializeTheme(VisualElement root)
        {
            if (_themePack == null)
            {
                Debug.LogWarning("No theme pack assigned, cannot initialize theme.", this);
                return;
            }

            if (root != null)
            {
                for (int i = root.styleSheets.count - 1; 0 <= i; --i)
                {
                    StyleSheet styleSheet = root.styleSheets[i];
                    if (
                        styleSheet == null
                        || styleSheet.name.Contains("Theme", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        root.styleSheets.Remove(styleSheet);
                    }
                }

                foreach (StyleSheet styleSheet in _themePack._themes)
                {
                    if (styleSheet == null)
                    {
                        continue;
                    }

                    root.styleSheets.Add(styleSheet);
                }
            }
            else
            {
                Debug.LogWarning(
                    "No root element assigned, theme initialization may be broken.",
                    this
                );
            }

            _runtimeTheme = _persistedTheme;
            List<string> themeNames = _themePack._themeNames;
            if (themeNames.Contains(_runtimeTheme))
            {
                return;
            }

            if (themeNames is { Count: > 0 })
            {
                string runtimeTheme = null;
                foreach (string themeName in themeNames)
                {
                    if (
                        themeName != null
                        && themeName.Contains("dark", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        runtimeTheme = themeName;
                        break;
                    }
                }

                _runtimeTheme = runtimeTheme;
                if (_runtimeTheme == null)
                {
                    foreach (string themeName in themeNames)
                    {
                        if (
                            themeName != null
                            && themeName.Contains("light", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            runtimeTheme = themeName;
                            break;
                        }
                    }

                    _runtimeTheme = runtimeTheme;
                }
                if (_runtimeTheme == null && themeNames.Count > 0)
                {
                    foreach (string themeName in themeNames)
                    {
                        if (themeName != null)
                        {
                            _runtimeTheme = themeName;
                            break;
                        }
                    }
                }
                Debug.LogWarning($"Persisted theme not found, defaulting to '{_runtimeTheme}'.");
            }
            else
            {
                Debug.LogWarning("No available terminal themes.", this);
            }
        }

        // Support method for tests and tooling to inject theme/font packs before enabling
        public void InjectPacks(TerminalThemePack themePack, TerminalFontPack fontPack)
        {
            _themePack = themePack;
            _fontPack = fontPack;
        }

        private void InitializeFont()
        {
            if (_fontPack == null)
            {
                Debug.LogWarning("No font pack assigned, cannot initialize font.", this);
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
                Font runtimeFont = null;
                foreach (Font font in loadedFonts)
                {
                    if (
                        font != null
                        && font.name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                        && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        runtimeFont = font;
                        break;
                    }
                }

                _runtimeFont = runtimeFont;
                if (_runtimeFont == null)
                {
                    foreach (Font font in loadedFonts)
                    {
                        if (
                            font != null
                            && font.name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            runtimeFont = font;
                            break;
                        }
                    }

                    _runtimeFont = runtimeFont;
                }
                if (_runtimeFont == null)
                {
                    foreach (Font font in loadedFonts)
                    {
                        if (
                            font != null
                            && font.name.Contains("Regular", StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            runtimeFont = font;
                            break;
                        }
                    }

                    _runtimeFont = runtimeFont;
                }
                if (_runtimeFont == null && loadedFonts.Count > 0)
                {
                    foreach (Font font in loadedFonts)
                    {
                        if (font != null)
                        {
                            runtimeFont = font;
                            break;
                        }
                    }

                    _runtimeFont = runtimeFont;
                }
            }

            if (_runtimeFont == null)
            {
                Debug.LogWarning("No font assigned, defaulting to Courier New 16pt", this);
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

        private void ApplyStandardLayout(float screenWidth)
        {
            VisualElement rootElement = _uiDocument.rootVisualElement;
            rootElement.style.width = screenWidth;
            rootElement.style.height = _currentWindowHeight;

            _terminalContainer.EnableInClassList("terminal-container--launcher", false);
            _terminalContainer.style.width = screenWidth;
            _terminalContainer.style.height = _currentWindowHeight;
            _terminalContainer.style.left = 0;
            _terminalContainer.style.top = 0;
            _terminalContainer.style.position = Position.Relative;
            _terminalContainer.style.paddingLeft = 0;
            _terminalContainer.style.paddingRight = 0;
            _terminalContainer.style.paddingTop = 0;
            _terminalContainer.style.paddingBottom = 0;
            _terminalContainer.style.marginLeft = 0;
            _terminalContainer.style.marginRight = 0;
            _terminalContainer.style.marginTop = 0;
            _terminalContainer.style.marginBottom = 0;
            _terminalContainer.style.borderTopLeftRadius = 0;
            _terminalContainer.style.borderTopRightRadius = 0;
            _terminalContainer.style.borderBottomLeftRadius = 0;
            _terminalContainer.style.borderBottomRightRadius = 0;
            _terminalContainer.style.justifyContent = Justify.FlexStart;
            _terminalContainer.style.alignItems = Align.Stretch;

            _logScrollView.style.marginTop = 0;
            _logScrollView.style.height = new StyleLength(StyleKeyword.Null);
            _logScrollView.style.maxHeight = new StyleLength(StyleKeyword.Null);
            _logScrollView.style.minHeight = new StyleLength(StyleKeyword.Null);
            _logScrollView.style.display = DisplayStyle.Flex;
            _logScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;

            _autoCompleteContainer.style.position = Position.Relative;
            _autoCompleteContainer.style.left = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.top = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.width = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.maxHeight = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.maxWidth = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.height = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.minHeight = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.marginBottom = 0;
            _autoCompleteContainer.style.marginTop = 0;
            _autoCompleteContainer.style.marginLeft = 0;
            _autoCompleteContainer.style.marginRight = 0;
            _autoCompleteContainer.style.flexGrow = StyleKeyword.Null;
            _autoCompleteContainer.style.flexShrink = StyleKeyword.Null;
            _autoCompleteContainer.style.alignSelf = StyleKeyword.Null;
            _inputContainer.style.marginBottom = 0;

            EnsureChildOrder(
                _terminalContainer,
                _logScrollView,
                _autoCompleteContainer,
                _inputContainer
            );
        }

        private void ApplyLauncherLayout(float screenWidth, float screenHeight)
        {
            VisualElement rootElement = _uiDocument.rootVisualElement;
            rootElement.style.width = screenWidth;
            rootElement.style.height = screenHeight;

            _terminalContainer.EnableInClassList("terminal-container--launcher", true);
            _terminalContainer.style.width = _launcherMetrics.Width;
            _terminalContainer.style.height = _currentWindowHeight;
            _terminalContainer.style.left = _launcherMetrics.Left;
            _terminalContainer.style.top = _launcherMetrics.Top;
            _terminalContainer.style.position = Position.Absolute;
            _terminalContainer.style.justifyContent = Justify.FlexStart;
            _terminalContainer.style.alignItems = Align.Stretch;
            _terminalContainer.style.flexDirection = FlexDirection.Column;

            float horizontalPadding = _launcherMetrics.InsetPadding;
            float verticalPadding = Mathf.Max(4f, _launcherMetrics.InsetPadding * 0.5f);
            _terminalContainer.style.paddingLeft = horizontalPadding;
            _terminalContainer.style.paddingRight = horizontalPadding;
            _terminalContainer.style.paddingTop = verticalPadding;
            _terminalContainer.style.paddingBottom = verticalPadding;
            _terminalContainer.style.marginLeft = 0;
            _terminalContainer.style.marginRight = 0;
            _terminalContainer.style.marginTop = 0;
            _terminalContainer.style.marginBottom = 0;

            float cornerRadius = _launcherMetrics.CornerRadius;
            _terminalContainer.style.borderTopLeftRadius = cornerRadius;
            _terminalContainer.style.borderTopRightRadius = cornerRadius;
            _terminalContainer.style.borderBottomLeftRadius = cornerRadius;
            _terminalContainer.style.borderBottomRightRadius = cornerRadius;
            _terminalContainer.style.overflow = Overflow.Visible;

            _inputContainer.style.marginBottom = 0;
            _autoCompleteContainer.style.marginBottom = 0;

            if (_launcherMetrics.HistoryHeight > 0f)
            {
                _logScrollView.style.display = DisplayStyle.Flex;
                _logScrollView.style.height = _launcherMetrics.HistoryHeight;
                _logScrollView.style.maxHeight = _launcherMetrics.HistoryHeight;
                _logScrollView.style.minHeight = 0;
                _logScrollView.style.marginTop = Mathf.Max(6f, verticalPadding * 0.35f);
            }
            else
            {
                _logScrollView.style.display = DisplayStyle.None;
                _logScrollView.style.height = 0;
                _logScrollView.style.maxHeight = 0;
                _launcherHistoryContentHeight = 0f;
                _logScrollView.style.marginTop = 0;
            }

            _logScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;

            _autoCompleteContainer.style.position = Position.Relative;
            _autoCompleteContainer.style.left = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.top = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.width = new StyleLength(StyleKeyword.Null);
            _autoCompleteContainer.style.maxHeight = _launcherMetrics.HistoryHeight;
            _autoCompleteContainer.style.display = DisplayStyle.None;
            _autoCompleteContainer.style.marginTop = 0;
            _autoCompleteContainer.style.marginBottom = 0;
            _autoCompleteContainer.style.marginLeft = 0;
            _autoCompleteContainer.style.marginRight = 0;
            _autoCompleteContainer.style.flexGrow = 0;
            _autoCompleteContainer.style.flexShrink = 0;

            EnsureChildOrder(
                _terminalContainer,
                _inputContainer,
                _autoCompleteContainer,
                _logScrollView
            );
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

            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            if (IsLauncherActive && _launcherMetricsInitialized)
            {
                ApplyLauncherLayout(screenWidth, screenHeight);
                UpdateLauncherLayoutMetrics();
            }
            else
            {
                ApplyStandardLayout(screenWidth);
            }

            _terminalContainer.style.height = _currentWindowHeight;

            DisplayStyle commandInputStyle =
                !IsLauncherActive && _currentWindowHeight <= 30
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;

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
                && !IsLauncherActive
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

            if (IsLauncherActive && _launcherMetricsInitialized)
            {
                RefreshLauncherHistory();
                return;
            }

            VisualElement content = _logScrollView.contentContainer;
            _logScrollView.style.display = DisplayStyle.Flex;
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
                    LogItem logItem = logs[i];
                    switch (item)
                    {
                        case TextField logText:
                        {
                            ApplyLogStyling(logText, logItem);
                            logText.value = logItem.message;
                            break;
                        }
                        case Label logLabel:
                        {
                            ApplyLogStyling(logLabel, logItem);
                            logLabel.text = logItem.message;
                            break;
                        }
                        case Button button:
                        {
                            ApplyLogStyling(button, logItem);
                            button.text = logItem.message;
                            break;
                        }
                    }

                    item.style.opacity = 1f;
                    item.style.display = DisplayStyle.Flex;
                }

                if (logs.Count == content.childCount)
                {
                    _lastSeenBufferVersion = Terminal.Buffer.Version;
                }
            }
        }

        private void RefreshLauncherHistory()
        {
            if (_logScrollView == null)
            {
                return;
            }

            VisualElement content = _logScrollView.contentContainer;
            CommandHistory history = Terminal.History;

            if (history == null)
            {
                _launcherHistoryEntries.Clear();
                _logScrollView.style.display = DisplayStyle.None;
                for (int i = 0; i < content.childCount; ++i)
                {
                    content[i].style.display = DisplayStyle.None;
                }

                _lastRenderedLauncherHistoryVersion = -1;
                _cachedLauncherScrollVersion = -1;
                _cachedLauncherScrollValue = 0f;
                _restoreLauncherScrollPending = false;
                _launcherHistoryContentHeight = 0f;
                _needsScrollToEnd = false;
                return;
            }

            history.CopyEntriesTo(_launcherHistoryEntries);
            long historyVersion = history.Version;

            int entryCount = _launcherHistoryEntries.Count;
            int visibleCount = Mathf.Min(_launcherMetrics.HistoryVisibleEntryCount, entryCount);

            if (_launcherMetrics.HistoryHeight <= 0f || visibleCount <= 0)
            {
                _logScrollView.style.display = DisplayStyle.None;
                for (int i = 0; i < content.childCount; ++i)
                {
                    content[i].style.display = DisplayStyle.None;
                }

                _lastRenderedLauncherHistoryVersion = historyVersion;
                _cachedLauncherScrollVersion = historyVersion;
                _cachedLauncherScrollValue = 0f;
                _restoreLauncherScrollPending = false;
                _launcherHistoryContentHeight = 0f;
                _needsScrollToEnd = false;
                return;
            }

            _logScrollView.style.display = DisplayStyle.Flex;

            if (content.childCount < visibleCount)
            {
                for (int i = content.childCount; i < visibleCount; ++i)
                {
                    Label logText = new();
                    logText.AddToClassList("terminal-output-label");
                    content.Add(logText);
                }
            }

            for (int i = visibleCount; i < content.childCount; ++i)
            {
                content[i].style.display = DisplayStyle.None;
            }

            float denominator = Mathf.Max(1f, visibleCount - 1f);
            for (int i = 0; i < visibleCount; ++i)
            {
                int historyIndex = entryCount - 1 - i;
                CommandHistoryEntry entry = _launcherHistoryEntries[historyIndex];
                VisualElement element = content[i];
                LogItem logItem = new(TerminalLogType.Input, entry.Text, string.Empty);

                switch (element)
                {
                    case TextField logText:
                    {
                        ApplyLogStyling(logText, logItem);
                        logText.value = entry.Text;
                        break;
                    }
                    case Label logLabel:
                    {
                        ApplyLogStyling(logLabel, logItem);
                        logLabel.text = entry.Text;
                        break;
                    }
                    case Button button:
                    {
                        ApplyLogStyling(button, logItem);
                        button.text = entry.Text;
                        break;
                    }
                }

                element.style.display = DisplayStyle.Flex;
                float fade =
                    visibleCount == 1
                        ? 1f
                        : Mathf.Pow(1f - (i / denominator), _launcherMetrics.HistoryFadeExponent);
                element.style.opacity = Mathf.Clamp01(fade);
            }

            bool historyChanged = historyVersion != _lastRenderedLauncherHistoryVersion;
            bool restoreRequested = _restoreLauncherScrollPending;
            float? targetScroll = null;

            if (restoreRequested)
            {
                float targetValue = _cachedLauncherScrollValue;
                if (_cachedLauncherScrollVersion != historyVersion)
                {
                    targetValue = 0f;
                }

                _cachedLauncherScrollVersion = historyVersion;
                _cachedLauncherScrollValue = targetValue;
                targetScroll = targetValue;
                _restoreLauncherScrollPending = false;
            }
            else if (historyChanged)
            {
                _cachedLauncherScrollVersion = historyVersion;
                _cachedLauncherScrollValue = 0f;
                targetScroll = 0f;
            }

            if (targetScroll.HasValue)
            {
                ScheduleLauncherScroll(targetScroll.Value);
            }

            _lastRenderedLauncherHistoryVersion = historyVersion;
            _needsScrollToEnd = false;
        }

        private static void ApplyLogStyling(VisualElement logText, LogItem log)
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

        private void ScrollToEnd()
        {
            if (0 < _logScrollView?.verticalScroller.highValue)
            {
                _logScrollView.verticalScroller.value = _logScrollView.verticalScroller.highValue;
            }
        }

        private void CacheLauncherScrollPosition()
        {
            if (_logScrollView?.verticalScroller == null)
            {
                return;
            }

            float highValue = _logScrollView.verticalScroller.highValue;
            float currentValue = Mathf.Clamp(
                _logScrollView.verticalScroller.value,
                0f,
                highValue
            );
            _cachedLauncherScrollValue = currentValue;
            _cachedLauncherScrollVersion = Terminal.History?.Version ?? -1;
        }

        private void ScheduleLauncherScroll(float targetValue)
        {
            if (_logScrollView?.verticalScroller == null)
            {
                return;
            }

            float clampedTarget = Mathf.Clamp(
                targetValue,
                0f,
                _logScrollView.verticalScroller.highValue
            );

            _logScrollView
                .schedule
                .Execute(() =>
                {
                    if (_logScrollView?.verticalScroller == null)
                    {
                        return;
                    }

                    float highValue = _logScrollView.verticalScroller.highValue;
                    _logScrollView.verticalScroller.value = Mathf.Clamp(
                        clampedTarget,
                        0f,
                        highValue
                    );
                })
                .ExecuteLater(0);
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

            float desiredTop = _currentWindowHeight;
            float desiredLeft = 2f;
            float desiredWidth = Screen.width;
            if (IsLauncherActive && _launcherMetricsInitialized)
            {
                desiredTop = _launcherMetrics.Top + _currentWindowHeight + 12f;
                desiredLeft = _launcherMetrics.Left;
                desiredWidth = _launcherMetrics.Width;
            }

            _stateButtonContainer.style.top = desiredTop;
            _stateButtonContainer.style.left = desiredLeft;
            _stateButtonContainer.style.width = desiredWidth;
            _stateButtonContainer.style.display = showGUIButtons
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _stateButtonContainer.style.justifyContent =
                IsLauncherActive && _launcherMetricsInitialized
                    ? Justify.Center
                    : Justify.FlexStart;

            Button primaryButton;
            Button secondaryButton;
            Button launcherButton;
            EnsureButtons(out primaryButton, out secondaryButton, out launcherButton);

            DisplayStyle buttonDisplay = showGUIButtons ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateButton(primaryButton, GetPrimaryLabel(), _state == TerminalState.OpenSmall);
            UpdateButton(secondaryButton, GetSecondaryLabel(), _state == TerminalState.OpenFull);
            UpdateButton(launcherButton, launcherButtonText, IsLauncherActive);

            return;

            void EnsureButtons(out Button primary, out Button secondary, out Button launcher)
            {
                while (_stateButtonContainer.childCount < 3)
                {
                    int index = _stateButtonContainer.childCount;
                    Button button = index switch
                    {
                        0 => new Button(FirstClicked) { name = "StateButton1" },
                        1 => new Button(SecondClicked) { name = "StateButton2" },
                        _ => new Button(LauncherClicked) { name = "StateButton3" },
                    };
                    button.AddToClassList("terminal-button");
                    _stateButtonContainer.Add(button);
                }

                primary = _stateButtonContainer[0] as Button;
                secondary = _stateButtonContainer[1] as Button;
                launcher = _stateButtonContainer[2] as Button;
            }

            string GetPrimaryLabel()
            {
                return _state switch
                {
                    TerminalState.Closed => smallButtonText,
                    TerminalState.OpenSmall => closeButtonText,
                    TerminalState.OpenFull => closeButtonText,
                    TerminalState.OpenLauncher => closeButtonText,
                    _ => string.Empty,
                };
            }

            string GetSecondaryLabel()
            {
                return _state switch
                {
                    TerminalState.Closed => fullButtonText,
                    TerminalState.OpenSmall => fullButtonText,
                    TerminalState.OpenFull => smallButtonText,
                    TerminalState.OpenLauncher => fullButtonText,
                    _ => string.Empty,
                };
            }

            void UpdateButton(Button button, string text, bool isActive)
            {
                if (button == null)
                {
                    return;
                }

                bool shouldShow =
                    buttonDisplay == DisplayStyle.Flex && !string.IsNullOrWhiteSpace(text);
                button.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
                if (shouldShow)
                {
                    button.text = text;
                }
                button.EnableInClassList("terminal-button-active", shouldShow && isActive);
            }

            void FirstClicked()
            {
                switch (_state)
                {
                    case TerminalState.Closed:
                        ToggleSmall();
                        break;
                    case TerminalState.OpenSmall:
                    case TerminalState.OpenFull:
                    case TerminalState.OpenLauncher:
                        Close();
                        break;
                }
            }

            void SecondClicked()
            {
                switch (_state)
                {
                    case TerminalState.Closed:
                    case TerminalState.OpenSmall:
                    case TerminalState.OpenLauncher:
                        ToggleFull();
                        break;
                    case TerminalState.OpenFull:
                        ToggleSmall();
                        break;
                }
            }

            void LauncherClicked()
            {
                ToggleLauncher();
            }
        }

        private static void EnsureChildOrder(
            VisualElement parent,
            params VisualElement[] orderedChildren
        )
        {
            if (parent == null)
            {
                return;
            }

            int insertIndex = 0;
            foreach (VisualElement child in orderedChildren)
            {
                if (child == null || child.parent != parent)
                {
                    continue;
                }

                int currentIndex = parent.IndexOf(child);
                if (currentIndex != insertIndex)
                {
                    parent.Remove(child);
                    parent.Insert(insertIndex, child);
                }

                insertIndex++;
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
            SetRuntimeFont(font);
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
            if (currentFont != font)
            {
                Debug.Log(
                    currentFont == null
                        ? $"Setting font to {font.name}."
                        : $"Changing font from {currentFont.name} to {font.name}.",
                    this
                );
            }

            if (persist)
            {
                _persistedFont = font;
            }

            return;

            void SetRuntimeFont(Font toSet)
            {
                if (toSet == null)
                {
                    return;
                }

                if (!Application.isPlaying)
                {
                    return;
                }

                if (_uiDocument == null)
                {
                    return;
                }

                VisualElement root = _uiDocument.rootVisualElement;
                if (root == null)
                {
                    return;
                }

                root.style.unityFontDefinition = new StyleFontDefinition(toSet);
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
            string friendlyThemeName = ThemeNameHelper.GetFriendlyThemeName(theme);
            SetRuntimeTheme();
            if (
                !persist
                && string.Equals(
                    friendlyThemeName,
                    CurrentFriendlyTheme,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return;
            }

            if (!IsValidTheme(out string validatedTheme))
            {
                return;
            }

            string currentTheme = ThemeNameHelper.GetFriendlyThemeName(CurrentTheme);
            _runtimeTheme = validatedTheme;
            if (!string.Equals(currentTheme, friendlyThemeName, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"Changing theme from {currentTheme} to {friendlyThemeName}.", this);
            }

            if (persist)
            {
                _persistedTheme = validatedTheme;
            }

            return;

            bool IsValidTheme(out string validTheme)
            {
                if (string.IsNullOrWhiteSpace(theme) || _themePack == null)
                {
                    validTheme = default;
                    return false;
                }

                List<string> themeNames = _themePack._themeNames;
                foreach (string themeName in themeNames)
                {
                    if (string.Equals(themeName, theme, StringComparison.OrdinalIgnoreCase))
                    {
                        validTheme = themeName;
                        return true;
                    }
                }

                foreach (string themeName in ThemeNameHelper.GetPossibleThemeNames(theme))
                {
                    foreach (string existingThemeName in themeNames)
                    {
                        if (
                            string.Equals(
                                existingThemeName,
                                themeName,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            validTheme = existingThemeName;
                            return true;
                        }
                    }
                }

                validTheme = default;
                return false;
            }

            void SetRuntimeTheme()
            {
                if (!Application.isPlaying)
                {
                    return;
                }

                if (!IsValidTheme(out validatedTheme))
                {
                    return;
                }

                if (_uiDocument == null)
                {
                    return;
                }

                VisualElement terminalRoot = _uiDocument.rootVisualElement?.Q<VisualElement>(
                    TerminalRootName
                );
                if (terminalRoot == null)
                {
                    return;
                }

                List<string> loadedThemes = new List<string>();
                foreach (string cls in terminalRoot.GetClasses())
                {
                    if (ThemeNameHelper.IsThemeName(cls))
                    {
                        loadedThemes.Add(cls);
                    }
                }
                for (int i = 0; i < loadedThemes.Count; ++i)
                {
                    terminalRoot.RemoveFromClassList(loadedThemes[i]);
                }

                terminalRoot.AddToClassList(validatedTheme);
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

        public void ToggleLauncher()
        {
            ToggleState(TerminalState.OpenLauncher);
        }

        // Internal test hooks
        internal TerminalState CurrentStateForTests => _state;

        internal bool LauncherMetricsInitializedForTests => _launcherMetricsInitialized;

        internal ScrollView LogScrollViewForTests => _logScrollView;

        internal void SetLauncherMetricsForTests(
            LauncherLayoutMetrics metrics,
            bool initialized = true
        )
        {
            _launcherMetrics = metrics;
            _launcherMetricsInitialized = initialized;
        }

        internal LauncherLayoutMetrics LauncherMetricsForTests => _launcherMetrics;

        internal float TargetWindowHeightForTests => _targetWindowHeight;

        internal float CurrentWindowHeightForTests => _currentWindowHeight;

        internal void SetWindowHeightsForTests(
            float currentHeight,
            float targetHeight,
            bool isAnimating = false
        )
        {
            _currentWindowHeight = currentHeight;
            _targetWindowHeight = targetHeight;
            _isAnimating = isAnimating;
        }

        internal void SetLogScrollViewForTests(ScrollView scrollView)
        {
            _logScrollView = scrollView;
        }

        internal void RefreshLauncherHistoryForTests()
        {
            RefreshLauncherHistory();
        }

        internal void ResetWindowForTests()
        {
            ResetWindowIdempotent();
        }

        private void ResetLauncherSettings()
        {
            Debug.LogWarning(
                "Launcher settings reset to defaults. This action is destructive.",
                this
            );
            _launcherSettings = new TerminalLauncherSettings();
            ResetWindowIdempotent();
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
                _lastKnownCommandText = _input.CommandText ?? string.Empty;
                _lastCompletionBufferTempCache.Clear();
                int caret =
                    _commandInput != null
                        ? _commandInput.cursorIndex
                        : (_lastKnownCommandText?.Length ?? 0);
                Terminal.AutoComplete?.Complete(
                    _lastKnownCommandText,
                    caret,
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

        private void RefreshStateButtons()
        {
            if (_stateButtonContainer == null)
            {
                return;
            }

            float desiredTop = _currentWindowHeight;
            float desiredLeft = 2f;
            float desiredWidth = Screen.width;
            if (IsLauncherActive && _launcherMetricsInitialized)
            {
                desiredTop = _launcherMetrics.Top + _currentWindowHeight + 12f;
                desiredLeft = _launcherMetrics.Left;
                desiredWidth = _launcherMetrics.Width;
            }

            _stateButtonContainer.style.top = desiredTop;
            _stateButtonContainer.style.left = desiredLeft;
            _stateButtonContainer.style.width = desiredWidth;
            _stateButtonContainer.style.display = showGUIButtons
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            _stateButtonContainer.style.justifyContent =
                IsLauncherActive && _launcherMetricsInitialized
                    ? Justify.Center
                    : Justify.FlexStart;

            Button primaryButton;
            Button secondaryButton;
            Button launcherButton;
            EnsureButtons(out primaryButton, out secondaryButton, out launcherButton);

            DisplayStyle buttonDisplay = showGUIButtons ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateButton(primaryButton, GetPrimaryLabel(), _state == TerminalState.OpenSmall);
            UpdateButton(secondaryButton, GetSecondaryLabel(), _state == TerminalState.OpenFull);
            UpdateButton(launcherButton, launcherButtonText, IsLauncherActive);

            return;

            void EnsureButtons(out Button primary, out Button secondary, out Button launcher)
            {
                while (_stateButtonContainer.childCount < 3)
                {
                    int index = _stateButtonContainer.childCount;
                    Button button = index switch
                    {
                        0 => new Button(FirstClicked) { name = "StateButton1" },
                        1 => new Button(SecondClicked) { name = "StateButton2" },
                        _ => new Button(LauncherClicked) { name = "StateButton3" },
                    };
                    button.AddToClassList("terminal-button");
                    _stateButtonContainer.Add(button);
                }

                primary = _stateButtonContainer[0] as Button;
                secondary = _stateButtonContainer[1] as Button;
                launcher = _stateButtonContainer[2] as Button;
            }

            string GetPrimaryLabel()
            {
                return _state switch
                {
                    TerminalState.Closed => smallButtonText,
                    TerminalState.OpenSmall => closeButtonText,
                    TerminalState.OpenFull => closeButtonText,
                    TerminalState.OpenLauncher => closeButtonText,
                    _ => string.Empty,
                };
            }

            string GetSecondaryLabel()
            {
                return _state switch
                {
                    TerminalState.Closed => fullButtonText,
                    TerminalState.OpenSmall => fullButtonText,
                    TerminalState.OpenFull => smallButtonText,
                    TerminalState.OpenLauncher => fullButtonText,
                    _ => string.Empty,
                };
            }

            void UpdateButton(Button button, string text, bool isActive)
            {
                if (button == null)
                {
                    return;
                }

                bool shouldShow =
                    buttonDisplay == DisplayStyle.Flex && !string.IsNullOrWhiteSpace(text);
                button.style.display = shouldShow ? DisplayStyle.Flex : DisplayStyle.None;
                if (shouldShow)
                {
                    button.text = text;
                }
                button.EnableInClassList("terminal-button-active", shouldShow && isActive);
            }

            void FirstClicked()
            {
                switch (_state)
                {
                    case TerminalState.Closed:
                        ToggleSmall();
                        break;
                    case TerminalState.OpenSmall:
                    case TerminalState.OpenFull:
                    case TerminalState.OpenLauncher:
                        Close();
                        break;
                }
            }

            void SecondClicked()
            {
                switch (_state)
                {
                    case TerminalState.Closed:
                    case TerminalState.OpenSmall:
                    case TerminalState.OpenLauncher:
                        ToggleFull();
                        break;
                    case TerminalState.OpenFull:
                        ToggleSmall();
                        break;
                }
            }

            void LauncherClicked()
            {
                ToggleLauncher();
            }
        }

        private void UpdateLauncherLayoutMetrics()
        {
            if (!IsLauncherActive || !_launcherMetricsInitialized)
            {
                return;
            }

            float padding = _launcherMetrics.InsetPadding;
            float inputHeight = Mathf.Max(_inputContainer.resolvedStyle.height, 0f);
            float availableWidth = Mathf.Max(0f, _launcherMetrics.Width - (padding * 2f));
            _autoCompleteContainer.style.width = availableWidth;
            _autoCompleteContainer.style.maxWidth = availableWidth;
            _autoCompleteContainer.style.alignSelf = Align.Stretch;
            _autoCompleteContainer.style.flexGrow = 0;
            _autoCompleteContainer.style.flexShrink = 0;
            _autoCompleteContainer.style.minHeight = 0f;

            bool hasSuggestions = false;
            int suggestionChildCount = _autoCompleteContainer.contentContainer.childCount;
            for (int i = 0; i < suggestionChildCount; ++i)
            {
                VisualElement suggestion = _autoCompleteContainer.contentContainer[i];
                if (suggestion == null)
                {
                    continue;
                }

                if (suggestion.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                hasSuggestions = true;
            }
            if (!hasSuggestions && suggestionChildCount > 0)
            {
                hasSuggestions = true;
            }
            if (hasSuggestions)
            {
                _autoCompleteContainer.style.display = DisplayStyle.Flex;
                _autoCompleteContainer.style.marginTop = LauncherAutoCompleteSpacing;
            }
            else
            {
                _autoCompleteContainer.style.display = DisplayStyle.None;
                _autoCompleteContainer.style.height = 0;
                if (_autoCompleteViewport != null)
                {
                    _autoCompleteViewport.style.height = 0;
                }
                _autoCompleteContainer.style.marginTop = 0;
                _launcherSuggestionContentHeight = 0f;
            }

            float effectiveSuggestionHeight = _launcherSuggestionContentHeight;
            if (effectiveSuggestionHeight <= 0f && hasSuggestions)
            {
                effectiveSuggestionHeight = LauncherEstimatedSuggestionRowHeight;
            }

            float suggestionsHeight = hasSuggestions
                ? Mathf.Clamp(effectiveSuggestionHeight, 0f, _launcherMetrics.HistoryHeight)
                : 0f;
            if (hasSuggestions)
            {
                _autoCompleteContainer.style.height = Mathf.Max(0f, suggestionsHeight);
                if (_autoCompleteViewport != null)
                {
                    _autoCompleteViewport.style.height = Mathf.Max(0f, suggestionsHeight);
                }
            }
            _autoCompleteContainer.style.marginBottom = 0;

            float spacingAboveLog = hasSuggestions
                ? LauncherAutoCompleteSpacing
                : Mathf.Max(LauncherAutoCompleteSpacing, padding * 0.25f);

            float reservedForSuggestions = hasSuggestions
                ? suggestionsHeight + spacingAboveLog
                : spacingAboveLog;

            VisualElement historyContent = _logScrollView.contentContainer;
            int visibleHistoryCount = 0;
            int historyChildCount = historyContent.childCount;
            for (int i = 0; i < historyChildCount; ++i)
            {
                VisualElement entry = historyContent[i];
                if (entry == null || entry.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }
                visibleHistoryCount++;
            }

            if (visibleHistoryCount == 0)
            {
                int pendingLogs = Terminal.History?.Count ?? 0;
                visibleHistoryCount = Mathf.Min(
                    pendingLogs,
                    _launcherMetrics.HistoryVisibleEntryCount
                );
                if (visibleHistoryCount == 0)
                {
                    _launcherHistoryContentHeight = 0f;
                }
            }

            float historyHeightFromContent =
                visibleHistoryCount > 0 ? _launcherHistoryContentHeight : 0f;
            if (float.IsNaN(historyHeightFromContent) || historyHeightFromContent < 0f)
            {
                historyHeightFromContent = 0f;
            }

            float estimatedHistoryHeight =
                visibleHistoryCount > 0
                    ? visibleHistoryCount * LauncherEstimatedHistoryRowHeight
                    : 0f;

            float desiredHistoryHeight =
                visibleHistoryCount > 0
                    ? Mathf.Min(
                        Mathf.Max(historyHeightFromContent, estimatedHistoryHeight),
                        _launcherMetrics.HistoryHeight
                    )
                    : 0f;
            if (desiredHistoryHeight < 0f)
            {
                desiredHistoryHeight = 0f;
            }

            float minimumHeight = padding * 2f + inputHeight + reservedForSuggestions;
            float desiredHeight = minimumHeight + desiredHistoryHeight;
            float clampedHeight = Mathf.Clamp(
                desiredHeight,
                minimumHeight,
                _launcherMetrics.Height
            );

            if (!Mathf.Approximately(clampedHeight, _targetWindowHeight))
            {
                _initialWindowHeight = Mathf.Clamp(
                    _currentWindowHeight,
                    minimumHeight,
                    _launcherMetrics.Height
                );
                _targetWindowHeight = clampedHeight;
                _animationTimer = 0f;
                _isAnimating = true;
                if (Mathf.Approximately(_initialWindowHeight, clampedHeight))
                {
                    _currentWindowHeight = clampedHeight;
                    _isAnimating = false;
                }
            }

            float availableForHistory =
                _currentWindowHeight - (padding * 2f) - inputHeight - reservedForSuggestions;
            availableForHistory = Mathf.Min(availableForHistory, _launcherMetrics.HistoryHeight);
            availableForHistory = Mathf.Max(0f, availableForHistory);

            if (availableForHistory <= 0.01f || _logScrollView.contentContainer.childCount == 0)
            {
                _logScrollView.style.display = DisplayStyle.None;
                _logScrollView.style.height = 0;
                _logScrollView.style.maxHeight = 0;
                _launcherHistoryContentHeight = 0f;
            }
            else
            {
                _logScrollView.style.display = DisplayStyle.Flex;
                _logScrollView.style.height = availableForHistory;
                _logScrollView.style.maxHeight = availableForHistory;
            }

            _logScrollView.style.marginTop = spacingAboveLog;
        }

        private void OnAutoCompleteGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            float newHeight = Mathf.Max(evt.newRect.height, 0f);
            if (float.IsNaN(newHeight))
            {
                newHeight = 0f;
            }

            bool isViewport = evt.target == _autoCompleteViewport;
            bool hasChildren = _autoCompleteContainer?.contentContainer?.childCount > 0;
            if (isViewport && newHeight <= 0f && hasChildren)
            {
                return;
            }

            if (!Mathf.Approximately(newHeight, _launcherSuggestionContentHeight))
            {
                _launcherSuggestionContentHeight = newHeight;
            }
        }

        private void OnLogContentGeometryChanged(GeometryChangedEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            float newHeight = Mathf.Max(evt.newRect.height, 0f);
            if (float.IsNaN(newHeight))
            {
                newHeight = 0f;
            }

            if (!Mathf.Approximately(newHeight, _launcherHistoryContentHeight))
            {
                _launcherHistoryContentHeight = newHeight;
            }
        }

        private void StartHeightAnimation()
        {
            if (Mathf.Approximately(_currentWindowHeight, _targetWindowHeight))
            {
                _isAnimating = false;
                return;
            }

            _initialWindowHeight = _currentWindowHeight;
            _animationTimer = 0f;
            _isAnimating = true;
        }

        private void HandleHeightAnimation()
        {
            if (!_isAnimating)
            {
                return;
            }

            _animationTimer += Time.unscaledDeltaTime;

            AnimationCurve selectedCurve;
            float animationDuration;
            bool isExpanding = _targetWindowHeight > _initialWindowHeight;

            bool useLauncherTiming =
                IsLauncherActive || _previousState == TerminalState.OpenLauncher;

            if (isExpanding)
            {
                selectedCurve = easeOutCurve;
                animationDuration = useLauncherTiming
                    ? Mathf.Max(_launcherMetrics.AnimationDuration, 0.0001f)
                    : easeOutTime;
            }
            else
            {
                selectedCurve = easeInCurve;
                animationDuration = useLauncherTiming
                    ? Mathf.Max(_launcherMetrics.AnimationDuration, 0.0001f)
                    : easeInTime;
            }

            if (animationDuration <= 0f)
            {
                _currentWindowHeight = _targetWindowHeight;
                _isAnimating = false;
                return;
            }

            float normalizedTime = Mathf.Clamp01(_animationTimer / animationDuration);

            float curveValue = selectedCurve.Evaluate(normalizedTime);

            _currentWindowHeight = Mathf.LerpUnclamped(
                _initialWindowHeight,
                _targetWindowHeight,
                curveValue
            );

            if (isExpanding)
            {
                _currentWindowHeight = Mathf.Clamp(
                    _currentWindowHeight,
                    _initialWindowHeight,
                    _targetWindowHeight
                );
            }
            else
            {
                _currentWindowHeight = Mathf.Clamp(
                    _currentWindowHeight,
                    _targetWindowHeight,
                    _initialWindowHeight
                );
            }

            if (
                Mathf.Approximately(_currentWindowHeight, _targetWindowHeight)
                || animationDuration <= _animationTimer
            )
            {
                _currentWindowHeight = _targetWindowHeight;
                _isAnimating = false;
            }
        }

        private static void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            Terminal.Buffer?.EnqueueUnityLog(message, stackTrace, (TerminalLogType)type);
        }
    }
}
