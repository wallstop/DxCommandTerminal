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

        /*
            Log refreshes a queued search jump waits for the view to lay out
            the line it is aiming at. The layout runs after the pass that
            creates the line, so the first attempt has nothing to scroll to and
            the second one does; a view that never lays out at all - a headless
            editor, a window collapsed to nothing - is answered by dropping the
            request rather than retrying it every pass forever.

            Counted in refreshes rather than frames, because those are the only
            ones that can act on it: a closed terminal runs no log refresh at
            all, and a frame budget would never be spent while it stayed shut.
         */
        private const int FindScrollFrameBudget = 4;

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

        /*
            Internal for test coverage: a transition assertion needs the state
            a frame left behind, and IsClosed cannot answer that while the
            window height is still settling toward its target.
         */
        internal TerminalState State => _state;

        /*
            Internal for test coverage of the search's scroll state (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).

            A search queues a jump to its match and suppresses the
            scroll-to-end that running a command asks for; `EnterCommand`
            re-asserts that scroll after the handler returns, and whether the
            search's suppression survived is invisible without a laid-out view -
            which is exactly the gap that let the re-assert overwrite the jump
            silently. The flags are the decision, and they read the same on a
            host that lays out no panel.

            The follower's own `Detached` is deliberately not exposed here: it
            only flips when a scroll is actually placed, so on a host with no
            layout it reads the same whether or not anything is wrong.
         */
        internal bool FindScrollQueued => _pendingFindScroll.HasValue;

        internal bool WantsScrollToEnd => _needsScrollToEnd;

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
        internal int _logBufferSize = TerminalSession.Config.DefaultLogBufferSize;

        /*
            Internal for test coverage of the shared settings asset (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        [SerializeField]
        internal int _historyBufferSize = TerminalSession.Config.DefaultHistoryBufferSize;

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
        private readonly List<SerializedProperty> _fontPackProperties = new();
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

        /*
            Internal for test coverage of hint selection (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal ScrollView _autoCompleteContainer;

        private TerminalState _state = TerminalState.Closed;
        private float _currentWindowHeight;
        private float _targetWindowHeight;
        private float _realWindowHeight;
        private bool _unityLogAttached;
        private bool _started;
        private bool _needsFocus;
        private bool _needsScrollToEnd;
        private LogTailFollower _logTail;
        private long? _lastSeenBufferVersion;

        /*
            The search the developer set over the log view, and where in it
            they are. Per terminal, not per session: it is a view of one
            screen, and a second terminal sharing the log buffer is its own
            view with its own reason to be reading a different part of it.
         */
        private readonly LogFilter _logFilter = new();

        /*
            The log read the frame draws: the window copy and the search's
            kept lines live in the cache, which also answers an unchanged
            frame without reading the buffer again.
         */
        private readonly LogWindowCache _logWindowCache;

        /*
            A search asked for one of its matches to be brought into view, and
            the log refreshes left to wait for that line to have a layout.

            The match is held as its ordinal in the kept list rather than as the
            child index it currently is, and the index is derived at apply time.
            The ring rotates under a search that is left standing - every new
            line moves it - so a cached index names a different match a frame
            later, and a search that jumped to the wrong line is worse than one
            that jumped to none.
         */
        private int? _pendingFindScroll;

        private int _findScrollPasses;

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
            The candidate each drawn suggestion row carries, so a pass can
            tell whether the bar still shows the buffer. A row is built once
            and re-used, so a candidate set that changes without changing
            count - one more keystroke in Always mode - left the previous
            candidates on screen and let a click apply a candidate the
            developer could not see.
         */
        private readonly List<string> _renderedHintCandidates = new();

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

        /*
            The command line's own history. A TextField has none, so this is
            the whole of Ctrl+Z; see TextFieldUndo for why the surface hands
            the stack the field's current text on every key instead of
            announcing its own writes.
         */
        private readonly TextFieldUndo _commandUndo = new();

        public TerminalUI()
        {
            _logWindowCache = new LogWindowCache(_logFilter);
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
                        LogTextSanitizer.Sanitize(
                            $"Invalid buffer size '{_logBufferSize}', must be greater than zero. Defaulting to 0 (empty buffer)."
                        ),
                        this
                    );
                    break;
                case < 10:
                    Debug.LogWarning(
                        LogTextSanitizer.Sanitize(
                            $"Unsupported buffer size '{_logBufferSize}', recommended size is > 10."
                        ),
                        this
                    );
                    break;
            }

            switch (_historyBufferSize)
            {
                case <= 0:
                    Debug.LogError(
                        LogTextSanitizer.Sanitize(
                            $"Invalid buffer size '{_historyBufferSize}', must be greater than zero. Defaulting to 0 (empty buffer)."
                        ),
                        this
                    );
                    break;
                case < 10:
                    Debug.LogWarning(
                        LogTextSanitizer.Sanitize(
                            $"Unsupported buffer size '{_historyBufferSize}', recommended size is > 10."
                        ),
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

            string[] fontPackPropertiesTracked = { nameof(_fontPack) };
            TrackProperties(fontPackPropertiesTracked, _fontPackProperties);

            string[] staticStaticPropertiesTracked =
            {
                nameof(_logBufferSize),
                nameof(_historyBufferSize),
                nameof(_ignoredLogTypes),
                nameof(_disabledCommands),
                nameof(ignoreDefaultCommands),
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
                            LogTextSanitizer.Sanitize(
                                $"Failed to track/find window property {propertyName}, updates to this property will be ignored."
                            ),
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

            /*
                A pack swap must re-resolve and rewrite the applied font the
                way a theme swap re-runs InitializeTheme; the static-state
                refresh never touches fonts. Guarded on a built tree: a
                closed terminal re-resolves on the next open.
             */
            if (CheckForRefresh(_fontPackProperties))
            {
                if (_uiDocument != null && _terminalContainer != null)
                {
                    InitializeFont();
                    WriteFontDefinition(_runtimeFont);
                }
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
                _renderedHintCandidates.Clear();
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
            Whether the command field - or the inner text-input element the
            focus controller may report instead - holds panel focus. A key
            that types text belongs to that field while it does, so the input
            poll leaves text-producing hotkeys alone
            (see InputHelpers.ProducesTypedText).

            A closed terminal does not count, whatever the focus controller
            still names: closing hides the field instead of detaching it, and a
            hidden field receives no character, so it must not hold the console
            key hostage after a close.
         */
        internal bool InputOwnsFocus
        {
            get
            {
                if (_commandInput == null || !IsOpenState(_state))
                {
                    return false;
                }

                VisualElement focused =
                    _commandInput.panel?.focusController?.focusedElement as VisualElement;
                return focused != null
                    && (focused == _commandInput || _commandInput.Contains(focused));
            }
        }

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
            CommandPaletteUI.ResetForNextPlaySession();
            CommandArg.ResetForNextPlaySession();
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

        /// <summary>
        ///     Reports whether any console text field - a live terminal's
        ///     command line or a palette's query - holds panel focus. The
        ///     input polls that still hold a text-producing key back (the
        ///     six non-toggle controls on the keyboard controller, the
        ///     PlayerInput messages the typing rule covers) ask this so the
        ///     key is left to the surface being typed into (see
        ///     <see cref="InputHelpers.ProducesTypedText"/>). The toggle
        ///     controls sit outside the rule (#218). It lives here so the
        ///     two surfaces share one answer.
        /// </summary>
        internal static bool AnyConsoleFieldOwnsFocus()
        {
            return AnyInputOwnsFocus() || CommandPaletteUI.AnyInputOwnsFocus();
        }

        /// <summary>
        ///     Reports whether any live terminal's command field holds panel
        ///     focus. The keyboard controller's typed-text hold asks
        ///     <see cref="AnyConsoleFieldOwnsFocus"/> (which asks this) so a
        ///     key that types text is left to the surface being typed into
        ///     (see <see cref="InputHelpers.ProducesTypedText"/>); the toggle
        ///     controls do not consult it (#218).
        /// </summary>
        internal static bool AnyInputOwnsFocus()
        {
            int liveCount = LiveTerminals.Count;
            for (int index = liveCount - 1; 0 <= index; --index)
            {
                TerminalUI candidate = LiveTerminals[index];
                if (candidate != null && candidate.InputOwnsFocus)
                {
                    return true;
                }
            }

            return false;
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
                    LogTextSanitizer.Sanitize(
                        currentFont == null
                            ? $"Setting font to {font.name}."
                            : $"Changing font from {currentFont.name} to {font.name}."
                    ),
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
                Debug.Log(
                    LogTextSanitizer.Sanitize(
                        $"Changing theme from {currentTheme} to {friendlyThemeName}."
                    ),
                    this
                );
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

        /*
            Freezes or resumes the caret blink schedule. Capture and test rigs
            pin the caret visible so rendered pixels never depend on elapsed
            wall time; resume re-arms the normal blink schedule.
         */
        public void SetCursorBlinkPaused(bool paused)
        {
            if (!paused)
            {
                ScheduleBlinkingCursor();
                return;
            }

            _cursorBlinkSchedule?.Pause();
            _commandInput?.EnableInClassList("transparent-cursor", false);
            _commandInput?.EnableInClassList("styled-cursor", true);
        }

        public void HandlePrevious()
        {
            if (!IsOpenState(_state))
            {
                return;
            }

            RecallHistoryLine(Terminal.History?.Previous(skipSameCommandsInHistory));
        }

        public void HandleNext()
        {
            if (!IsOpenState(_state))
            {
                return;
            }

            RecallHistoryLine(Terminal.History?.Next(skipSameCommandsInHistory));
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

                /*
                    Running a command is an explicit request for its output, so
                    the tail follows again even when the developer had scrolled
                    away to read an earlier one.

                    A search is the exception, and this is the only site that
                    can know it. The handler ran three lines above, and a
                    search that queued a jump to its match asked for that line,
                    not for the newest one. Re-attaching here overwrites the
                    suppression the jump sets for itself, so the view scrolls
                    to the end and the jump then either loses the race or
                    spends its budget waiting for a layout that keeps moving.
                    A developer watching that sees a search that filtered
                    correctly and then ignored them.
                 */
                if (!_pendingFindScroll.HasValue)
                {
                    _logTail.Attach();
                    _needsScrollToEnd = true;
                }
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

#if UNITY_2022_1_OR_NEWER
            _commandInput.cursorIndex = index;
            _commandInput.selectIndex = index;
#else
            /*
                2021.3 exposes the caret getters only; once the field holds
                the input, the engine owns placement and the marker retires.
             */
            _pendingCaretIndex = null;
#endif
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

            /*
                The stack described the field that just went away, so a rebuild
                that kept it would answer an undo with a line from before the
                console was closed.
             */
            _commandUndo.Clear();

            /*
                The jump was aimed at a line in the tree that just went away.
                A rebuild draws the filtered log from scratch, so keeping the
                request would scroll the fresh view to whatever line took that
                index.
             */
            DropFindScroll();
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
            A click applies the candidate its row shows now. Two things move
            under the bar while it is open - the candidate set, and the
            carousel's row order - so the row's position is resolved at click
            time and the text is read then; the closure used to capture both
            when the row was built, so it applied a candidate the developer
            could not see. The queued caret matches the palette: a row
            replaces the whole line, so the caret belongs at its end. 2022.1
            and newer write it; see RecallHistoryLine for 2021.3.

            Internal for test coverage of what a row applies (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime); Unity's
            dispatcher drops synthetic pointer events, so the click itself is
            driven at this level.
         */
        internal void ApplyHint(int index)
        {
            if (index < 0 || _renderedHintCandidates.Count <= index)
            {
                return;
            }

            string candidate = _renderedHintCandidates[index];
            _input.CommandText = candidate;
            _pendingCaretIndex = candidate.Length;
            _lastCompletionIndex = index;
            _needsFocus = true;
        }

        /*
            `find`: the log view keeps only the lines that hold the text, and
            the first of them is brought into view.

            The counts are read here rather than from the view's next pass,
            because the command answers now and a count read a frame later
            would describe a log that has moved on - and because a frame is
            long enough for the log to gain a line the answer does not know
            about.

            The text is not repeated in the answer. The command echo directly
            above it in the log already shows exactly what was searched for,
            and a line that repeated it would match the search that produced
            it: the next `find` of the same text would then report one more
            match than the developer can count on screen, every time they ran
            it.
         */
        internal void SetLogFilter(string query)
        {
            if (Terminal.Buffer == null)
            {
                /*
                    No session means no log to search, and the facade getter
                    legitimately reads null before the session exists and after
                    a play-session reset. Reported rather than treated as "no
                    matches", which would be an answer about a log that is not
                    there.
                 */
                LogFindWarning("There is no log to search yet.");
                return;
            }

            if (!_logFilter.SetQuery(query))
            {
                /*
                    The search already in place is left alone, and so is any
                    jump it had queued: a refused query is not a request to
                    change what is on screen.
                 */
                LogFindWarning("Nothing to search for. clear-filter shows every log line again.");
                return;
            }

            int matches = ReadRenderedLogWindow(Terminal.Buffer, out _);
            if (matches < 1)
            {
                /*
                    The search stays set. Dropping it here would mean a typo
                    silently put the whole log back on screen, and the developer
                    would have to type the search again to see that it had hit
                    nothing.
                 */
                DropFindScroll();
                LogFindWarning(
                    "No log line matches the search. clear-filter shows every line again."
                );
                return;
            }

            QueueFindScroll();

            /*
                "of" counts the lines the search could have matched, not the
                ones it did - so the total and the match count are not
                interchangeable words here, and "matching log lines" on the
                total would say the opposite of what the number means.
             */
            LogFindReply($"Showing {matches} of {_logFilter.TotalCount} log lines.");
        }

        /*
            `find` with no argument, and F3: the next match, or the one before
            it, wrapping.

            The window is re-read first because the ring rotates under a search
            that is left standing: the matches the developer is stepping
            through are the ones the log holds now, not the ones it held when
            they typed the search.
         */
        internal void StepLogFilter(bool forward)
        {
            if (!_logFilter.IsActive)
            {
                LogFindWarning("No search is set. find <text> sets one.");
                return;
            }

            if (Terminal.Buffer == null)
            {
                /*
                    The counts a step reports are the last ones read, and with
                    no log there is nothing to step through - stepping anyway
                    would answer "Match 7 of 20" for a log that holds nothing.
                 */
                LogFindWarning("There is no log to search yet.");
                return;
            }

            ReadRenderedLogWindow(Terminal.Buffer, out _);
            if (!(forward ? _logFilter.StepForward() : _logFilter.StepBackward()))
            {
                LogFindWarning("No log line matches the search.");
                return;
            }

            QueueFindScroll();
            LogFindReply($"Match {_logFilter.CurrentMatch} of {_logFilter.MatchCount}.");
        }

        internal void ClearLogFilter()
        {
            if (!_logFilter.IsActive)
            {
                LogFindReply("No search is set.");
                return;
            }

            _logFilter.Clear();
            DropFindScroll();
            LogFindReply("Search cleared. The log shows every line again.");
        }

        /*
            The routing a key that walks the line's own history gets, and the
            same two answers the log keys give: a key this does not own is left
            alone, and a key it does own is consumed so it does not also reach
            the field it arrived through.

            The stack is handed the command text rather than the field's value,
            because the abstraction is what the terminal reads and what the
            value write below pushes back out; on a frame where the field has
            not caught up, the two are different strings and the field's would
            undo a state the command line never held.

            Internal for test coverage: a host that does not route synthetic
            keys to the field can still drive the routing directly, so the
            behaviour is measured rather than reported as an environment limit.
         */
        internal bool TryApplyHistoryKey(KeyDownEvent evt)
        {
            if (_input == null || _commandInput == null)
            {
                return false;
            }

            bool commandOrCtrl = evt.commandKey || evt.ctrlKey;
            bool forward = TextFieldUndo.IsRedo(evt.keyCode, commandOrCtrl, evt.shiftKey);
            if (!forward && !TextFieldUndo.IsUndo(evt.keyCode, commandOrCtrl, evt.shiftKey))
            {
                return false;
            }

            if (
                !_commandUndo.Step(
                    _input.CommandText,
                    _commandInput.cursorIndex,
                    forward,
                    out string text,
                    out int caret
                )
            )
            {
                return false;
            }

            SetCommandTextFromCode(text, caret);
            return true;
        }

        /*
            Every line the search writes, through one door.

            A command that answers in the console writes ordinary log text, and
            the search's answer is no different from any other until something
            stops counting it. The words in it are ordinary words, so a query
            that happened to be one of them - "search", "log", "clear-filter" -
            matched the answer, and every repeat of the search added another
            match. A search that hit nothing would then report a hit, which is
            the one answer it must never give.

            Registering the text is what keeps the count honest; the log type
            cannot do it. A `Warning` is exactly what a developer is looking
            for, and a `ShellMessage` is any `Terminal.Log` the game made. The
            lines that are neither are the console's own, and there are only
            ever a handful - one per command the developer ran.
         */
        private void LogFindReply(string message)
        {
            _logFilter.IgnoreOwnReply(message);
            Terminal.Log(message);
        }

        private void LogFindWarning(string message)
        {
            _logFilter.IgnoreOwnReply(message);
            Terminal.Log(TerminalLogType.Warning, message);
        }

        /*
            The one write the two callers that replace the whole line share: a
            recalled history line, and an undone edit. The caret is the
            caller's, because a recall ends at the end of the line it recalled
            and an undo ends where the caret was when the state it undoes
            began. A recalled line belongs at its end because FocusInput only
            writes a caret on a fresh focus, and the field was already
            focused, so the caret stayed where the developer left it and the
            next character landed in the middle of the line they had just
            recalled.

            It goes through the input abstraction like every other
            programmatic write, so the field, the completion state, and the
            abstraction cannot disagree. The caret is queued after the
            completion reset, which retires a pending caret of its own, and
            through the path that retries, because a value write makes the text
            element re-run its own caret reset afterwards. 2022.1 and newer
            write it; 2021.3 has no caret setter, and its engine places the
            caret at the end after the value lands.
         */
        private void SetCommandTextFromCode(string text, int caret)
        {
            _input.CommandText = text;
            ResetAutoComplete();
            _pendingCaretIndex = text.SnapToTextBoundary(caret);
            _needsFocus = true;
        }

        private void RecallHistoryLine(string line)
        {
            string recalled = line ?? string.Empty;
            SetCommandTextFromCode(recalled, recalled.Length);
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
            int clamped = caret < 0 || input.Length < caret ? input.Length : caret;
            return input.SnapToTextBoundary(clamped);
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
            }

            /* One read, after the refill: the cycling below cannot change it. */
            int completionCount = _tokenCompletions.Count;
            if (!equivalent)
            {
                _tokenCompletionIndex = searchForward ? 0 : completionCount - 1;
            }
            else if (searchForward)
            {
                _tokenCompletionIndex = (_tokenCompletionIndex.Value + 1) % completionCount;
            }
            else
            {
                _tokenCompletionIndex =
                    (_tokenCompletionIndex.Value - 1 + completionCount) % completionCount;
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
            /*
                Snapped here, where the position is an offset into the text
                this write puts in the field. The field applies the value on its
                own schedule, so the queued position is applied later and
                against whatever the field holds then; snapping it there would
                snap it against the old text, and the retry that re-asserts the
                caret would hold it at the wrong place.
             */
            _pendingCaretIndex = newInput.SnapToTextBoundary(replacementStart + insertion.Length);

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
            /* A fresh view starts at zero, so no earlier pin describes it. */
            _logTail.Attach();

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

                        /*
                            The field now holds the code's value, and the code's
                            value is a state the field held. Recorded here
                            because the nested change event this write
                            dispatches re-enters this same branch, which stops
                            before the recording below can see it.
                         */
                        context._commandUndo.Observe(
                            context._input.CommandText,
                            context._commandInput.cursorIndex
                        );
                        evt.StopPropagation();
                        return;
                    }

                    context._input.CommandText = evt.newValue;

                    /*
                        The one recording point for the command line. Every
                        text that reaches the field - a keystroke, a paste, a
                        value write the terminal made - arrives here, so the
                        stack is a step behind the line rather than a step
                        behind the last history key. Without it the first
                        Ctrl+Z would empty a line the developer had typed
                        rather than taking one keystroke back.
                     */
                    context._commandUndo.Observe(evt.newValue, context._commandInput.cursorIndex);

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

            /*
                A TextField owns no clipboard, so paste is a key the terminal
                has to answer for. Trickle-down, because the focused element
                is the field's own inner text input and the field's key
                handling sits below this; a real keystroke therefore arrives
                here before the field sees it. The callback is static and
                captureless for the reason the change callback above states,
                and the key is stopped only when a paste happened, so an
                ordinary V types a V.

                The log is answered here for the same reason. The command line
                holds panel focus for as long as the console is open, so the
                keys that scroll the log never reach the log view; routing
                them is the terminal's job, and it is the same trickle-down
                callback so the two answers cannot disagree about what
                "consumed" means.
             */
            _commandInput.RegisterCallback<KeyDownEvent, TerminalUI>(
                static (evt, context) =>
                {
                    /*
                        History before paste and before the log keys, because
                        it is the one key a developer reaches for while
                        looking at the line rather than at the log. It is
                        answered by the same callback and consumed the same
                        way, so a key handled here can never also reach the
                        field below.
                     */
                    if (context.TryApplyHistoryKey(evt))
                    {
                        KeyEvents.Consume(context._commandInput, evt);
                        return;
                    }

                    if (TextFieldPaste.TryApply(context._commandInput, evt, out _))
                    {
                        /*
                            Consumed the way the bar consumes, not just
                            stopped: KeyEvents is the shared definition
                            because the two surfaces used to differ here, and a
                            key answered in one and only half-answered in the
                            other reads as a flaky double action. The focused
                            element below the field is a TextElement with its
                            own paste handling, so letting the key continue
                            would paste the raw clipboard a second time,
                            newlines and all.
                         */
                        KeyEvents.Consume(context._commandInput, evt);
                        return;
                    }

                    if (context.TryScrollLog(evt))
                    {
                        KeyEvents.Consume(context._commandInput, evt);
                        return;
                    }

                    if (context.TryStepLogFilter(evt))
                    {
                        KeyEvents.Consume(context._commandInput, evt);
                    }
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
                        LogTextSanitizer.Sanitize(
                            $"Persisted theme '{_persistedTheme}' not found in the pack, defaulting to '{_runtimeTheme}'."
                        ),
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
#if UNITY_2022_1_OR_NEWER
            int textEndPosition = _commandInput.value.Length;
            _commandInput.cursorIndex = textEndPosition;
            _commandInput.selectIndex = textEndPosition;
#endif
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
            /*
                Capture the buffer once: the facade getter legitimately reads
                null before the session exists (and after a play-session
                reset), so every read in this method goes through the same
                guarded local instead of re-deriving nullability per access.
             */
            CommandLog buffer = Terminal.Buffer;
            if (buffer == null)
            {
                return;
            }

            if (_logScrollView == null)
            {
                return;
            }

            VisualElement content = _logScrollView.contentContainer;
            bool newLogs = _lastSeenBufferVersion != buffer.Version;
            bool dirty = newLogs;

            /*
                One consistent read of the whole window, not a count followed
                by a per-line index: a background thread's Debug.Log arrives
                through the same buffer this reads, and a count and the lines
                read under it separately could describe two different moments.
                The window is sized to the buffer's capacity, so it holds
                every entry a read can return, and it is written once and
                reused. A search narrows that read to the lines it keeps, and
                the count the view lays out is the count it draws.
             */
            int logCount = ReadRenderedLogWindow(buffer, out LogItem[] rendered);
            if (content.childCount != logCount)
            {
                dirty = true;
                if (content.childCount < logCount)
                {
                    while (content.childCount < logCount)
                    {
                        Label logText = new();
                        logText.AddToClassList("terminal-output-label");
                        content.Add(logText);
                    }
                }
                else if (logCount < content.childCount)
                {
                    for (int i = content.childCount - 1; logCount <= i; --i)
                    {
                        content.RemoveAt(i);
                    }
                }
            }

            if (dirty)
            {
                int childCount = content.childCount;
                for (int i = 0; i < logCount && i < childCount; ++i)
                {
                    VisualElement item = content[i];
                    LogItem logItem = rendered[i];
                    switch (item)
                    {
                        case TextField logText:
                        {
                            SetupLogText(logText, logItem);
                            logText.value = logItem.message;
                            break;
                        }
                        case Label logLabel:
                        {
                            SetupLogText(logLabel, logItem);
                            logLabel.text = logItem.message;
                            break;
                        }
                        case Button button:
                        {
                            SetupLogText(button, logItem);
                            button.text = logItem.message;
                            break;
                        }
                    }
                }

                if (logCount == content.childCount)
                {
                    _lastSeenBufferVersion = buffer.Version;
                }
            }

            _needsScrollToEnd |= ObserveLogTail(newLogs);
            ApplyPendingFindScroll(content, logCount);
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

        /*
            Reads the log read for this pass through the cache. The cache
            owns the reused window arrays and the memo, so a frame whose
            buffer version, capacity, and query are unchanged costs no copy
            and no search pass (#222).
         */
        private int ReadRenderedLogWindow(CommandLog buffer, out LogItem[] rendered)
        {
            return _logWindowCache.Read(buffer, out rendered);
        }

        /*
            Records which match the next refreshes have to bring into view.

            The ordinal, not a child index: the ring rotates under a search
            that is left standing, and the kept list shifts with it, so an index
            taken now names a different match a frame later.

            The scroll-to-end a command run asks for is dropped here rather than
            left to be re-armed. `EnterCommand` attaches the tail and sets the
            flag, and `ObserveLogTail` puts it back on every pass the follower
            is still following - so a jump that waited for a layout would be
            undone by a scroll to the end on the pass in between, and the
            developer would see the view go to the end and snap back.
         */
        private void QueueFindScroll()
        {
            int? match = _logFilter.CurrentMatch;
            if (!match.HasValue)
            {
                return;
            }

            _pendingFindScroll = match;
            _findScrollPasses = FindScrollFrameBudget;
            _needsScrollToEnd = false;
        }

        /*
            Forgets a queued jump, and the budget that was waiting for it. Every
            site that ends a search's intent to scroll calls this, so a request
            cannot outlive the decision that made it.
         */
        private void DropFindScroll()
        {
            _pendingFindScroll = null;
            _findScrollPasses = 0;
        }

        /*
            The match a search asked for, once the view can say where it is.

            A child's position is a layout result, and the layout runs after the
            pass that created the child: a search made this frame is aiming at
            children this frame has only just added, so the first pass has
            nothing to scroll to and the next one does. The request is dropped
            rather than retried forever, because a view that never lays out at
            all - a headless editor with no rendered view, a window collapsed
            to nothing - would otherwise carry a request nothing can act on,
            and a view that starts laying out much later would scroll to
            whatever line then held that index.

            The index is read from the ordinal here, not from the request: the
            kept list can have shifted between the request and this pass, so a
            cached index would name a different match than the one asked for.

            `content` is the reconciled container and `logCount` how many
            children it holds, so the index is in range by construction - the
            check is the clamp, and a match the search no longer has ends the
            request rather than scrolling somewhere arbitrary.
         */
        private void ApplyPendingFindScroll(VisualElement content, int logCount)
        {
            int? pending = _pendingFindScroll;
            if (!pending.HasValue)
            {
                return;
            }

            int index = pending.GetValueOrDefault() - 1;
            if (index < 0 || logCount <= index)
            {
                DropFindScroll();
                return;
            }

            VisualElement match = content[index];
            if (0f < match.layout.height)
            {
                DropFindScroll();
                ScrollToLogLine(match);
                return;
            }

            if (0 < _findScrollPasses)
            {
                --_findScrollPasses;
                return;
            }

            DropFindScroll();
        }

        /*
            Puts a line at the top of the log view, which is where a find puts
            its hit, and detaches the tail: a developer who searched is reading
            the line they found, and the output arriving next is not what they
            asked for. Following again is Ctrl+End, or a search that lands on
            the end.

            The offset is the line's own position in the content, which is the
            same space the scroller's value is in, and it is clamped to the
            extent the scroller holds now - a content that grew after the
            layout this read would otherwise leave the view past its end.
         */
        private void ScrollToLogLine(VisualElement line)
        {
            Scroller scroller = _logScrollView?.verticalScroller;
            if (scroller == null)
            {
                return;
            }

            scroller.value = Math.Max(0f, Math.Min(line.layout.yMin, scroller.highValue));
            _logTail.Detach(scroller.value, scroller.highValue);
            _needsScrollToEnd = false;
        }

        /*
            New output scrolls into view only while the log view is at its own
            end. A developer who scrolls up reads at their own pace, and
            scrolling back to the end follows again. The child count cannot
            answer this: a full ring buffer holds its count, so a child-count
            trigger stops following exactly when a session starts logging
            continuously.
         */
        private bool ObserveLogTail(bool newLogs)
        {
            Scroller scroller = _logScrollView?.verticalScroller;
            if (scroller == null)
            {
                return false;
            }

            return _logTail.Observe(scroller.value, scroller.highValue, newLogs);
        }

        /*
            Moves the log for a key that arrived at the command field, because
            the command line holds panel focus for as long as the console is
            open. False means the key was not the log's to answer and the
            caller leaves it alone, so typing, history recall, completion, and
            closing are untouched.

            A key the log answers detaches the tail, which is what makes the
            scroll a first-class state: a developer paging back to read an
            error is not yanked to the newest line by the next frame's output.
            The follower learns that from the scroller's own value on the next
            pass, so all this has to do is place the value and clear a tail
            pin that has not been spent yet.
         */
        private bool TryScrollLog(KeyDownEvent evt)
        {
            /*
                Both flags, for the reason the paste path checks both: which
                flag a command modifier arrives in is a per-editor detail
                (Command on macOS, Control elsewhere, and a Windows key can
                arrive as Command as well), and a developer who reaches the log
                with the key their platform calls the command key has to get
                there whichever flag it arrived in.
             */
            if (
                !LogScrollKeys.TryResolve(
                    evt.keyCode,
                    evt.commandKey || evt.ctrlKey,
                    out LogScrollIntent intent
                )
            )
            {
                return false;
            }

            Scroller scroller = _logScrollView?.verticalScroller;
            if (scroller == null)
            {
                return false;
            }

            /*
                The extent the log actually shows, which is the page. Neither
                Scroller nor ScrollView exposes a page size on Unity 2021.3,
                the oldest editor this package supports, so it is read from the
                content viewport's laid-out rectangle - a Rect, whose height is
                a float on every supported version. A zero height is a log
                that has not been laid out, where there is nothing to page
                through and the clamp leaves the view where it is.
             */
            float target = LogScrollKeys.Target(
                intent,
                scroller.value,
                scroller.highValue,
                _logScrollView.contentViewport.layout.height
            );

            scroller.value = target;

            /*
                The developer's scroll, said so. The follower infers one from a
                position below the pin it already holds, but running a command
                calls `Attach`, which clears that pin, and the pin lands a
                frame later - so a page taken in that window would have
                nothing to be below and the very next pass would read it as
                output and snap the view back to the end. `Detach` is what
                closes that window, and it decides for itself whether the key
                actually left the view away from its end: a scroll that lands
                on the end is a developer paged down to the bottom, and
                recording a detach there would freeze the view a line short of
                an end that is still growing.
             */
            _logTail.Detach(scroller.value, scroller.highValue);
            _needsScrollToEnd = false;
            return true;
        }

        /*
            The same routing for a key that steps a search, and the same two
            answers: a key with no search set is not the log's to answer and is
            left alone, and a key the log answers is consumed so it does not
            also reach the field it arrived through.
         */
        private bool TryStepLogFilter(KeyDownEvent evt)
        {
            bool forward = LogFindKeys.IsStepForward(evt.keyCode, evt.shiftKey);
            if (!forward && !LogFindKeys.IsStepBackward(evt.keyCode, evt.shiftKey))
            {
                return false;
            }

            if (!_logFilter.IsActive)
            {
                return false;
            }

            StepLogFilter(forward);
            return true;
        }

        private void ScrollToEnd()
        {
            Scroller scroller = _logScrollView?.verticalScroller;
            float highValue = scroller?.highValue ?? 0f;
            if (highValue <= 0f)
            {
                return;
            }

            scroller.value = highValue;
            /*
                Read the value back rather than assuming the write landed on
                the high value: the scroller clamps to the extent it holds
                now, and that clamped number is what the next pass compares
                against to tell a developer's scroll from the layout pass that
                grows the content after this pin. The scroller may have
                reassigned it, so the read is not a repeated read of one
                source.
             */
            _logTail.Pin(scroller.value);
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

                _renderedHintCandidates.Clear();
                _previousLastCompletionIndex = null;
                return;
            }

            int bufferLength = _lastCompletionBuffer.Count;
            if (_lastKnownHintsClickable != makeHintsClickable)
            {
                _autoCompleteContainer.Clear();
                _renderedHintCandidates.Clear();
                _lastKnownHintsClickable = makeHintsClickable;
            }

            int currentChildCount = _autoCompleteContainer.childCount;

            /*
                The bar is a view of the buffer, so a pass compares the rows
                it drew against the buffer instead of inferring a change from
                a child count and a selection index. In Always mode one more
                keystroke swaps the candidates without changing their count,
                and the bar then showed the previous keystroke's text.
             */
            bool contentsChanged =
                currentChildCount != bufferLength || !RenderedCandidatesMatch(bufferLength);
            bool dirty = contentsChanged || _lastCompletionIndex != _previousLastCompletionIndex;
            if (contentsChanged)
            {
                if (currentChildCount < bufferLength)
                {
                    for (int i = currentChildCount; i < bufferLength; ++i)
                    {
                        string hint = _lastCompletionBuffer[i];
                        VisualElement hintElement;

                        if (makeHintsClickable)
                        {
                            Button hintButton = new();
                            hintButton.clicked += () =>
                                ApplyHint(_autoCompleteContainer.IndexOf(hintButton));
                            hintButton.text = LogTextSanitizer.Sanitize(hint);
                            hintElement = hintButton;
                        }
                        else
                        {
                            Label hintText = new(LogTextSanitizer.Sanitize(hint));
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
                    int rowCount = hintCount < bufferLength ? hintCount : bufferLength;
                    for (int i = 0; i < rowCount; ++i)
                    {
                        VisualElement hintElement = _autoCompleteContainer[i];
                        string hintText = LogTextSanitizer.Sanitize(_lastCompletionBuffer[i]);
                        switch (hintElement)
                        {
                            case Button button:
                                button.text = hintText;
                                break;
                            case Label label:
                                label.text = hintText;
                                break;
                            case TextField textField:
                                textField.value = hintText;
                                break;
                        }

                        bool isSelected = i == _lastCompletionIndex;

                        hintElement.EnableInClassList("autocomplete-item-selected", isSelected);
                        hintElement.EnableInClassList("autocomplete-item", !isSelected);
                    }

                    /*
                        The mirror is written with the rows it describes, so a
                        row loop that stops early leaves it short and the next
                        pass rebuilds. It runs after UpdateAutoCompleteView,
                        the only writer of the buffer, so the order it reads
                        is the order the rows show.
                     */
                    if (rowCount == bufferLength)
                    {
                        _renderedHintCandidates.Clear();
                        for (int i = 0; i < bufferLength; ++i)
                        {
                            _renderedHintCandidates.Add(_lastCompletionBuffer[i]);
                        }
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

        private bool RenderedCandidatesMatch(int bufferLength)
        {
            if (_renderedHintCandidates.Count != bufferLength)
            {
                return false;
            }

            for (int i = 0; i < bufferLength; ++i)
            {
                if (
                    !string.Equals(
                        _renderedHintCandidates[i],
                        _lastCompletionBuffer[i],
                        StringComparison.Ordinal
                    )
                )
                {
                    return false;
                }
            }

            return true;
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
