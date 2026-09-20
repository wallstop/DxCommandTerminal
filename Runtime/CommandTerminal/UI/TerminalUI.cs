namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using Attributes;
    using Backend;
    using Extensions;
    using Helper;
    using Input;
    using Themes;
    using UnityEngine;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    [DisallowMultipleComponent]
    public sealed class TerminalUI : MonoBehaviour
    {
        private const string TerminalRootName = "TerminalRoot";

        public static TerminalUI Instance { get; private set; }

        // Cache log callback to reduce allocations
        private static readonly Application.LogCallback UnityLogCallback = HandleUnityLog;

        /*
            Live registry for lifecycle handoff: Instance and session-config
            ownership transfer to the newest live enabled terminal when the
            current owner is disabled or destroyed, so built-in commands and
            the shared session keep working while a peer remains. Cleared by
            the play-session reset (entries become stale across Play Mode
            sessions with disabled domain reload).
         */
        private static readonly List<TerminalUI> LiveTerminals = new();

        private static TerminalUI _configOwner;

        // ReSharper disable once MemberCanBePrivate.Global
        public bool IsClosed =>
            !IsOpenState(_state) && Mathf.Approximately(_currentWindowHeight, _targetWindowHeight);

        public string CurrentTheme =>
            !string.IsNullOrWhiteSpace(_runtimeTheme) ? _runtimeTheme : _persistedTheme;

        public string CurrentFriendlyTheme => ThemeNameHelper.GetFriendlyThemeName(CurrentTheme);

        public Font CurrentFont => _runtimeFont != null ? _runtimeFont : _persistedFont;

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

        [SerializeField]
        [Tooltip("Unique Id for this terminal, mainly for use with persisted configuration")]
        internal string id = Guid.NewGuid().ToString();

        [SerializeField]
        internal UIDocument _uiDocument;

        [SerializeField]
        internal string _persistedTheme = "dark-theme";

        [Header("Input")]
        [SerializeField]
        internal Font _persistedFont;

        [Header("System")]
        [SerializeField]
        internal int _logBufferSize = 256;

        /*
            Internal for test coverage of the shared settings asset (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        [SerializeField]
        internal int _historyBufferSize = 512;

        /*
            Internal for test coverage of the shared settings asset (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        [SerializeField]
        internal string _inputCaret = ">";

        [Header("System")]
        [SerializeField]
        internal int _cursorBlinkRateMilliseconds = 666;

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
        internal List<TerminalLogType> _ignoredLogTypes = new();

        [SerializeField]
        internal List<string> _disabledCommands = new();

        [SerializeField]
        internal TerminalFontPack _fontPack;

        [SerializeField]
        internal TerminalThemePack _themePack;

        [SerializeField]
        internal bool _logUnityMessages;

        [SerializeField]
        [Tooltip(
            "Which entries capture a caller stack trace. ErrorsAndWarnings and Disabled skip the per-log extraction cost for routine messages."
        )]
        internal TerminalStackTraceMode _stackTraceMode = TerminalStackTraceMode.All;

        [SerializeField]
        [Tooltip(
            "Shared settings asset applied when this component wakes; its values win over the serialized component values. Empty = component values."
        )]
        internal TerminalSettings _settings;

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

        /*
           Internal for test coverage of token completion (see
           WallstopStudios.DxCommandTerminal.Tests.Runtime).
        */
        internal TextField _commandInput;

        /*
           Internal for test coverage of caret behavior (see
           WallstopStudios.DxCommandTerminal.Tests.Runtime).
        */
        internal VisualElement _textInput;

        /*
            Internal for test coverage of hint suppression (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal readonly List<string> _lastCompletionBuffer = new();

        private TerminalState _state = TerminalState.Closed;
        private float _currentWindowHeight;
        private float _targetWindowHeight;
        private float _realWindowHeight;
        private bool _unityLogAttached;
        private bool _started;
        private bool _needsFocus;
        private bool _needsScrollToEnd;
        private long? _lastSeenBufferVersion;
        private bool _paletteHeldSurface;
        private bool _needsInitialRefresh;
        private string _lastKnownCommandText;
        private int? _lastCompletionIndex;
        private int? _previousLastCompletionIndex;
        private string _focusedControl;
        private bool _isCommandFromCode;
        private bool _commandIssuedThisFrame;
        private string _runtimeTheme;
        private Font _runtimeFont;
        private float _initialWindowHeight;
        private float _animationTimer;
        private bool _isAnimating;

        private VisualElement _terminalContainer;
        private ScrollView _logScrollView;
        private ScrollView _autoCompleteContainer;
        private VisualElement _inputContainer;
        private Button _runButton;
        private VisualElement _stateButtonContainer;
        private Label _inputCaretLabel;
        private bool _lastKnownHintsClickable;
        private IVisualElementScheduledItem _cursorBlinkSchedule;
        private readonly List<string> _lastCompletionBufferTempCache = new();
        private readonly HashSet<string> _lastCompletionBufferTempSet = new(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly List<VisualElement> _autoCompleteChildren = new();

        /*
            Provider-based token completion state. Cycled the same way the
            history completion buffer is cycled; reset whenever the input
            changes or the terminal state resets.
         */
        private readonly List<CommandCompletion> _tokenCompletions = new();
        private readonly List<CommandCompletion> _tokenCompletionsTemp = new();
        private int? _tokenCompletionIndex;
        private string _tokenCompletionInput;
        private int _tokenCompletionCaret;
        private string _tokenCompletionAppliedText;
        private int _tokenCompletionReplacementStart;
        private int _tokenCompletionReplacementLength;
        private bool _tokenCompletionQuoted;

        /*
            The last value RefreshUI wrote to the field. The panel re-emits it
            as a synthetic change event; matching it keeps that echo from
            resetting completion state like user input.
         */
        private string _lastCodeSyncedValue;

#if UNITY_EDITOR
        private readonly EditorApplication.CallbackFunction _checkForChanges;
#endif
        /*
            Caret to restore on the next focus pass; negative keeps the
            focus-at-end behavior. Token completions set it. Internal for
            completion-caret test coverage.
         */
        internal int? _pendingCaretIndex;
        private ITerminalInput _input;

        public TerminalUI()
        {
#if UNITY_EDITOR
            _checkForChanges = CheckForChanges;
#endif
        }

        private static List<T> CopyList<T>(List<T> source)
        {
            return source == null ? new List<T>() : new List<T>(source);
        }

        private void Awake()
        {
            /*
                Shared settings asset wins over the serialized component
                values (issue #72 option B). Applied before any validation so
                a misconfigured asset surfaces the same buffer-size warnings
                the component fields would.
             */
            ApplySharedSettings();

            /*
                The serialized field is assigned by the editor when the
                component is added through the inspector; adds that bypass
                that hook (scripted adds, components added before editor
                scripts compile) leave it null while a UIDocument component
                may still exist on this GameObject. Recovering here keeps the
                terminal launchable and runs before the editor property
                snapshot, so the recovered document does not register as a
                tracked change.
             */
            if (_uiDocument == null)
            {
                TryGetComponent(out _uiDocument);
            }

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
            LiveTerminals.Add(this);

#if UNITY_EDITOR
            _serializedObject = new SerializedObject(this);

            string[] uiPropertiesTracked = { nameof(_uiDocument), nameof(showGUIButtons) };
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
                nameof(_stackTraceMode),
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
            _configOwner = this;

            /*
                A component that lost Instance or config ownership to a peer
                while disabled reclaims them on re-enable: it is now the
                newest enabled claim. The registry entry moves to the back so
                the newest-enabled ordering holds for handoff targets too.
             */
            Instance = this;
            LiveTerminals.Remove(this);
            LiveTerminals.Add(this);

            ConsumeAndLogErrors();

            if (_logUnityMessages && !_unityLogAttached)
            {
                Application.logMessageReceivedThreaded += UnityLogCallback;
                _unityLogAttached = true;
            }

            /*
                On-screen state buttons are the open controls for a closed
                terminal, so this opt-in mode builds its tree eagerly and
                stays on the always-refresh path (LateUpdate exempts it from
                the idle gate). Terminals without the buttons defer the whole
                tree to the first open.
             */
            if (showGUIButtons)
            {
                EnsureUI();
            }

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
                /*
                    Every attach site (the enable path and the inspector
                    toggle path) binds the same static HandleUnityLog method,
                    so the delegates are structurally equal and this single
                    removal drops exactly this component's one occurrence -
                    never a peer's subscription. Removing twice would strip a
                    surviving terminal's forwarding when two components
                    forward Unity logs.
                 */
                Application.logMessageReceivedThreaded -= UnityLogCallback;
                _unityLogAttached = false;
            }

            SetState(TerminalState.Closed);
            TeardownUI();

            /*
                Lifecycle handoff: this component applied the last session
                configuration, so a remaining enabled terminal reapplies its
                own; Instance follows so built-in UI commands reach a working
                surface. Without a peer, both stay with this component and a
                later re-enable reclaims them.
             */
            if (_configOwner == this && TryHandOffToLivePeer(out TerminalUI peer))
            {
                _configOwner = peer;
                peer.RefreshStaticState(force: false);
            }

            if (Instance == this && TryHandOffToLivePeer(out TerminalUI instancePeer))
            {
                Instance = instancePeer;
            }
        }

        private void OnDestroy()
        {
            LiveTerminals.Remove(this);

            if (Instance != this)
            {
                return;
            }

            /*
                The owner is destroyed; it can never re-enable, so hand
                Instance to a live enabled peer or clear it.
             */
            Instance = TryHandOffToLivePeer(out TerminalUI peer) ? peer : null;
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
            /*
                The idle check runs before the animation step: the frame where
                HandleHeightAnimation snaps to the closed target is the frame
                RefreshUI must write the final height and input display; a
                gate evaluated after the snap would skip that final write and
                freeze the surface at the last animated height.
             */
            bool idleClosed =
                IsClosed
                && !_needsFocus
                && !_needsScrollToEnd
                && !_pendingCaretIndex.HasValue
                && !IsPaletteSurfaceOpen()
                && !showGUIButtons
                && !_needsInitialRefresh;

            ResetWindowIdempotent();
            HandleHeightAnimation();

            /*
                A palette on this document owns the shared root: RefreshUI
                must not clamp the root to the terminal's own window height
                while the panel is up, and the terminal's close animation
                resumes only after the surface is handed back. Latch the
                takeover so the first pass after the palette closes
                reasserts the terminal's root height exactly once.
             */
            if (IsPaletteSurfaceOpen())
            {
                _paletteHeldSurface = true;
            }
            else if (idleClosed && !_paletteHeldSurface)
            {
                /*
                    Everything below is UI-sync work: style writes, log
                    sync, hints, focus, and caret application. A fully
                    closed terminal with no pending work skips it and
                    resumes on the next open or buffer change. Terminals
                    with on-screen state buttons stay on the always-refresh
                    path: their buttons are the open controls while closed.
                 */
            }
            else
            {
                /*
                    A rebuild can happen out-of-band while closed (the editor
                    change hook), leaving stale heights and a visible input;
                    one refresh pass clamps the tree before idling again.
                 */
                _paletteHeldSurface = false;
                _needsInitialRefresh = false;
                RefreshUI();
            }

            _commandIssuedThisFrame = false;
        }

        /*
            Shared settings asset wins over the serialized component values
            (issue #72 option B). Runs at wake and before every UI build;
            idempotent. Lists are copied so a scripted mutation of the
            component fields can never edit the shared asset.
         */
        private void ApplySharedSettings()
        {
            if (_settings == null)
            {
                return;
            }

            _logBufferSize = _settings.logBufferSize;
            _historyBufferSize = _settings.historyBufferSize;
            _inputCaret = _settings.inputCaret;
            _cursorBlinkRateMilliseconds = _settings.cursorBlinkRateMilliseconds;
            showGUIButtons = _settings.showGUIButtons;
            runButtonText = _settings.runButtonText;
            closeButtonText = _settings.closeButtonText;
            smallButtonText = _settings.smallButtonText;
            fullButtonText = _settings.fullButtonText;
            hintDisplayMode = _settings.hintDisplayMode;
            makeHintsClickable = _settings.makeHintsClickable;
            resetStateOnInit = _settings.resetStateOnInit;
            skipSameCommandsInHistory = _settings.skipSameCommandsInHistory;
            ignoreDefaultCommands = _settings.ignoreDefaultCommands;
            _ignoredLogTypes = CopyList(_settings.ignoredLogTypes);
            _disabledCommands = CopyList(_settings.disabledCommands);
            _logUnityMessages = _settings.logUnityMessages;
            _stackTraceMode = _settings.stackTraceMode;
        }

        private void RefreshStaticState(bool force)
        {
            bool shellReconfigured = TerminalSession.Current.Apply(
                new TerminalSession.Config(
                    logBufferSize: _logBufferSize,
                    historyBufferSize: _historyBufferSize,
                    ignoredLogTypes: _ignoredLogTypes,
                    disabledCommands: _disabledCommands,
                    ignoreDefaultCommands: ignoreDefaultCommands,
                    stackTraceMode: _stackTraceMode
                ),
                force
            );

            if (shellReconfigured && _started)
            {
                ResetAutoComplete();
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
                if (_uiDocument != null && _terminalContainer != null)
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

        /*
            Runs once per Play Mode session with the earliest load type, so
            with disabled domain reload it clears what a previous session's
            components left behind: stale static references (the registry
            and Instance hold destroyed components after play exits), the
            shared session's backends (a previous session's shell would
            otherwise keep its manual registrations), and any log callback
            left attached by an abnormal exit. Exactly-once per session and
            idempotent on repeat.
         */
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForNextPlaySession()
        {
            Instance = null;
            _configOwner = null;
            LiveTerminals.Clear();
            TerminalSession.Current.ResetState();

            /*
                No live component exists here (scene Awake has not run and the
                previous session's components are destroyed), so these
                removals cannot strip a legitimate subscription; they clean
                leaked occurrences from an abnormal exit. A leaked occurrence
                that survives is inert: HandleUnityLog reads the buffer, which
                is null until the next Apply.
             */
            Application.logMessageReceivedThreaded -= UnityLogCallback;
            Application.logMessageReceivedThreaded -= HandleUnityLog;
        }

        private static void ConsumeAndLogErrors()
        {
            while (Terminal.Shell?.TryConsumeErrorMessage(out string error) == true)
            {
                Terminal.Log(TerminalLogType.Error, $"Error: {error}");
            }
        }

        private static bool TokenCompletionsEquivalent(
            List<CommandCompletion> candidates,
            List<CommandCompletion> current
        )
        {
            if (candidates.Count != current.Count)
            {
                return false;
            }

            int candidateCount = candidates.Count;
            for (int i = 0; i < candidateCount; ++i)
            {
                CommandCompletion candidate = candidates[i];
                CommandCompletion active = current[i];
                if (
                    !string.Equals(
                        candidate.InsertionText,
                        active.InsertionText,
                        StringComparison.Ordinal
                    )
                    || candidate.HasReplacementOverride != active.HasReplacementOverride
                )
                {
                    return false;
                }

                if (
                    candidate.Replacement is CommandCompletionReplacement candidateReplacement
                    && active.Replacement is CommandCompletionReplacement activeReplacement
                    && (
                        candidateReplacement.Start != activeReplacement.Start
                        || candidateReplacement.Length != activeReplacement.Length
                    )
                )
                {
                    return false;
                }
            }

            return true;
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

            ScrollBarCaptureState scrollBarCaptureState = ScrollBarCaptureState.Inactive;

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
                scrollBarCaptureState = ScrollBarCaptureState.Inactive;
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
                scrollBarCaptureState = ScrollBarCaptureState.Inactive;
                trackerElement.RemoveFromClassList("dragger-active");
                draggerElement.RemoveFromClassList("tracker-active");
                draggerElement.RemoveFromClassList("dragger-active");
            }

            void OnTrackerMouseEnter(MouseEnterEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.Inactive)
                {
                    draggerElement.AddToClassList("tracker-hovered");
                }
            }

            void OnTrackerMouseLeave(MouseLeaveEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.Inactive)
                {
                    draggerElement.RemoveFromClassList("tracker-hovered");
                }
            }

            void OnDraggerMouseEnter(MouseEnterEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.Inactive)
                {
                    trackerElement.AddToClassList("dragger-hovered");
                }
            }

            void OnDraggerMouseLeave(MouseLeaveEvent evt)
            {
                if (scrollBarCaptureState == ScrollBarCaptureState.Inactive)
                {
                    trackerElement.RemoveFromClassList("dragger-hovered");
                }
            }
        }

        private static bool TryHandOffToLivePeer(out TerminalUI peer)
        {
            int liveCount = LiveTerminals.Count;
            for (int i = liveCount - 1; 0 <= i; --i)
            {
                TerminalUI candidate = LiveTerminals[i];
                if (candidate != null && candidate.isActiveAndEnabled)
                {
                    peer = candidate;
                    return true;
                }
            }

            peer = null;
            return false;
        }

        private static void HandleUnityLog(string message, string stackTrace, LogType type)
        {
            Terminal.Buffer?.HandleLog(message, stackTrace, (TerminalLogType)type);
        }

        private static bool NamesContain(List<string> names, string candidate)
        {
            foreach (string name in names)
            {
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /*
            The only states that count as open; everything else (closed, and
            any unknown/invalid value a serialized asset might hold) is
            treated as closed. Whitelisting keeps future enum additions from
            silently behaving as open.
         */
        private static bool IsOpenState(TerminalState state)
        {
            return state is TerminalState.OpenSmall or TerminalState.OpenFull;
        }

        private static string FindName(List<string> names, string marker)
        {
            foreach (string name in names)
            {
                if (name.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }

            return null;
        }

        private static Font FindFont(List<Font> fonts, bool requireMono, bool requireRegular)
        {
            foreach (Font font in fonts)
            {
                if (font == null)
                {
                    continue;
                }

                string fontName = font.name;
                if (!requireMono || fontName.Contains("Mono", StringComparison.OrdinalIgnoreCase))
                {
                    if (
                        !requireRegular
                        || fontName.Contains("Regular", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return font;
                    }
                }
            }

            return null;
        }

        private static bool ListsEqual<T>(List<T> left, List<T> right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            int leftCount = left.Count;
            for (int index = 0; index < leftCount; ++index)
            {
                if (!comparer.Equals(left[index], right[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public void ToggleState(TerminalState newState)
        {
            SetState(_state == newState ? TerminalState.Closed : newState);
        }

        public void SetState(TerminalState newState)
        {
            _commandIssuedThisFrame = true;
            if (IsOpenState(newState))
            {
                CommandPaletteUI.CloseActive();
                /*
                    Sweep every palette sharing this document: CloseActive only
                    closes the Instance palette, and a leftover palette would
                    keep the shared-surface gate closed forever.
                 */
                CommandPaletteUI.CloseAllOn(_uiDocument);
            }

            _state = newState;
            if (IsOpenState(_state))
            {
                EnsureUI();
            }

            ResetWindowIdempotent();
            if (IsOpenState(_state))
            {
                _needsFocus = true;
            }
            else
            {
                /*
                    A focus queued while open can never be applied once the
                    terminal closes; dropping it keeps the closed state idle
                    instead of re-running the refresh loop every frame.
                 */
                _needsFocus = false;

                /*
                    OnDisable routes through here and can run before Awake on a
                    never-enabled component, where _input is not resolved yet.
                 */
                if (_input != null)
                {
                    _input.CommandText = string.Empty;
                }

                ResetAutoComplete();
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

            int validFontCount = 0;
            foreach (Font font in loadedFonts)
            {
                if (font != null)
                {
                    ++validFontCount;
                }
            }

            if (validFontCount == 0)
            {
                return _runtimeFont;
            }

            int currentFontIndex = loadedFonts.IndexOf(_runtimeFont);

            int newFontIndex;
            Font newFont;
            do
            {
                newFontIndex = ThreadLocalRandom.Instance.Next(loadedFonts.Count);
                newFont = loadedFonts[newFontIndex];
            } while (newFont == null || (newFontIndex == currentFontIndex && validFontCount != 1));
            SetFont(newFont, persist);
            return newFont;
        }

        public void SetFont(Font font, bool persist = false)
        {
            WriteFontDefinition(font);
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
                if (NamesContain(themeNames, theme))
                {
                    validTheme = theme;
                    return true;
                }

                foreach (string themeName in ThemeNameHelper.GetPossibleThemeNames(theme))
                {
                    if (NamesContain(themeNames, themeName))
                    {
                        validTheme = themeName;
                        return true;
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

                List<string> loadedThemes = new();
                foreach (string cssClass in terminalRoot.GetClasses())
                {
                    if (ThemeNameHelper.IsThemeName(cssClass))
                    {
                        loadedThemes.Add(cssClass);
                    }
                }

                foreach (string loadedTheme in loadedThemes)
                {
                    terminalRoot.RemoveFromClassList(loadedTheme);
                }

                terminalRoot.AddToClassList(validatedTheme);
            }
        }

        public void HandlePrevious()
        {
            if (!IsOpenState(_state))
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
            if (!IsOpenState(_state))
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
            if (!IsOpenState(_state))
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
            if (!IsOpenState(_state))
            {
                return;
            }

            try
            {
                /*
                    Commands with a completion provider own completion for
                    their input shape: cycling replaces only the active token.
                    Without a provider the legacy history-based completion
                    below runs unchanged.
                 */
                if (TryTokenComplete(searchForward))
                {
                    return;
                }

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

        internal void ApplyPendingCaret()
        {
            if (_pendingCaretIndex is not int index || _commandInput == null)
            {
                return;
            }

            if (_commandInput.value.Length < index)
            {
                /*
                   The queued position targets input the field does not hold
                   yet; the value sync applies it once the field catches up.
                */
                return;
            }

            bool focused =
                _textInput != null
                && _textInput.focusController != null
                && _textInput.focusController.focusedElement == _textInput;

            /*
                The field applies programmatic value writes on its own
                schedule, and that apply can re-clamp the caret after this
                pass's write. Consume the marker only while focused once the
                caret stuck on a later pass; an unfocused field keeps the
                marker (Bugbot: unfocused completion caret jump) so a later
                fresh focus cannot send the caret to line end.
             */
            if (focused && _commandInput.cursorIndex == index)
            {
                _pendingCaretIndex = null;
                return;
            }

            _commandInput.cursorIndex = index;
            _commandInput.selectIndex = index;
        }

        /*
            Internal for test coverage of the TerminalUI-level standard
            operations benchmarks (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal void ResetAutoComplete()
        {
            if (_input == null)
            {
                return;
            }

            _lastKnownCommandText = _input.CommandText ?? string.Empty;
            ResetTokenCompletion();
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

        /*
            Internal for test coverage of the first-open tree build
            measurement (see WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal void EnsureUI()
        {
            if (_terminalContainer != null)
            {
                return;
            }

            SetupUI();
        }

        /*
            Internal for test coverage of the first-open tree build
            measurement (see WallstopStudios.DxCommandTerminal.Tests.Runtime):
            the benchmark rebuilds through the same teardown the disable path
            pays.
         */
        internal void TeardownUI()
        {
            _terminalContainer = null;
            _logScrollView = null;
            _autoCompleteContainer = null;
            _inputContainer = null;
            _runButton = null;
            _inputCaretLabel = null;
            _commandInput = null;
            _textInput = null;
            _stateButtonContainer = null;
            _lastCodeSyncedValue = null;
        }

        /*
            Internal for test coverage of the steady-state refresh
            measurement (see WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal void RefreshUI()
        {
            if (_terminalContainer == null)
            {
                return;
            }

            /*
                The palette shares this document when both surfaces live on one
                GameObject. While it is open it owns the root: RefreshUI would
                force the root height to the terminal's (zero when closed), so
                the palette panel's percent position collapses to the top, and
                its focus/caret writes steal keys from the palette input.
             */
            if (IsPaletteSurfaceOpen())
            {
                return;
            }

            /*
                Heights and the input display are written on every pass,
                including state-transition frames: the idle gate can skip the
                very next pass, and a zero-duration close snaps its height on
                the same frame SetState flags the transition, so this is the
                only pass that can land the final closed height.
             */
            _uiDocument.rootVisualElement.style.height = _currentWindowHeight;
            _terminalContainer.style.height = _currentWindowHeight;
            _terminalContainer.style.width = Screen.width;
            DisplayStyle commandInputStyle =
                _currentWindowHeight <= 30 ? DisplayStyle.None : DisplayStyle.Flex;

            _needsFocus |=
                _inputContainer.resolvedStyle.display != commandInputStyle
                && commandInputStyle == DisplayStyle.Flex;
            _inputContainer.style.display = commandInputStyle;

            if (_commandIssuedThisFrame)
            {
                return;
            }

            RefreshLogs();
            RefreshAutoCompleteHints();
            string commandInput = _input.CommandText;
            if (!string.Equals(_commandInput.value, commandInput))
            {
                _isCommandFromCode = true;
                _lastCodeSyncedValue = commandInput;
                _commandInput.value = commandInput;
            }
            else if (
                _needsFocus
                && _textInput != null
                && _textInput.focusController != null
                && _textInput.focusable
                && _textInput.resolvedStyle.display != DisplayStyle.None
                && _commandInput.resolvedStyle.display != DisplayStyle.None
            )
            {
                if (_textInput.focusController.focusedElement != _textInput)
                {
                    /*
                        Retry focus only: the scheduled pass must not re-run
                        the caret-to-end behavior of a fresh focus, which
                        would clobber a caret the user or a completion placed
                        in the meantime. The gate keeps a retry scheduled
                        before the palette took over from stealing focus
                        after it opens.
                     */
                    _textInput.schedule.Execute(RetryInputFocus).ExecuteLater(0);
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

            /*
               Pending carets are applied on every pass: an accepted
               completion can be a text no-op that must still move the
               caret. The marker is consumed once the write sticks on a
               focused pass.
            */
            ApplyPendingCaret();
            RefreshStateButtons();
        }

        /*
            Single font-application path for every surface: SetFont, and the
            first UI build in SetupUI (whose SetFont call runs before
            InitializeFont has resolved a pack font, so the build must write
            the resolved definition itself). The definition lives on the
            document root and inherits into the terminal tree, covering
            single- and shared-document surfaces alike.
         */
        private void WriteFontDefinition(Font font)
        {
            if (font == null)
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

            root.style.unityFontDefinition = new StyleFontDefinition(font);
        }

        private int NormalizeCaret(int caret)
        {
            string input = _input.CommandText ?? string.Empty;
            return caret < 0 || input.Length < caret ? input.Length : caret;
        }

        private void ResetTokenCompletion()
        {
            _tokenCompletionIndex = null;
            _tokenCompletionInput = null;
            _tokenCompletionCaret = 0;
            _tokenCompletionReplacementStart = 0;
            _tokenCompletionReplacementLength = 0;
            _tokenCompletionQuoted = false;
            _tokenCompletionAppliedText = null;
            _pendingCaretIndex = null;
            _tokenCompletions.Clear();
        }

        /*
            Provider-based token completion. Runs before the legacy
            history-based completion: when the command under the caret has a
            completion provider, cycling replaces only the active token, and
            full-line history suggestions are suppressed so they cannot
            overwrite unrelated input. Returns false (and leaves the legacy
            path in charge) when there is no provider to answer.
         */
        private bool TryTokenComplete(bool searchForward)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null || _commandInput == null)
            {
                return false;
            }

            /*
                The snapshot is taken on the first press of a cycle and
                survives applications; a caret move that is not ours
                restarts the request from the live state.
             */
            string currentInput = _input.CommandText ?? string.Empty;
            int liveCaret = _commandInput.cursorIndex;
            /*
                The applied-input branch matches text only: panel handling
                can move the caret after a programmatic sync, so a caret
                check would drop cycles. Typing restarts the request.
             */
            bool keepRequest =
                _tokenCompletionInput != null
                && (
                    string.Equals(
                        currentInput,
                        _tokenCompletionAppliedText,
                        StringComparison.Ordinal
                    )
                    || (
                        string.Equals(currentInput, _tokenCompletionInput, StringComparison.Ordinal)
                        && NormalizeCaret(liveCaret) == _tokenCompletionCaret
                    )
                );
            if (!keepRequest)
            {
                _tokenCompletionInput = currentInput;
                _tokenCompletionCaret = NormalizeCaret(liveCaret);
            }

            _tokenCompletionsTemp.Clear();
            bool hasProvider = shell.TryComplete(
                CommandExecutionContext.Current,
                _tokenCompletionInput,
                _tokenCompletionCaret,
                _tokenCompletionsTemp,
                out CommandCompletionContext completionContext
            );
            if (!hasProvider)
            {
                ResetTokenCompletion();
                return false;
            }

            _tokenCompletionReplacementStart = completionContext.ReplacementStart;
            _tokenCompletionReplacementLength = completionContext.ReplacementLength;
            _tokenCompletionQuoted = completionContext.IsQuoted;

            /*
                A provider-attached command owns its input shape: stale
                full-line history hints go away until the next input
                change re-derives them.
             */
            _lastCompletionBuffer.Clear();
            _lastCompletionIndex = null;

            if (_tokenCompletionsTemp.Count == 0)
            {
                /*
                   A provider is attached but has nothing to offer; do not
                   substitute full-line history suggestions for the token.
                */
                return true;
            }

            bool equivalent =
                _tokenCompletionIndex != null
                && TokenCompletionsEquivalent(_tokenCompletionsTemp, _tokenCompletions);
            if (!equivalent)
            {
                _tokenCompletions.Clear();
                _tokenCompletions.AddRange(_tokenCompletionsTemp);
                _tokenCompletionIndex = searchForward ? 0 : _tokenCompletions.Count - 1;
            }
            else if (searchForward)
            {
                _tokenCompletionIndex = (_tokenCompletionIndex.Value + 1) % _tokenCompletions.Count;
            }
            else
            {
                _tokenCompletionIndex =
                    (_tokenCompletionIndex.Value - 1 + _tokenCompletions.Count)
                    % _tokenCompletions.Count;
            }

            ApplyTokenCompletion(_tokenCompletions[_tokenCompletionIndex.Value]);
            return true;
        }

        private void ApplyTokenCompletion(CommandCompletion completion)
        {
            string input = _tokenCompletionInput ?? string.Empty;
            int replacementStart;
            int replacementLength;
            if (completion.Replacement is CommandCompletionReplacement override2)
            {
                replacementStart = override2.Start;
                replacementLength = override2.Length;
            }
            else
            {
                replacementStart = _tokenCompletionReplacementStart;
                replacementLength = _tokenCompletionReplacementLength;
            }
            if (
                replacementStart < 0
                || replacementLength < 0
                || input.Length < replacementStart
                || input.Length - replacementStart < replacementLength
            )
            {
                return;
            }

            if (
                !CommandTokenizer.TryPrepareInsertion(
                    input,
                    completion.InsertionText,
                    replacementStart,
                    replacementLength,
                    _tokenCompletionQuoted,
                    out string insertion,
                    out replacementStart,
                    out replacementLength,
                    wholeToken: replacementStart == _tokenCompletionReplacementStart
                        && replacementLength == _tokenCompletionReplacementLength
                )
            )
            {
                return;
            }
            string newInput = input
                .Remove(replacementStart, replacementLength)
                .Insert(replacementStart, insertion);
            _tokenCompletionAppliedText = newInput;
            _pendingCaretIndex = replacementStart + insertion.Length;

            _input.CommandText = newInput;
            _needsFocus = true;
        }

        private void ResetWindowIdempotent()
        {
            int height = Screen.height;
            float oldTargetHeight = _targetWindowHeight;
            try
            {
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
            finally
            {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                if (oldTargetHeight != _targetWindowHeight)
                {
                    StartHeightAnimation();
                }
            }
        }

        /*
            Builds the visual tree the first time the terminal opens. The
            tree is not constructed while the terminal is closed, so a
            component that never opens pays no UI-construction cost.
         */
        /*
            Drops the built visual tree references so the next open rebuilds
            from scratch. Called from OnDisable after the document root has
            been cleared; the stale detached elements must not be mistaken
            for a built tree by EnsureUI.
         */
        private void SetupUI()
        {
            /*
                Apply the shared settings asset before any element reads a
                config field: the rare external SetState on a disabled
                component can reach here before Awake runs.
             */
            ApplySharedSettings();

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

            /*
                A null persisted font means "derive from the pack" (the
                InitializeFont contract below), not a misconfiguration:
                route the resolved font through so rebuilds reapply the
                definition without tripping SetFont's null guard. On the
                first build CurrentFont is still null here; the resolved
                definition is written after InitializeFont below.
             */
            SetFont(_persistedFont != null ? _persistedFont : CurrentFont);
            uiRoot.Clear();
            VisualElement root = new();
            uiRoot.Add(root);
            root.name = TerminalRootName;
            root.AddToClassList("terminal-root");

            InitializeTheme(root);
            InitializeFont();
            WriteFontDefinition(_runtimeFont);

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
            /*
                SetupUI can run before Awake (an external SetState while the
                component is inactive); the input abstraction may not exist
                yet, so the initial field sync tolerates that and the first
                RefreshUI pass applies it once Awake has resolved it.
             */
            _lastCodeSyncedValue = _input != null ? _input.CommandText : string.Empty;
            _commandInput.value = _lastCodeSyncedValue;
            /*
                The callback is explicitly static and receives the terminal
                through userArgs: it stays captureless, so registering it
                cannot allocate a closure or root this component through the
                element's callback registry, and any future capture fails the
                compile instead of silently leaking both.
             */
            _commandInput.RegisterCallback<ChangeEvent<string>, TerminalUI>(
                static (evt, context) =>
                {
                    if (context._input == null)
                    {
                        /*
                            The input abstraction is not resolved yet (Awake
                            has not run); there is nothing to sync with.
                         */
                        evt.StopPropagation();
                        return;
                    }

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
                            context._lastCodeSyncedValue = context._input.CommandText;
                            context._commandInput.value = context._input.CommandText;
                        }
                        evt.StopPropagation();
                        return;
                    }

                    context._input.CommandText = evt.newValue;

                    bool echoFromCode = context._isCommandFromCode;
                    if (
                        !echoFromCode
                        && string.Equals(
                            evt.newValue,
                            context._lastCodeSyncedValue,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        /*
                           One echo follows each programmatic write; a later
                           same-value edit is a genuine edit, not an echo.
                        */
                        context._lastCodeSyncedValue = null;
                        echoFromCode = true;
                    }

                    context._runButton.style.display =
                        context.showGUIButtons
                        && !string.IsNullOrWhiteSpace(context._input.CommandText)
                        && !string.IsNullOrWhiteSpace(context.runButtonText)
                            ? DisplayStyle.Flex
                            : DisplayStyle.None;
                    if (!echoFromCode)
                    {
                        context.ResetAutoComplete();
                    }

                    context._isCommandFromCode = false;
                },
                userArgs: this,
                useTrickleDown: TrickleDown.TrickleDown
            );

            _inputContainer.Add(_commandInput);
            ResetTokenCompletion();
            _textInput = _commandInput.Q<VisualElement>("unity-text-input");

            _stateButtonContainer = new VisualElement { name = "StateButtonContainer" };
            _stateButtonContainer.AddToClassList("state-button-container");
            root.Add(_stateButtonContainer);
            RefreshStateButtons();

            /*
                A freshly built tree carries stale heights until a RefreshUI
                pass clamps them; the idle gate owes that pass before it may
                skip (an out-of-band rebuild while closed would otherwise
                render at the wrong height until the next open).
             */
            _needsInitialRefresh = true;
        }

        private void InitializeTheme(VisualElement root)
        {
            if (_themePack == null)
            {
                Debug.LogError("No theme pack assigned, cannot initialize theme.", this);
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
                _runtimeTheme = FindName(themeNames, "dark");
                if (_runtimeTheme == null)
                {
                    _runtimeTheme = FindName(themeNames, "light");
                }
                if (_runtimeTheme == null)
                {
                    _runtimeTheme = themeNames[0];
                }

                /*
                   Defaulting from an empty or unknown persisted name is normal
                   operation; only a stale persisted name deserves a warning.
                */
                if (_persistedTheme != null)
                {
                    Debug.LogWarning(
                        $"Persisted theme '{_persistedTheme}' not found in the pack, defaulting to '{_runtimeTheme}'.",
                        this
                    );
                }
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
                _runtimeFont = FindFont(loadedFonts, requireMono: true, requireRegular: true);
                if (_runtimeFont == null)
                {
                    _runtimeFont = FindFont(loadedFonts, requireMono: true, requireRegular: false);
                }
                if (_runtimeFont == null)
                {
                    _runtimeFont = FindFont(loadedFonts, requireMono: false, requireRegular: true);
                }
                if (_runtimeFont == null)
                {
                    foreach (Font font in loadedFonts)
                    {
                        if (font != null)
                        {
                            _runtimeFont = font;
                            break;
                        }
                    }
                }
            }

            if (_runtimeFont != null)
            {
                // The pack defaulting itself is normal operation: stay silent.
                return;
            }

            Debug.LogWarning(
                "Font pack contains no fonts; defaulting to OS font 'Courier New' 16pt.",
                this
            );
            _runtimeFont = Font.CreateDynamicFontFromOSFont("Courier New", 16);
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

        private void FocusInput()
        {
            if (_textInput == null)
            {
                return;
            }

            /*
                A retry scheduled before the palette opened must not yank
                panel focus back to the terminal input after the palette
                took over.
             */
            if (IsPaletteSurfaceOpen())
            {
                return;
            }

            bool alreadyFocused = _textInput.focusController.focusedElement == _textInput;
            if (alreadyFocused || _pendingCaretIndex.HasValue)
            {
                /*
                    The field already holds focus, or a completion queued a
                    caret: keep the caret and let ApplyPendingCaret place it.
                 */
                _textInput.Focus();
                return;
            }

            // A fresh focus places the caret at the end of the input.
            _textInput.Focus();
            int textEndPosition = _commandInput.value.Length;
            _commandInput.cursorIndex = textEndPosition;
            _commandInput.selectIndex = textEndPosition;
        }

        private void RetryInputFocus()
        {
            if (IsPaletteSurfaceOpen())
            {
                return;
            }

            _textInput?.Focus();
        }

        /*
            Document-scoped: a terminal only yields its shared surface to a
            palette opened on the same UIDocument, independent of which
            palette claimed the static Instance.
         */
        private bool IsPaletteSurfaceOpen()
        {
            UIDocument document = _uiDocument;
            return document != null && CommandPaletteUI.IsOpenOn(document);
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
                    while (content.childCount < logs.Count)
                    {
                        Label logText = new();
                        logText.AddToClassList("terminal-output-label");
                        content.Add(logText);
                    }
                }
                else if (logs.Count < content.childCount)
                {
                    int logCount = logs.Count;
                    for (int i = content.childCount - 1; logCount <= i; --i)
                    {
                        content.RemoveAt(i);
                    }
                }

                _needsScrollToEnd = true;
            }

            if (dirty)
            {
                int logCount = logs.Count;
                int childCount = content.childCount;
                for (int i = 0; i < logCount && i < childCount; ++i)
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
                    int hintCount = _autoCompleteContainer.childCount;
                    for (int i = 0; i < hintCount && i < bufferLength; ++i)
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

            int stateButtonCount = _stateButtonContainer.childCount;
            for (int i = 0; i < stateButtonCount; ++i)
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
            bool isExpanding = _initialWindowHeight < _targetWindowHeight;

            if (isExpanding)
            {
                selectedCurve = easeOutCurve;
                animationDuration = easeOutTime;
            }
            else
            {
                selectedCurve = easeInCurve;
                animationDuration = easeInTime;
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

        private enum ScrollBarCaptureState
        {
            [Obsolete("Use a valid value")]
            None = 0,
            DraggerActive = 1,
            TrackerActive = 2,
            Inactive = 3,
        }
    }
}
