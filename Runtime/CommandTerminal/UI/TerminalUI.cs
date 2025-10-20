namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using Attributes;
    using Extensions;
    using Helper;
    using Input;
    using Themes;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.DxCommandTerminal.Backend;
    using WallstopStudios.DxCommandTerminal.Backend.Profiles;
    using WallstopStudios.DxCommandTerminal.Service;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    [DisallowMultipleComponent]
    public sealed partial class TerminalUI : MonoBehaviour, ITerminalInputTarget
    {
        private const string TerminalRootName = "TerminalRoot";
        private const float LauncherAutoCompleteSpacing = 3f;
        private const float LauncherEstimatedSuggestionRowHeight = 32f;
        private const float LauncherEstimatedHistoryRowHeight = 28f;
        private const float LauncherInputFallbackHeight = 24f;
        private const float StandardEstimatedHistoryRowHeight = 24f;

        internal const float LauncherInputFallbackHeightForTests = LauncherInputFallbackHeight;
        internal const float LauncherAutoCompleteSpacingForTests = LauncherAutoCompleteSpacing;
        internal const float LauncherEstimatedHistoryRowHeightForTests =
            LauncherEstimatedHistoryRowHeight;

        private enum ScrollBarCaptureState
        {
            None = 0,
            DraggerActive = 1,
            TrackerActive = 2,
        }

        [Serializable]
        public sealed class RuntimeModeOption
        {
            [Tooltip("Unique identifier for this runtime mode option.")]
            public string id = "default";

            [Tooltip("Friendly label for inspector tooling.")]
            public string displayName = "Default";

#pragma warning disable CS0618 // Type or member is obsolete
            [Tooltip("Runtime mode flags applied when this option is active.")]
            public TerminalRuntimeModeFlags modes = TerminalRuntimeModeFlags.All;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        // Cache log callback to reduce allocations
        private static readonly Application.LogCallback UnityLogCallback = HandleUnityLog;

        public static TerminalUI Instance { get; private set; }

        private static ITerminalServiceLocator _serviceLocator = TerminalServiceLocator.Default;

        public static ITerminalServiceLocator ServiceLocator
        {
            get => _serviceLocator ?? TerminalServiceLocator.Default;
            set => _serviceLocator = value ?? TerminalServiceLocator.Default;
        }

        public static ITerminalProvider TerminalProvider
        {
            get => ServiceLocator.TerminalProvider;
            set => EnsureMutableLocator().TerminalProvider = value;
        }

        public static ITerminalRuntimeConfigurator RuntimeConfigurator
        {
            get => ServiceLocator.RuntimeConfigurator;
            set => EnsureMutableLocator().RuntimeConfigurator = value;
        }

        public static ITerminalInputProvider InputProvider
        {
            get => ServiceLocator.InputProvider;
            set => EnsureMutableLocator().InputProvider = value;
        }

        public static ITerminalRuntimeProvider RuntimeProvider
        {
            get => ServiceLocator.RuntimeProvider;
            set => EnsureMutableLocator().RuntimeProvider = value;
        }

        public static ITerminalRuntimePool RuntimePool
        {
            get => ServiceLocator.RuntimePool;
            set => EnsureMutableLocator().RuntimePool = value;
        }

        public static TerminalUI ActiveTerminal => TerminalProvider?.ActiveTerminal;

        public static IReadOnlyList<TerminalUI> ActiveTerminals =>
            TerminalProvider?.ActiveTerminals ?? System.Array.Empty<TerminalUI>();

        private ITerminalRuntimeScope RuntimeScope => ServiceLocator.RuntimeScope;

        private ITerminalRuntimePool RuntimePoolInstance => ServiceLocator.RuntimePool;

        private static MutableTerminalServiceLocator EnsureMutableLocator()
        {
            if (_serviceLocator is MutableTerminalServiceLocator mutable)
            {
                return mutable;
            }

            ITerminalServiceLocator currentLocator =
                _serviceLocator ?? TerminalServiceLocator.Default;
            MutableTerminalServiceLocator replacement = new MutableTerminalServiceLocator(
                currentLocator.TerminalProvider,
                currentLocator.RuntimeConfigurator,
                currentLocator.InputProvider,
                currentLocator.RuntimeProvider,
                currentLocator.RuntimeScope,
                currentLocator.RuntimeConfiguratorService,
                currentLocator.RuntimePool
            );
            _serviceLocator = replacement;
            return replacement;
        }

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
        [Tooltip("Optional configuration asset applied on Awake to seed runtime settings.")]
        private TerminalRuntimeProfile _runtimeProfile;

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

        [Header("Appearance")]
        [SerializeField]
        private TerminalHistoryFadeTargets _historyFadeTargets =
            TerminalHistoryFadeTargets.SmallTerminal
            | TerminalHistoryFadeTargets.FullTerminal
            | TerminalHistoryFadeTargets.Launcher;

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
        internal TerminalConfigurationAsset _terminalConfigurationAsset;

        internal ITerminalRuntimeFactory _runtimeFactoryOverrideForTests;

        internal List<TerminalLogType> _allowedLogTypes = new();
        internal List<TerminalLogType> _blockedLogTypes = new();

        [SerializeField]
        internal List<string> _allowedCommands = new();
        internal List<string> _blockedCommands = new();

        [SerializeField]
        internal TerminalFontPack _fontPack;

        [SerializeField]
        internal TerminalThemePack _themePack;

        [SerializeField]
        private TerminalAppearanceProfile _appearanceProfile;

        [SerializeField]
        private TerminalCommandProfile _commandProfile;

        [SerializeField]
        [Tooltip("Optional service binding asset used to resolve terminal dependencies.")]
        private TerminalServiceBindingAsset _serviceBindingAsset;

        [SerializeField]
        [Tooltip("Optional component-based binding overrides. Added automatically by the editor.")]
        internal TerminalServiceBindingComponent _serviceBindingComponent;

        private IInputHandler[] _inputHandlers;

        private ITerminalRuntime _runtime;
        private ITerminalServiceLocator _previousServiceLocator;
        private ITerminalServiceLocator _appliedServiceLocator;

        internal ITerminalRuntime Runtime => _runtime;

        private CommandLog ActiveLog => _runtime?.Log;

        private CommandShell ActiveShell => _runtime?.Shell;

        private CommandHistory ActiveHistory => _runtime?.History;

        private CommandAutoComplete ActiveAutoComplete => _runtime?.AutoComplete;

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
        [Tooltip("Available runtime mode options used to configure environment-specific features.")]
        [SerializeField]
        private List<RuntimeModeOption> _runtimeModeOptions = new();

        [Tooltip("Identifier of the runtime mode option applied on startup.")]
        [SerializeField]
        private string _selectedRuntimeModeId = string.Empty;

        [Tooltip("Legacy fallback for existing data. Prefer runtime mode options.")]
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
        private bool _useLauncherAnimationTiming;
        private LauncherLayoutMetrics _launcherMetrics;
        private bool _launcherMetricsInitialized;
        private bool _isClosingLauncher;
        private bool _isClosingStandard;
        private bool _hasCachedStandardScroll;
        private bool _restoreStandardScrollPending;
        private float _cachedStandardScrollValue;
        private float _cachedStandardScrollNormalized;
        private float _cachedStandardScrollLowValue;
        private float _cachedStandardScrollHighValue;
        private bool _cachedStandardScrollAtEnd;
        private long _cachedStandardLogVersion;
        private int _standardRestoreRetryCount;
        private StandardScrollSnapshot _smallStandardScrollCache;
        private StandardScrollSnapshot _fullStandardScrollCache;

        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("Enable verbose logging for scroll caching/restoration behaviour.")]
        private bool enableScrollDiagnostics;

        [SerializeField]
        [Tooltip("Enable verbose logging for launcher history fade behaviour.")]
        private bool enableFadeDiagnostics;

        [SerializeField]
        [Tooltip("Enable verbose logging for launcher layout/scroll computations.")]
        private bool enableLauncherDiagnostics;
        private const float ScrollDiagnosticMinimumIntervalSeconds = 0.5f;
        private const int StandardRestoreRetryLimit = 5;
        private string _lastScrollDiagnosticMessage;
        private int _suppressedScrollDiagnosticCount;
        private float _lastScrollDiagnosticTimestamp = float.NegativeInfinity;
        private string _pendingScrollDiagnosticMessage;

        private struct StandardScrollSnapshot
        {
            public bool HasCache;
            public float Value;
            public float Normalized;
            public float Low;
            public float High;
            public bool AtEnd;
            public long LogVersion;

            public void Reset()
            {
                HasCache = false;
                Value = 0f;
                Normalized = 0f;
                Low = 0f;
                High = 0f;
                AtEnd = false;
                LogVersion = -1;
            }
        }

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
        private string _lastCompletionAnchorText;
        private int? _lastCompletionAnchorCaretIndex;
        private readonly List<CommandHistoryEntry> _launcherHistoryEntries = new();
        private readonly List<LogItem> _logListItems = new();
        private Action<float> _launcherScrollValueChangedHandler;
        private LauncherViewController _launcherViewController;

        private float _launcherSuggestionContentHeight;
        private float _launcherHistoryContentHeight;
        private long _lastRenderedLauncherHistoryVersion = -1;
        private long _cachedLauncherScrollVersion = -1;
        private float _cachedLauncherScrollValue;

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

        private TerminalRuntimeModeFlags ResolveRuntimeModeFlags()
        {
            TerminalRuntimeModeFlags resolved;
            bool resolvedFromOptions = TryResolveRuntimeModeFromOptions(out resolved);
            if (resolvedFromOptions)
            {
                return resolved;
            }

#pragma warning disable CS0618 // Type or member is obsolete
            return _runtimeModes;
#pragma warning restore CS0618 // Type or member is obsolete
        }

        private bool TryResolveRuntimeModeFromOptions(out TerminalRuntimeModeFlags resolved)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            resolved = TerminalRuntimeModeFlags.None;
#pragma warning restore CS0618 // Type or member is obsolete
            if (_runtimeModeOptions == null || _runtimeModeOptions.Count == 0)
            {
                return false;
            }

            int matchIndex = ResolveRuntimeModeIndex(_selectedRuntimeModeId);
            if (matchIndex < 0)
            {
                matchIndex = 0;
            }

            RuntimeModeOption option = _runtimeModeOptions[matchIndex];
            if (option == null)
            {
                return false;
            }

            resolved = option.modes;
            if (!string.IsNullOrWhiteSpace(option.id))
            {
                _selectedRuntimeModeId = option.id;
            }

            return true;
        }

        private int ResolveRuntimeModeIndex(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || _runtimeModeOptions == null)
            {
                return -1;
            }

            for (int i = 0; i < _runtimeModeOptions.Count; ++i)
            {
                RuntimeModeOption option = _runtimeModeOptions[i];
                if (option == null || string.IsNullOrWhiteSpace(option.id))
                {
                    continue;
                }

                if (string.Equals(option.id, key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        internal bool TryApplyRuntimeMode(string runtimeModeId)
        {
            if (_runtimeModeOptions == null || _runtimeModeOptions.Count == 0)
            {
                return false;
            }

            int index = ResolveRuntimeModeIndex(runtimeModeId);
            if (index < 0)
            {
                return false;
            }

            RuntimeModeOption option = _runtimeModeOptions[index];
            if (option == null)
            {
                return false;
            }

            _selectedRuntimeModeId = option.id;
            ApplyRuntimeMode(option.modes);
            return true;
        }

        internal void SetRuntimeModeOptions(
            IEnumerable<RuntimeModeOption> options,
            string selectedId
        )
        {
            if (options == null)
            {
                _runtimeModeOptions = new List<RuntimeModeOption>();
            }
            else
            {
                _runtimeModeOptions = new List<RuntimeModeOption>(options);
            }

            _selectedRuntimeModeId = string.IsNullOrWhiteSpace(selectedId)
                ? string.Empty
                : selectedId;
        }

        private void ApplyRuntimeMode(TerminalRuntimeModeFlags modes)
        {
            RuntimeConfigurator.SetMode(modes);
#if UNITY_EDITOR
            RuntimeConfigurator.EditorAutoDiscover = _autoDiscoverParsersInEditor;
#endif
            RuntimeConfigurator.TryAutoDiscoverParsers();
        }

        private void Awake()
        {
            ApplyRuntimeProfile();
            ApplyCommandProfile();
            ApplyAppearanceProfile();

            TerminalRuntimeModeFlags resolvedRuntimeModes = ResolveRuntimeModeFlags();
            ApplyRuntimeMode(resolvedRuntimeModes);
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
                _input = TerminalUI.InputProvider?.GetInput(this) ?? DefaultTerminalInput.Instance;
            }

            if (TerminalProvider == null)
            {
                TerminalProvider = TerminalRegistry.Default;
            }

            Instance = this;
            TerminalProvider.Register(this);

            _launcherViewController = new LauncherViewController(this);

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
                nameof(_runtimeProfile),
                nameof(_appearanceProfile),
                nameof(_logBufferSize),
                nameof(_historyBufferSize),
                nameof(_terminalConfigurationAsset),
                nameof(_blockedLogTypes),
                nameof(_allowedLogTypes),
                nameof(_blockedCommands),
                nameof(_allowedCommands),
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
                nameof(_blockedCommands),
                nameof(_allowedCommands),
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                return;
            }

            ApplyRuntimeProfile();
            ApplyCommandProfile();
            ApplyAppearanceProfile();
        }
#endif

        private void OnEnable()
        {
            ApplyServiceBinding();

            if (resetStateOnInit)
            {
                ITerminalRuntimePool pool = RuntimePoolInstance;
                if (pool != null)
                {
                    pool.Clear();
                }
                else
                {
                    TerminalRuntimeCache.Clear();
                }
            }

            if (_runtime == null)
            {
                _runtime = AcquireRuntime();
            }

            RuntimeScope?.RegisterRuntime(_runtime);

            ApplyCommandProfile();
            RefreshStaticState(force: resetStateOnInit);
            ApplyAppearanceProfile();
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

            RuntimeScope?.UnregisterRuntime(_runtime);

            RestoreServiceBinding();
        }

        private void OnDestroy()
        {
            ITerminalRuntime runtime = _runtime;
            ITerminalRuntimePool pool = RuntimePoolInstance;
            if (runtime != null)
            {
                if (pool != null)
                {
                    pool.Return(runtime);
                }
                else if (runtime is TerminalRuntime runtimeImpl)
                {
                    TerminalRuntimeCache.Store(runtimeImpl);
                }
            }

            TerminalProvider?.Unregister(this);
            if (Instance == this)
            {
                Instance = TerminalProvider?.ActiveTerminal;
            }

            RestoreServiceBinding();
        }

        private void ApplyServiceBinding()
        {
            ITerminalServiceLocator locator = ResolveServiceLocator();
            if (locator == null)
            {
                _previousServiceLocator = null;
                _appliedServiceLocator = null;
                return;
            }

            if (!ReferenceEquals(ServiceLocator, locator))
            {
                _previousServiceLocator = ServiceLocator;
                ServiceLocator = locator;
            }

            _appliedServiceLocator = locator;
        }

        private void RestoreServiceBinding()
        {
            if (_appliedServiceLocator == null)
            {
                return;
            }

            if (ReferenceEquals(ServiceLocator, _appliedServiceLocator))
            {
                ITerminalServiceLocator fallback =
                    _previousServiceLocator ?? TerminalServiceLocator.Default;
                ServiceLocator = fallback;
            }

            _previousServiceLocator = null;
            _appliedServiceLocator = null;
        }

        private ITerminalServiceLocator ResolveServiceLocator()
        {
            if (_serviceBindingAsset != null)
            {
                return _serviceBindingAsset;
            }

            if (_serviceBindingComponent != null)
            {
                return _serviceBindingComponent;
            }

            TerminalServiceBindingComponent bindingComponent =
                GetComponent<TerminalServiceBindingComponent>();
            if (bindingComponent != null)
            {
                _serviceBindingComponent = bindingComponent;
                return bindingComponent;
            }

            TerminalServiceBindingAsset defaultBinding =
                TerminalServiceBindingSettings.DefaultBinding;
            if (defaultBinding != null)
            {
                _serviceBindingAsset = defaultBinding;
                return defaultBinding;
            }

            return null;
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
            ActiveLog?.DrainPending();
            HandleHeightAnimation();
            RefreshUI();
            _commandIssuedThisFrame = false;
        }

        private ITerminalRuntime AcquireRuntime()
        {
            ITerminalRuntimePool pool = RuntimePoolInstance;
            if (!resetStateOnInit)
            {
                if (
                    pool != null
                    && pool.TryRent(out ITerminalRuntime pooledRuntime)
                    && pooledRuntime != null
                )
                {
                    return pooledRuntime;
                }

                if (
                    pool == null
                    && TerminalRuntimeCache.TryAcquire(out TerminalRuntime cachedRuntime)
                )
                {
                    return cachedRuntime;
                }
            }

            ITerminalSettingsProvider settingsProvider = ResolveSettingsProvider();
            ITerminalRuntimeFactory factory = ResolveRuntimeFactory();
            ITerminalRuntime runtime = factory.CreateRuntime(settingsProvider);
            return runtime;
        }

        private ITerminalSettingsProvider ResolveSettingsProvider()
        {
            if (_terminalConfigurationAsset != null)
            {
                return _terminalConfigurationAsset;
            }

            if (_runtimeProfile != null)
            {
                return new RuntimeProfileSettingsProvider(_runtimeProfile);
            }

            return new DefaultTerminalSettingsProvider();
        }

        private ITerminalRuntimeFactory ResolveRuntimeFactory()
        {
            if (_runtimeFactoryOverrideForTests != null)
            {
                return _runtimeFactoryOverrideForTests;
            }

            return new TerminalRuntimeFactory();
        }

        private void RefreshStaticState(bool force)
        {
            if (_runtime == null)
            {
                _runtime = AcquireRuntime();
            }

            TerminalRuntimeSettings settings = BuildRuntimeSettings();
            TerminalRuntimeUpdateResult updateResult = _runtime.Configure(settings, force);

            if (_started && (updateResult.CommandsRefreshed || updateResult.RuntimeReset))
            {
                ResetAutoComplete();
            }
        }

        private TerminalRuntimeSettings BuildRuntimeSettings()
        {
            int logCapacity = Mathf.Max(0, _logBufferSize);
            int historyCapacity = Mathf.Max(0, _historyBufferSize);
            return new TerminalRuntimeSettings(
                logCapacity,
                historyCapacity,
                _blockedLogTypes,
                _allowedLogTypes,
                _blockedCommands,
                _allowedCommands,
                includeDefaultCommands: !ignoreDefaultCommands
            );
        }

        private void ApplyRuntimeProfile()
        {
            if (_runtimeProfile == null)
            {
                return;
            }

            _logBufferSize = Mathf.Max(0, _runtimeProfile.LogBufferSize);
            _historyBufferSize = Mathf.Max(0, _runtimeProfile.HistoryBufferSize);
            ignoreDefaultCommands = !_runtimeProfile.IncludeDefaultCommands;
            CopyList(_runtimeProfile.BlockedLogTypes, _blockedLogTypes);
            CopyList(_runtimeProfile.AllowedLogTypes, _allowedLogTypes);
            CopyList(_runtimeProfile.BlockedCommands, _blockedCommands);
            CopyList(_runtimeProfile.AllowedCommands, _allowedCommands);
        }

        private static void CopyList<T>(IReadOnlyList<T> source, List<T> destination)
        {
            if (destination == null)
            {
                return;
            }

            if (ReferenceEquals(source, destination))
            {
                return;
            }

            destination.Clear();
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; ++i)
            {
                destination.Add(source[i]);
            }
        }

        private void ApplyAppearanceProfile()
        {
            if (_appearanceProfile == null)
            {
                return;
            }

            showGUIButtons = _appearanceProfile.showGUIButtons;
            runButtonText = _appearanceProfile.runButtonText;
            closeButtonText = _appearanceProfile.closeButtonText;
            smallButtonText = _appearanceProfile.smallButtonText;
            fullButtonText = _appearanceProfile.fullButtonText;
            launcherButtonText = _appearanceProfile.launcherButtonText;
            hintDisplayMode = _appearanceProfile.hintDisplayMode;
            makeHintsClickable = _appearanceProfile.makeHintsClickable;
            _historyFadeTargets = _appearanceProfile.historyFadeTargets;
            _cursorBlinkRateMilliseconds = Mathf.Max(
                0,
                _appearanceProfile.cursorBlinkRateMilliseconds
            );
            _logUnityMessages = _appearanceProfile.logUnityMessages;
        }

        private void ApplyCommandProfile()
        {
            if (_commandProfile == null)
            {
                return;
            }

            ignoreDefaultCommands = !_commandProfile.CommandFilters.IncludeDefaults;
            CopyList(_commandProfile.LogFilters.Blocked, _blockedLogTypes);
            CopyList(_commandProfile.LogFilters.Allowed, _allowedLogTypes);
            CopyList(_commandProfile.CommandFilters.Blocked, _blockedCommands);
            CopyList(_commandProfile.CommandFilters.Allowed, _allowedCommands);
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
                ApplyRuntimeProfile();
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
            if (
                (_state == TerminalState.OpenSmall || _state == TerminalState.OpenFull)
                && newState == TerminalState.Closed
            )
            {
                CacheStandardScrollPosition();
            }

            _commandIssuedThisFrame = true;
            _previousState = _state;
            _state = newState;
            _isClosingLauncher =
                _previousState == TerminalState.OpenLauncher && newState == TerminalState.Closed;
            _isClosingStandard =
                (
                    _previousState == TerminalState.OpenSmall
                    || _previousState == TerminalState.OpenFull
                )
                && newState == TerminalState.Closed;
            if (_state == TerminalState.OpenLauncher)
            {
                _isClosingLauncher = false;
            }
            if (_state == TerminalState.OpenSmall || _state == TerminalState.OpenFull)
            {
                LoadStandardScrollSnapshotForState(_state);
                _isClosingStandard = false;
                if (_hasCachedStandardScroll)
                {
                    CommandLog log = ActiveLog;
                    if (log != null && log.Version == _cachedStandardLogVersion)
                    {
                        if (_cachedStandardScrollAtEnd)
                        {
                            _restoreStandardScrollPending = false;
                            _needsScrollToEnd = true;
                            LogScrollDiagnostic(
                                $"SetState {_state}: cached at end, will scroll to end"
                            );
                        }
                        else
                        {
                            _restoreStandardScrollPending = true;
                            _needsScrollToEnd = false;
                            LogScrollDiagnostic(
                                $"SetState {_state}: cached mid-history, restore pending"
                            );
                        }
                    }
                    else
                    {
                        _restoreStandardScrollPending = false;
                        _needsScrollToEnd = true;
                        LogScrollDiagnostic($"SetState {_state}: cache stale, will scroll to end");
                    }
                }
                else
                {
                    _needsScrollToEnd = true;
                    LogScrollDiagnostic($"SetState {_state}: no cache, will scroll to end");
                }
                LogScrollDiagnostic(
                    $"SetState {_state}: restorePending={_restoreStandardScrollPending} needsScrollToEnd={_needsScrollToEnd}"
                );
            }
            else if (!_isClosingStandard)
            {
                _restoreStandardScrollPending = false;
                LogScrollDiagnostic(
                    $"SetState {_state}: cleared restore flag (not closing standard)"
                );
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
                if (_commandInput != null)
                {
                    _isCommandFromCode = true;
                    _commandInput.SetValueWithoutNotify(string.Empty);
                    _commandInput.cursorIndex = 0;
                    _commandInput.selectIndex = 0;
                }
            }
        }

        private void CacheLauncherScrollPosition()
        {
            if (_logScrollView == null)
            {
                _cachedLauncherScrollVersion = -1;
                _cachedLauncherScrollValue = 0f;
                return;
            }

            CommandHistory history = ActiveHistory;
            if (history == null)
            {
                _cachedLauncherScrollVersion = -1;
                _cachedLauncherScrollValue = 0f;
                return;
            }

            _cachedLauncherScrollVersion = history.Version;
            Scroller verticalScroller = _logScrollView.verticalScroller;
            _cachedLauncherScrollValue = verticalScroller != null ? verticalScroller.value : 0f;
        }

        private void CacheStandardScrollPosition()
        {
            if (_logScrollView == null)
            {
                ResetStandardScrollCacheFields();
                _restoreStandardScrollPending = false;
                ResetStandardScrollCacheForState(_state);
                return;
            }

            Scroller scroller = _logScrollView.verticalScroller;
            if (scroller == null)
            {
                ResetStandardScrollCacheFields();
                _restoreStandardScrollPending = false;
                ResetStandardScrollCacheForState(_state);
                return;
            }

            CommandLog log = ActiveLog;
            float scrollerValue = scroller.value;
            float lowValue = scroller.lowValue;
            float scrollerHighValue = scroller.highValue;
            float geometryRange = GetGeometryScrollRange(_logScrollView);
            float effectiveHighValue = Mathf.Max(scrollerHighValue, geometryRange);
            if (effectiveHighValue < scrollerValue)
            {
                effectiveHighValue = scrollerValue;
            }

            float range = effectiveHighValue - lowValue;

            _cachedStandardScrollValue = scrollerValue;
            _cachedStandardScrollLowValue = lowValue;
            _cachedStandardScrollHighValue = effectiveHighValue;
            _cachedStandardScrollNormalized =
                range > 0.0001f ? Mathf.Clamp01((scrollerValue - lowValue) / range) : 0f;
            bool hasOverflow = effectiveHighValue > 0.01f;
            _cachedStandardScrollAtEnd = !hasOverflow || scrollerValue >= effectiveHighValue - 0.5f;
            _cachedStandardLogVersion = log?.Version ?? -1;
            _hasCachedStandardScroll = true;
            _restoreStandardScrollPending = false;
            _standardRestoreRetryCount = 0;
            StoreStandardScrollSnapshot(_state);
            LogScrollDiagnostic(
                $"CacheStandardScrollPosition state={_state} value={scrollerValue:F3} low={lowValue:F3} high={effectiveHighValue:F3} normalized={_cachedStandardScrollNormalized:F3} atEnd={_cachedStandardScrollAtEnd}"
            );
        }

        private void ResetStandardScrollCacheFields()
        {
            _hasCachedStandardScroll = false;
            _cachedStandardScrollValue = 0f;
            _cachedStandardScrollNormalized = 0f;
            _cachedStandardScrollLowValue = 0f;
            _cachedStandardScrollHighValue = 0f;
            _cachedStandardScrollAtEnd = false;
            _cachedStandardLogVersion = -1;
            _standardRestoreRetryCount = 0;
        }

        private void ResetStandardScrollCacheForState(TerminalState state)
        {
            switch (state)
            {
                case TerminalState.OpenSmall:
                    _smallStandardScrollCache.Reset();
                    break;
                case TerminalState.OpenFull:
                    _fullStandardScrollCache.Reset();
                    break;
            }
        }

        private void StoreStandardScrollSnapshot(TerminalState state)
        {
            if (state != TerminalState.OpenSmall && state != TerminalState.OpenFull)
            {
                return;
            }

            StandardScrollSnapshot snapshot = new StandardScrollSnapshot
            {
                HasCache = _hasCachedStandardScroll,
                Value = _cachedStandardScrollValue,
                Normalized = _cachedStandardScrollNormalized,
                Low = _cachedStandardScrollLowValue,
                High = _cachedStandardScrollHighValue,
                AtEnd = _cachedStandardScrollAtEnd,
                LogVersion = _cachedStandardLogVersion,
            };

            if (state == TerminalState.OpenSmall)
            {
                _smallStandardScrollCache = snapshot;
            }
            else
            {
                _fullStandardScrollCache = snapshot;
            }

            LogScrollDiagnostic(
                $"Stored standard scroll snapshot for {state}: hasCache={snapshot.HasCache}, value={snapshot.Value:F3}, normalized={snapshot.Normalized:F3}, low={snapshot.Low:F3}, high={snapshot.High:F3}, atEnd={snapshot.AtEnd}"
            );
        }

        private void LoadStandardScrollSnapshotForState(TerminalState state)
        {
            if (state != TerminalState.OpenSmall && state != TerminalState.OpenFull)
            {
                ResetStandardScrollCacheFields();
                _restoreStandardScrollPending = false;
                return;
            }

            StandardScrollSnapshot snapshot =
                state == TerminalState.OpenSmall
                    ? _smallStandardScrollCache
                    : _fullStandardScrollCache;

            if (snapshot.HasCache)
            {
                _hasCachedStandardScroll = true;
                _cachedStandardScrollValue = snapshot.Value;
                _cachedStandardScrollNormalized = snapshot.Normalized;
                _cachedStandardScrollLowValue = snapshot.Low;
                _cachedStandardScrollHighValue = snapshot.High;
                _cachedStandardScrollAtEnd = snapshot.AtEnd;
                _cachedStandardLogVersion = snapshot.LogVersion;
            }
            else
            {
                ResetStandardScrollCacheFields();
            }

            _restoreStandardScrollPending = false;
            _standardRestoreRetryCount = 0;
            LogScrollDiagnostic(
                $"Loaded standard scroll snapshot for {state}: hasCache={snapshot.HasCache}, value={_cachedStandardScrollValue:F3}, normalized={_cachedStandardScrollNormalized:F3}, low={_cachedStandardScrollLowValue:F3}, high={_cachedStandardScrollHighValue:F3}, atEnd={_cachedStandardScrollAtEnd}"
            );
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

        private void ConsumeAndLogErrors()
        {
            while (ActiveShell?.TryConsumeErrorMessage(out string error) == true)
            {
                RuntimeScope?.Log(TerminalLogType.Error, $"Error: {error}");
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
            _logScrollView = new ScrollView { name = "LogScrollView" };
            _logScrollView.AddToClassList("log-scroll-view");
            _terminalContainer.Add(_logScrollView);
            InitializeScrollView(_logScrollView);
            _logViewport = _logScrollView.contentViewport;
            if (_logViewport != null)
            {
                _logViewport.style.flexDirection = FlexDirection.Column;
                _logViewport.style.flexGrow = 1f;
                _logViewport.style.flexShrink = 1f;
                _logViewport.style.minHeight = 0f;
                _logViewport.style.overflow = Overflow.Hidden;
            }
            VisualElement logContent = _logScrollView.contentContainer;
            if (logContent != null)
            {
                logContent.style.flexDirection = FlexDirection.Column;
                logContent.style.alignItems = Align.FlexStart;
                logContent.style.minHeight = 0f;
                SetHistoryJustification(Justify.FlexEnd);
                logContent.RegisterCallback<GeometryChangedEvent>(OnLogContentGeometryChanged);
            }

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
                        if (justTypedSpace && context.ActiveShell != null)
                        {
                            string check = curr;
                            // Remove trailing space(s) to isolate the command token
                            if (check.NeedsTrim())
                            {
                                check = check.TrimEnd();
                            }

                            if (CommandShell.TryEatArgument(ref check, out CommandArg cmd))
                            {
                                if (context.ActiveShell.Commands.ContainsKey(cmd.contents))
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

        private void InitializeScrollView(ScrollView scrollView)
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
            SetupScrollValueChanged();
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

            void SetupScrollValueChanged()
            {
                Scroller scroller = scrollView.verticalScroller;
                if (scroller == null)
                {
                    scrollView.schedule.Execute(SetupScrollValueChanged).ExecuteLater(0);
                    return;
                }

                scroller.valueChanged -= OnLogScrollValueChanged;
                scroller.valueChanged += OnLogScrollValueChanged;
            }
        }

        private float GetGeometryScrollRange(ScrollView scrollView)
        {
            if (scrollView == null)
            {
                return 0f;
            }

            float viewportHeight = 0f;
            VisualElement viewport = scrollView.contentViewport;
            if (viewport != null)
            {
                viewportHeight = viewport.layout.height;
                if (float.IsNaN(viewportHeight) || viewportHeight <= 0.0001f)
                {
                    viewportHeight = viewport.resolvedStyle.height;
                    if (float.IsNaN(viewportHeight))
                    {
                        viewportHeight = 0f;
                    }
                }
            }

            if (viewportHeight <= 0.0001f)
            {
                if (_logViewport != null)
                {
                    float resolvedViewportHeight = _logViewport.resolvedStyle.height;
                    if (!float.IsNaN(resolvedViewportHeight) && resolvedViewportHeight > 0.0001f)
                    {
                        viewportHeight = resolvedViewportHeight;
                    }
                }

                if (viewportHeight <= 0.0001f)
                {
                    float inputHeight =
                        _inputContainer != null ? _inputContainer.resolvedStyle.height : 0f;
                    float containerHeight =
                        _terminalContainer != null ? _terminalContainer.resolvedStyle.height : 0f;
                    if (float.IsNaN(inputHeight))
                    {
                        inputHeight = 0f;
                    }
                    if (float.IsNaN(containerHeight) || containerHeight <= 0.0001f)
                    {
                        containerHeight = _currentWindowHeight;
                    }

                    float fallbackViewport = Mathf.Max(0f, containerHeight - inputHeight);
                    if (fallbackViewport > viewportHeight)
                    {
                        viewportHeight = fallbackViewport;
                    }
                }
            }

            float contentHeight = 0f;
            VisualElement content = scrollView.contentContainer;
            if (content != null)
            {
                contentHeight = content.layout.height;
                if (float.IsNaN(contentHeight) || contentHeight <= 0.0001f)
                {
                    contentHeight = content.resolvedStyle.height;
                    if (float.IsNaN(contentHeight))
                    {
                        contentHeight = 0f;
                    }
                }
            }

            if (contentHeight <= 0.0001f)
            {
                int itemCount = _logListItems != null ? _logListItems.Count : 0;
                if (itemCount > 0)
                {
                    float estimatedHeight = itemCount * StandardEstimatedHistoryRowHeight;
                    if (estimatedHeight > contentHeight)
                    {
                        contentHeight = estimatedHeight;
                    }
                }
            }

            return Mathf.Max(0f, contentHeight - viewportHeight);
        }

        private float GetEffectiveScrollHighValue(ScrollView scrollView, Scroller scroller)
        {
            if (scroller == null)
            {
                return 0f;
            }

            float currentHighValue = Mathf.Max(0f, scroller.highValue);
            float geometryHighValue = GetGeometryScrollRange(scrollView);
            float candidateHighValue = Mathf.Max(currentHighValue, geometryHighValue);

            if (_restoreStandardScrollPending && _hasCachedStandardScroll)
            {
                float cachedHigh = Mathf.Max(0f, _cachedStandardScrollHighValue);
                if (cachedHigh > candidateHighValue)
                {
                    candidateHighValue = cachedHigh;
                }
            }

            return Mathf.Max(0f, candidateHighValue);
        }

        private bool ScrollToEnd()
        {
            if (_restoreStandardScrollPending)
            {
                LogScrollDiagnostic("ScrollToEnd aborted: restore pending");
                return false;
            }

            if (_logScrollView == null)
            {
                LogScrollDiagnostic("ScrollToEnd aborted: _logScrollView is null");
                return false;
            }

            bool immediateSuccess = TryApplyScrollToEnd(_logScrollView, "immediate");
            _logScrollView
                .schedule.Execute(() =>
                {
                    bool scheduledSuccess = TryApplyScrollToEnd(_logScrollView, "scheduled");
                    if (scheduledSuccess)
                    {
                        _needsScrollToEnd = false;
                    }
                    else
                    {
                        LogScrollDiagnostic(
                            "ScrollToEnd scheduled retry deferred; will reschedule"
                        );
                        _needsScrollToEnd = true;
                    }
                })
                .ExecuteLater(0);
            return immediateSuccess;
        }

        private bool TryApplyScrollToEnd(ScrollView scrollView, string phase)
        {
            if (scrollView == null)
            {
                LogScrollDiagnostic($"TryApplyScrollToEnd ({phase}) aborted: scrollView null");
                return false;
            }

            Scroller scroller = scrollView.verticalScroller;
            if (scroller == null)
            {
                LogScrollDiagnostic($"TryApplyScrollToEnd ({phase}) waiting for scroller");
                return false;
            }

            float highValue = GetEffectiveScrollHighValue(scrollView, scroller);
            if (highValue <= 0.01f)
            {
                LogScrollDiagnostic(
                    $"TryApplyScrollToEnd ({phase}) skipped, highValue={highValue:F4}"
                );
                return false;
            }

            if (highValue > scroller.highValue + 0.001f)
            {
                scroller.highValue = highValue;
            }

            if (highValue < scroller.lowValue)
            {
                highValue = scroller.lowValue;
            }

            scroller.value = highValue;
            Vector2 offset = scrollView.scrollOffset;
            if (!Mathf.Approximately(offset.y, highValue))
            {
                scrollView.scrollOffset = new Vector2(offset.x, highValue);
            }
            LogScrollDiagnostic($"TryApplyScrollToEnd ({phase}) set scroller.value={highValue:F4}");

            UpdateStandardScrollAlignment(highValue);
            return true;
        }

        private void LogScrollDiagnostic(string message)
        {
            if (!enableScrollDiagnostics)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            float elapsed = now - _lastScrollDiagnosticTimestamp;
            if (elapsed < ScrollDiagnosticMinimumIntervalSeconds)
            {
                _suppressedScrollDiagnosticCount++;
                _pendingScrollDiagnosticMessage = message;
                return;
            }

            if (_suppressedScrollDiagnosticCount > 0)
            {
                string latestMessage =
                    _pendingScrollDiagnosticMessage
                    ?? _lastScrollDiagnosticMessage
                    ?? "no details available";
                Debug.Log(
                    $"[TerminalUI Scroll][{id}] (suppressed {_suppressedScrollDiagnosticCount} messages, latest: {latestMessage})",
                    this
                );
                _suppressedScrollDiagnosticCount = 0;
                _pendingScrollDiagnosticMessage = null;
            }

            _lastScrollDiagnosticMessage = message;
            _lastScrollDiagnosticTimestamp = now;
            Debug.Log($"[TerminalUI Scroll][{id}] {message}", this);
        }

        private void LogFadeDiagnostic(string message)
        {
            if (!enableFadeDiagnostics)
            {
                return;
            }

            Debug.Log($"[TerminalUI Fade][{id}] {message}", this);
        }

        private void LogLauncherDiagnostic(string message)
        {
            if (!enableLauncherDiagnostics)
            {
                return;
            }

            Debug.Log($"[TerminalUI Launcher][{id}] {message}", this);
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

            _input.CommandText = ActiveHistory?.Previous(skipSameCommandsInHistory) ?? string.Empty;
            ResetAutoComplete();
            _needsFocus = true;
        }

        public void HandleNext()
        {
            if (_state == TerminalState.Closed)
            {
                return;
            }

            _input.CommandText = ActiveHistory?.Next(skipSameCommandsInHistory) ?? string.Empty;
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

        internal void ForceStateForTests(TerminalState state)
        {
            _state = state;
        }

        internal bool LauncherMetricsInitializedForTests => _launcherMetricsInitialized;

        internal ScrollView LogScrollViewForTests => _logScrollView;

        internal VisualElement LogContentForTests => _logScrollView?.contentContainer;

        internal void UpdateTerminalVisibilityForTests()
        {
            UpdateTerminalVisibility(_state != TerminalState.Closed && _currentWindowHeight > 0.1f);
        }

        internal ScrollView AutoCompleteContainerForTests => _autoCompleteContainer;

        internal VisualElement InputContainerForTests => _inputContainer;

        internal VisualElement TerminalContainerForTests => _terminalContainer;

        internal void RefreshUIForTests()
        {
            RefreshUI();
        }

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

        internal IList<LogItem> LogItemsForTests => _logListItems;

        internal IList<string> CompletionBufferForTests => _lastCompletionBuffer;

        internal void SetConfigurationAssetForTests(TerminalConfigurationAsset configurationAsset)
        {
            _terminalConfigurationAsset = configurationAsset;
        }

        internal void SetRuntimeFactoryForTests(ITerminalRuntimeFactory factory)
        {
            _runtimeFactoryOverrideForTests = factory;
        }

        internal void SetServiceBindingForTests(TerminalServiceBindingAsset binding)
        {
            _serviceBindingAsset = binding;
        }

        internal void SetServiceBindingComponentForTests(
            TerminalServiceBindingComponent bindingComponent
        )
        {
            _serviceBindingComponent = bindingComponent;
        }

        internal void SetRuntimeProfileForTests(TerminalRuntimeProfile profile)
        {
            _runtimeProfile = profile;
            ApplyRuntimeProfile();
            if (_runtime != null)
            {
                RefreshStaticState(force: true);
            }
        }

        internal void SetAppearanceProfileForTests(TerminalAppearanceProfile profile)
        {
            _appearanceProfile = profile;
            ApplyAppearanceProfile();
        }

        internal void SetCommandProfileForTests(TerminalCommandProfile profile)
        {
            _commandProfile = profile;
            ApplyCommandProfile();
            if (_runtime != null)
            {
                RefreshStaticState(force: true);
            }
        }

        internal void SetBlockedCommandsForTests(IReadOnlyList<string> commands)
        {
            CopyList(commands, _blockedCommands);
        }

        internal void SetAllowedCommandsForTests(IReadOnlyList<string> commands)
        {
            CopyList(commands, _allowedCommands);
        }

        internal void SetBlockedLogTypesForTests(IReadOnlyList<TerminalLogType> logTypes)
        {
            CopyList(logTypes, _blockedLogTypes);
        }

        internal void SetAllowedLogTypesForTests(IReadOnlyList<TerminalLogType> logTypes)
        {
            CopyList(logTypes, _allowedLogTypes);
        }

        internal IReadOnlyList<string> BlockedCommandsForTests => _blockedCommands;

        internal IReadOnlyList<string> AllowedCommandsForTests => _allowedCommands;

        internal IReadOnlyList<TerminalLogType> BlockedLogTypesForTests => _blockedLogTypes;

        internal IReadOnlyList<TerminalLogType> AllowedLogTypesForTests => _allowedLogTypes;

        internal TerminalHistoryFadeTargets HistoryFadeTargetsForTests => _historyFadeTargets;

        internal int CursorBlinkRateForTests => _cursorBlinkRateMilliseconds;

        internal bool LogUnityMessagesForTests => _logUnityMessages;

        private void UpdateTerminalVisibility(bool shouldDisplayTerminal)
        {
            if (_terminalContainer != null)
            {
                _terminalContainer.style.display = shouldDisplayTerminal
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            }

            if (_uiDocument != null)
            {
                VisualElement terminalRoot = _uiDocument.rootVisualElement?.Q<VisualElement>(
                    TerminalRootName
                );
                if (terminalRoot != null)
                {
                    terminalRoot.style.display = shouldDisplayTerminal
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                }
            }
        }

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

        internal void SetLauncherContentHeightsForTests(float historyHeight, float suggestionHeight)
        {
            _launcherHistoryContentHeight = historyHeight;
            _launcherSuggestionContentHeight = suggestionHeight;
        }

        internal void SetLogScrollViewForTests(ScrollView scrollView)
        {
            _logScrollView = scrollView;
        }

        internal void RefreshLauncherHistoryForTests()
        {
            RefreshLauncherHistory();
        }

        internal void UpdateLauncherLayoutMetricsForTests()
        {
            UpdateLauncherLayoutMetrics();
        }

        internal void RefreshAutoCompleteHintsForTests()
        {
            RefreshAutoCompleteHints();
        }

        internal void InjectAutoCompleteContainerForTests(ScrollView container)
        {
            _autoCompleteContainer = container;
            _autoCompleteViewport = container != null ? container.contentViewport : null;
        }

        internal void InjectLayoutElementsForTests(
            VisualElement terminalContainer,
            VisualElement inputContainer,
            ScrollView autoCompleteContainer,
            ScrollView logScrollView
        )
        {
            _terminalContainer = terminalContainer;
            _inputContainer = inputContainer;
            _logScrollView = logScrollView;
            InjectAutoCompleteContainerForTests(autoCompleteContainer);
            InitializeInjectedScrollViewForTests();
        }

        internal void SetHintDisplayModeForTests(HintDisplayMode mode)
        {
            hintDisplayMode = mode;
        }

        internal void ResetWindowForTests()
        {
            ResetWindowIdempotent();
        }

        private void InitializeInjectedScrollViewForTests()
        {
            if (_logScrollView == null)
            {
                return;
            }

            InitializeScrollView(_logScrollView);
            _logScrollView.AddToClassList("log-scroll-view");

            VisualElement viewport = _logScrollView.contentViewport;
            if (viewport != null)
            {
                viewport.style.flexDirection = FlexDirection.Column;
                viewport.style.flexGrow = 1f;
                viewport.style.flexShrink = 1f;
                viewport.style.minHeight = 0f;
                viewport.style.overflow = Overflow.Hidden;
            }

            VisualElement content = _logScrollView.contentContainer;
            if (content != null)
            {
                content.style.flexDirection = FlexDirection.Column;
                content.style.alignItems = Align.FlexStart;
                content.style.minHeight = 0f;
                SetHistoryJustification(Justify.FlexEnd);
                content.RegisterCallback<GeometryChangedEvent>(OnLogContentGeometryChanged);
            }
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

        internal void SetHistoryJustification(Justify justify)
        {
            VisualElement content = _logScrollView?.contentContainer;
            if (content == null)
            {
                return;
            }

            content.style.justifyContent = justify;
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

                RuntimeScope?.Log(TerminalLogType.Input, commandText);
                ActiveShell?.RunCommand(commandText);
                while (ActiveShell?.TryConsumeErrorMessage(out string error) == true)
                {
                    RuntimeScope?.Log(TerminalLogType.Error, $"Error: {error}");
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
            bool isExpanding = _targetWindowHeight > _initialWindowHeight;
            _useLauncherAnimationTiming =
                (_state == TerminalState.OpenLauncher || _isClosingLauncher)
                && (isExpanding || _launcherMetricsInitialized);
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

            bool useLauncherTiming = _useLauncherAnimationTiming;

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
                _useLauncherAnimationTiming = false;
                if (_isClosingLauncher)
                {
                    _isClosingLauncher = false;
                    _launcherMetricsInitialized = false;
                    _launcherSuggestionContentHeight = 0f;
                    _launcherHistoryContentHeight = 0f;
                }
                if (_isClosingStandard)
                {
                    _isClosingStandard = false;
                }
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
                _useLauncherAnimationTiming = false;
                if (_isClosingLauncher)
                {
                    _isClosingLauncher = false;
                    _launcherMetricsInitialized = false;
                    _launcherSuggestionContentHeight = 0f;
                    _launcherHistoryContentHeight = 0f;
                }
                if (_isClosingStandard)
                {
                    _isClosingStandard = false;
                }
            }
        }

        private static void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            ITerminalRuntime runtime = RuntimeScope?.ActiveRuntime;
            if (runtime == null)
            {
                return;
            }

            CommandLog log = runtime.Log;
            log?.EnqueueUnityLog(message, stackTrace, (TerminalLogType)type);
        }

        internal float ComputeLauncherOpacityForTests(float normalized)
        {
            return _launcherViewController != null
                ? _launcherViewController.ComputeOpacityForTests(normalized)
                : 1f;
        }

        internal float LauncherFadeMinimumForTests => GetHistoryFadeMinimumOpacity();
    }
}
