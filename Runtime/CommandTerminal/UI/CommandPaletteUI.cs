namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using Helper;
    using Input;
    using Themes;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    ///     Runtime quick-launch bar: a compact command search surface that
    ///     opens on a configurable hotkey (default Ctrl+Space), ranks command
    ///     names by exact/prefix/fuzzy-subsequence match, runs the selected
    ///     command through the shared <see cref="Terminal.Shell"/>, and chains
    ///     argument completion: once the input resolves to an eligible
    ///     command with an active argument, rows show the shell's dynamic
    ///     completion candidates and Tab applies the selected one to the
    ///     active token. Independent of <see cref="TerminalUI"/>: opening one
    ///     surface closes the other.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CommandPaletteUI : MonoBehaviour
    {
        private const string PaletteRootName = "PaletteRoot";
        private const string PalettePanelName = "PalettePanel";
        private const string PaletteResultsName = "PaletteResults";
        private const string PaletteInputName = "PaletteInput";
        private const string PaletteDividerName = "PaletteDivider";
        private const string PaletteOutputName = "PaletteOutput";
        private const string PaletteFeedbackName = "PaletteFeedback";
        private const string PaletteFooterName = "PaletteFooter";
        private const string RowName = "PaletteRow";
        private const string RowNameLabel = "PaletteRowName";
        private const string RowHelpName = "PaletteRowHelp";
        private const string SelectedRowClass = "palette-row-selected";
        private const string FooterText = "↑↓ select · Enter run · Tab apply · Esc close";
        private const int CaretStickPasses = 2;

        public event Action Opened;

        public event Action Closed;

        public static CommandPaletteUI Instance { get; private set; }

        /*
            Per-pass caret logging for #74-class investigations: a flake that
            only reproduces under full-suite session sequences. Test/editor
            diagnostics only; never enabled in normal runs.
         */
        internal static bool _logCaretPasses;

        private static readonly List<CommandPaletteUI> _livePalettes = new();

        public bool IsOpen => _isOpen;

        [Header("Window")]
        [Tooltip("Width of the palette panel in pixels, clamped to the viewport")]
        [Min(320f)]
        public float width = 640f;

        [Tooltip("Vertical position of the panel top, as a fraction of the viewport height")]
        [Range(0f, 1f)]
        public float verticalPosition = 0.3f;

        [Tooltip("Maximum visible result rows; additional results scroll")]
        [Range(1, 32)]
        public int maxVisibleRows = 6;

        [Tooltip("Height of one result row in pixels")]
        [Min(16f)]
        public float rowHeight = 30f;

        [Tooltip("Close the palette when a command runs successfully")]
        public bool closeOnSuccessfulExecution = true;

        [Header("Input")]
        public InputMode inputMode =
#if ENABLE_INPUT_SYSTEM
        InputMode.NewInputSystem;
#else
        InputMode.LegacyInputSystem;
#endif

        [Tooltip("Hotkey that opens/closes the palette (supports ctrl+ and shift+ modifiers)")]
        [SerializeField]
        public string toggleHotkey = "ctrl+space";

        [SerializeField]
        internal UIDocument _uiDocument;

        [SerializeField]
        internal TerminalSettings _settings;

        [SerializeField]
        internal TerminalThemePack _themePack;

        internal VisualElement _paletteRoot;
        internal TextField _input;
        internal ScrollView _results;
        internal VisualElement _divider;
        internal Label _output;
        internal Label _feedback;
        internal readonly List<string> _matchNames = new();
        internal readonly List<CommandCompletion> _completions = new();
        internal readonly List<VisualElement> _rows = new();
        internal int? _pendingCaretIndex;
        internal int _caretStickPasses;

        [SerializeField]
        private Font _font;
        private VisualElement _panel;
        private readonly List<Label> _rowNameLabels = new();
        private readonly List<Label> _rowHelpLabels = new();
        private readonly List<string> _sourceNames = new();
        private readonly List<CommandCompletion> _completionsBuffer = new();
        private readonly List<CommandToken> _tokenBuffer = new();

        /*
            Only the scalar geometry of the last request (replacement range,
            quoted flag) is read after the refresh, and always before the
            next shell call; PrecedingArguments is valid only inside the
            provider callback and must never be consumed from here.
         */
        private CommandCompletionContext _completionContext;
        private bool _completionMode;
        private int? _selectionIndex;
        private bool _isOpen;
        private bool _built;
        private bool _lastRunProducedOutput;
        private VisualElement _previousFocus;
        private readonly List<string> _outputLines = new();

        public static void CloseActive()
        {
            if (Instance != null)
            {
                Instance.Close();
            }
        }

        /// <summary>
        ///     Reports whether any live palette is open on <paramref name="document"/>.
        ///     Terminals gate their per-frame document writes on this so an open
        ///     palette owns the shared surface, independent of which component
        ///     claimed <see cref="Instance"/>.
        /// </summary>
        public static bool IsOpenOn(UIDocument document)
        {
            if (document == null)
            {
                return false;
            }

            int livePaletteCount = _livePalettes.Count;
            for (int index = 0; index < livePaletteCount; ++index)
            {
                CommandPaletteUI palette = _livePalettes[index];
                if (palette._uiDocument == document && palette.IsOpen)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Closes every open palette on <paramref name="document"/> -
        ///     including palettes that never claimed <see cref="Instance"/> -
        ///     so a terminal state change reclaims the shared surface even in
        ///     multi-palette setups.
        /// </summary>
        public static void CloseAllOn(UIDocument document)
        {
            if (document == null)
            {
                return;
            }

            for (int index = _livePalettes.Count - 1; 0 <= index; --index)
            {
                CommandPaletteUI palette = _livePalettes[index];
                if (palette._uiDocument == document && palette.IsOpen)
                {
                    palette.Close();
                }
            }
        }

        public void Open()
        {
            if (_isOpen)
            {
                return;
            }

            EnsureBuilt();
            if (!_built)
            {
                return;
            }

            /*
                The build-time font application can predate the terminal's
                first UI build, where the pack font resolves; a fresh read
                per open picks the resolved font up without a rebuild.
             */
            ApplyFont();
            Attach();
            CloseTerminalSurface();
            CapturePreviousFocus();
            /*
                The terminal clamps the shared document root to its own window
                height while it owns the surface; restore automatic sizing so
                the panel's percent position measures the full viewport.
             */
            _uiDocument.rootVisualElement.style.height = new StyleLength(StyleKeyword.Auto);
            _input.SetValueWithoutNotify(string.Empty);
            _paletteRoot.style.display = DisplayStyle.Flex;
            _isOpen = true;
            SetQuery(string.Empty);
            FocusInput();
            Opened?.Invoke();
        }

        public void Close()
        {
            if (!_isOpen)
            {
                return;
            }

            _isOpen = false;
            QueueCaret(null);
            RestorePreviousFocus();
            /*
                Detach instead of hiding: a display:none subtree keeps panel
                focus, so the hidden input would keep consuming Enter/Escape/
                Tab/arrows in games without other focusable UI. Detaching
                releases focus and no key press can reach the closed palette.
             */
            _paletteRoot?.RemoveFromHierarchy();
            Closed?.Invoke();
        }

        public void Toggle()
        {
            if (_isOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>
        ///     Re-runs the palette search for <paramref name="query"/>,
        ///     rebuilding the result rows and resetting the selection to the
        ///     first match. Argument completion first: when the query resolves
        ///     to an eligible command with an active argument stage, rows show
        ///     the shell's completion candidates. Otherwise the query filters
        ///     command names. A blank query clears the results, leaving only
        ///     the search bar; any displayed output or feedback from a
        ///     previous run is cleared. Completion reads the caret at the end
        ///     of the query.
        /// </summary>
        public void SetQuery(string query)
        {
            RefreshQuery(query, query != null ? query.Length : 0);
        }

        public bool MoveSelection(int direction)
        {
            if (!_isOpen || _matchNames.Count == 0)
            {
                return false;
            }

            int current = _selectionIndex ?? 0;
            int target = Mathf.Clamp(current + direction, 0, _matchNames.Count - 1);
            if (target == current)
            {
                return false;
            }

            _selectionIndex = target;
            UpdateSelectionVisual();
            /*
                Command rows auto-load the highlighted name (launcher style);
                completion rows must not: loading an insertion text would
                corrupt the command text the user is assembling.
             */
            if (!_completionMode)
            {
                DisplaySelected();
            }

            return true;
        }

        /// <summary>
        ///     Applies the selected row to the input (Tab behavior). Command
        ///     rows load the command name; argument completion rows insert the
        ///     candidate at the active token's replacement range and re-run
        ///     the search on the updated text.
        /// </summary>
        public bool ApplySelected()
        {
            if (!IsOpen || !TryGetSelected(out string selected))
            {
                return false;
            }

            if (_completionMode)
            {
                int index = _selectionIndex ?? -1;
                if (index < 0 || _completions.Count <= index)
                {
                    return false;
                }

                ApplyCompletion(_completions[index]);
                return true;
            }

            SetInputValue(selected);
            SetQuery(selected);
            FocusInput();
            return true;
        }

        /// <summary>
        ///     Runs the current input through the command shell. Blank input
        ///     falls back to the selected row when one exists (for example
        ///     after a direct <see cref="SetQuery"/> call). On success the
        ///     palette closes by default unless the command printed output —
        ///     the output is shown, the input is cleared, and the palette
        ///     stays open so commands like <c>list-fonts</c> stay readable.
        ///     Failures keep it open with visible feedback.
        /// </summary>
        public bool Submit()
        {
            if (!_isOpen)
            {
                return false;
            }

            string commandText = _input.value;
            if (string.IsNullOrWhiteSpace(commandText) && TryGetSelected(out string selected))
            {
                commandText = selected;
            }

            if (string.IsNullOrWhiteSpace(commandText))
            {
                return false;
            }

            bool success = RunCommandText(commandText.Trim());
            bool close = success && closeOnSuccessfulExecution && !_lastRunProducedOutput;
            if (close)
            {
                Close();
                return success;
            }

            /*
                Output-producing commands keep the palette open, but the run is
                finished: clear the executed command so the output reads on its
                own and Enter does not re-run it.
             */
            if (_lastRunProducedOutput)
            {
                ClearInput();
            }

            return success;
        }

        public bool TryGetSelected(out string selectedName)
        {
            if (_selectionIndex.HasValue)
            {
                int index = _selectionIndex.Value;
                if (index < _matchNames.Count)
                {
                    selectedName = _matchNames[index];
                    return true;
                }
            }

            selectedName = null;
            return false;
        }

        /*
            Row activation (pointer click) resolves to an index, then drives
            the same selection path as keyboard navigation. Split from the
            ClickEvent callback because Unity's dispatcher drops synthetic
            pointer events, leaving only real input injection for parity
            testing (see TerminalUI pointer-parity note).
         */
        internal void RowActivated(int index)
        {
            _selectionIndex = index;
            UpdateSelectionVisual();
            /*
                Command rows sync the input to the clicked name before
                submitting (after an arrow auto-load the input still holds the
                previously highlighted name). Completion rows apply instead of
                submitting: the command may need more arguments.
             */
            if (_completionMode)
            {
                ApplySelected();
                return;
            }

            DisplaySelected();
            Submit();
        }

        internal void ApplyPendingCaret()
        {
            if (_pendingCaretIndex is not int index || _input == null)
            {
                return;
            }

            if (_input.value.Length < index)
            {
                /*
                    The queued position targets input the field does not hold
                    yet; the write applies once the value sync lands.
                 */
                return;
            }

            if (_logCaretPasses)
            {
                VisualElement focused =
                    _input.panel?.focusController?.focusedElement as VisualElement;
                string focusOwner =
                    focused == null ? "none"
                    : focused == _input || _input.Contains(focused) ? "input"
                    : focused.name;
                Debug.Log(
                    $"[CommandPaletteUI] caret pass frame={Time.frameCount} pending={index}"
                        + $" cursor={_input.cursorIndex} select={_input.selectIndex}"
                        + $" valueLength={_input.value.Length} focus='{focusOwner}'",
                    this
                );
            }

            if (_input.cursorIndex == index && _input.selectIndex == index)
            {
                /*
                    The position held across a panel pass; once it has held
                    for two, later movement is the user's or a settled reset.
                 */
                ++_caretStickPasses;
            }
            else
            {
                /*
                    A panel-driven re-clamp moved the caret away after an
                    earlier pass (issue #74); re-assert and wait for it to
                    hold again.
                 */
                _caretStickPasses = 0;
#if UNITY_2022_1_OR_NEWER
                _input.cursorIndex = index;
                _input.selectIndex = index;
#else
                /*
                    2021.3 exposes the caret getters only; the engine owns
                    placement, so the queue retires instead of re-asserting.
                 */
                QueueCaret(null);
#endif
            }

            if (CaretStickPasses <= _caretStickPasses)
            {
                QueueCaret(null);
            }
        }

        private void RefreshQuery(string query, int caretIndex)
        {
            if (!_isOpen)
            {
                return;
            }

            string effectiveQuery = query ?? string.Empty;
            if (string.IsNullOrWhiteSpace(effectiveQuery))
            {
                CollapseResults();
            }
            else if (!TryRefreshCompletions(effectiveQuery, caretIndex))
            {
                RefreshSource();
                CommandPaletteSearch.Filter(effectiveQuery, _sourceNames, _matchNames);
                _selectionIndex = _matchNames.Count == 0 ? (int?)null : 0;
                RefreshRows();
                UpdateSelectionVisual();
                UpdateResultsVisibility();
            }

            ClearFeedback();
        }

        private void Awake()
        {
            /*
                Shared settings asset wins over the serialized component
                value (issue #72 option B): the palette hotkey follows the
                asset when one is assigned. Same wake semantics as
                TerminalUI.
             */
            if (_settings != null)
            {
                toggleHotkey = _settings.paletteToggleHotkey;
            }
        }

        private void OnEnable()
        {
            EnsureBackendSession();

            /*
                First enabled component owns the static instance so two
                palettes in a scene cannot both react to CloseActive.
             */
            if (Instance == null)
            {
                Instance = this;
            }

            _livePalettes.Add(this);
        }

        /*
            A palette owns no session configuration and never reconfigures a
            session another component created: it only bootstraps the shared
            session when nothing has, so logging and commands work without
            any terminal visual tree. Registration stays deferred to first
            use, matching the terminal's enable-frame cost. A later terminal
            still applies its own configuration as the owner.
         */
        private void EnsureBackendSession()
        {
            if (TerminalSession.Current.Buffer != null)
            {
                return;
            }

            TerminalSession.Config config =
                _settings != null
                    ? new TerminalSession.Config(
                        logBufferSize: _settings.logBufferSize,
                        historyBufferSize: _settings.historyBufferSize,
                        ignoredLogTypes: _settings.ignoredLogTypes,
                        disabledCommands: _settings.disabledCommands,
                        ignoreDefaultCommands: _settings.ignoreDefaultCommands,
                        stackTraceMode: _settings.stackTraceMode
                    )
                    : TerminalSession.Config.Default;
            TerminalSession.Current.Apply(config, force: false);
        }

        private void OnDisable()
        {
            _livePalettes.Remove(this);
            Close();
            if (_built && _uiDocument != null)
            {
                _uiDocument.rootVisualElement?.Clear();
            }

            _pendingCaretIndex = null;
            _caretStickPasses = 0;
            _paletteRoot = null;
            _panel = null;
            _results = null;
            _divider = null;
            _output = null;
            _input = null;
            _feedback = null;
            _rows.Clear();
            _rowNameLabels.Clear();
            _rowHelpLabels.Clear();
            ClearCompletionState();
            _built = false;
            _previousFocus = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (InputHelpers.IsKeyPressed(toggleHotkey, inputMode))
            {
                Toggle();
            }
        }

        private void LateUpdate()
        {
            /*
                After the panel's internal update: a pending caret write lands
                once the text element's own reset has already run.
             */
            ApplyPendingCaret();
        }

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            if (_uiDocument == null)
            {
                /*
                    Same recovery as TerminalUI: scripted adds leave the
                    serialized field null while a UIDocument component may
                    exist on this GameObject (typically shared with the
                    terminal's document).
                 */
                TryGetComponent(out _uiDocument);
            }

            if (_uiDocument == null)
            {
                Debug.LogError("No UIDocument assigned, cannot open the command palette.", this);
                return;
            }

            VisualElement uiRoot = _uiDocument.rootVisualElement;
            if (uiRoot == null)
            {
                Debug.LogError("No UI root element, cannot open the command palette.", this);
                return;
            }

            _paletteRoot = new VisualElement
            {
                name = PaletteRootName,
                pickingMode = PickingMode.Position,
            };
            _paletteRoot.AddToClassList("palette-root");
            _paletteRoot.style.position = Position.Absolute;
            _paletteRoot.style.left = 0;
            _paletteRoot.style.right = 0;
            _paletteRoot.style.top = 0;
            _paletteRoot.style.bottom = 0;
            _paletteRoot.style.alignItems = Align.Center;
            ApplyThemePack();
            ApplyFont();

            _panel = new VisualElement { name = PalettePanelName };
            _panel.AddToClassList("palette-panel");
            /*
                Geometry stays inline because width and position follow
                serialized fields; every other value lives in BaseStyles.uss
                so theme tokens drive the look.
             */
            _panel.style.position = Position.Absolute;
            _panel.style.top = Length.Percent(Mathf.Clamp(verticalPosition, 0f, 1f) * 100f);
            _panel.style.width = width;
            _panel.style.maxWidth = Length.Percent(100f);
            _paletteRoot.Add(_panel);

            _input = new TextField { name = PaletteInputName };
            _input.AddToClassList("palette-input");
            _panel.Add(_input);
            _input.RegisterCallback<ChangeEvent<string>>(OnInputChanged);

            _divider = new VisualElement { name = PaletteDividerName };
            _divider.AddToClassList("palette-divider");
            _divider.style.display = DisplayStyle.None;
            _panel.Add(_divider);

            _results = new ScrollView(ScrollViewMode.Vertical) { name = PaletteResultsName };
            _results.AddToClassList("palette-results");
            _results.style.maxHeight = maxVisibleRows * rowHeight;
            _results.style.display = DisplayStyle.None;
            Scroller verticalScroller = _results.verticalScroller;
            if (verticalScroller != null)
            {
                /*
                    UITK moves panel focus to the child slider on click, and
                    the slider is what steals caret from the search input;
                    both the scroller and its slider opt out so scrolling
                    stays wheel- and arrow-driven.
                 */
                verticalScroller.focusable = false;
                if (verticalScroller.slider != null)
                {
                    verticalScroller.slider.focusable = false;
                }
            }

            _panel.Add(_results);

            _output = new Label { name = PaletteOutputName };
            _output.AddToClassList("palette-output");
            _output.style.display = DisplayStyle.None;
            _panel.Add(_output);

            _feedback = new Label { name = PaletteFeedbackName };
            _feedback.AddToClassList("palette-feedback");
            _feedback.style.display = DisplayStyle.None;
            _panel.Add(_feedback);

            Label footer = new(FooterText) { name = PaletteFooterName };
            footer.AddToClassList("palette-footer");
            _panel.Add(footer);

            _paletteRoot.RegisterCallback<KeyDownEvent>(HandleKeyDown, TrickleDown.TrickleDown);
            _built = true;
        }

        private void Attach()
        {
            if (_paletteRoot == null || _paletteRoot.panel != null)
            {
                return;
            }

            VisualElement uiRoot = _uiDocument.rootVisualElement;
            if (uiRoot == null)
            {
                Debug.LogError("No UI root element, cannot open the command palette.", this);
                return;
            }

            uiRoot.Add(_paletteRoot);
        }

        private void ApplyThemePack()
        {
            TerminalThemePack pack = _themePack;
            if (pack == null)
            {
                return;
            }

            List<StyleSheet> themes = pack._themes;
            if (themes == null)
            {
                return;
            }

            foreach (StyleSheet styleSheet in themes)
            {
                if (styleSheet == null)
                {
                    continue;
                }

                _paletteRoot.styleSheets.Add(styleSheet);
            }

            TerminalUI terminal = TerminalUI.Instance;
            if (terminal != null && !string.IsNullOrWhiteSpace(terminal.CurrentTheme))
            {
                _paletteRoot.AddToClassList(terminal.CurrentTheme);
            }
        }

        private void ApplyFont()
        {
            Font font = _font;
            if (font == null)
            {
                TerminalUI terminal = TerminalUI.Instance;
                font = terminal != null ? terminal.CurrentFont : null;
            }

            if (font == null)
            {
                return;
            }

            _paletteRoot.style.unityFontDefinition = new StyleFontDefinition(font);
        }

        private void RefreshSource()
        {
            _sourceNames.Clear();
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return;
            }

            CommandExecutionContext context = CommandExecutionContext.Current;
            if (shell.Commands is SortedDictionary<string, CommandInfo> sorted)
            {
                foreach (KeyValuePair<string, CommandInfo> entry in sorted)
                {
                    AppendEligibleCommand(entry, context);
                }
            }
            else
            {
                foreach (KeyValuePair<string, CommandInfo> entry in shell.Commands)
                {
                    AppendEligibleCommand(entry, context);
                }
            }
        }

        private void AppendEligibleCommand(
            KeyValuePair<string, CommandInfo> entry,
            CommandExecutionContext context
        )
        {
            if (!context.IsEligibleFor(entry.Value.executionContexts))
            {
                return;
            }

            _sourceNames.Add(entry.Key);
        }

        private void RefreshRows()
        {
            EnsureRowCapacity(_matchNames.Count);
            CommandShell shell = Terminal.Shell;
            int matchCount = _matchNames.Count;
            for (int index = 0; index < matchCount; ++index)
            {
                VisualElement row = _rows[index];
                row.style.display = DisplayStyle.Flex;
                if (_completionMode)
                {
                    CommandCompletion completion = _completions[index];
                    _rowNameLabels[index].text = completion.EffectiveDisplayLabel;
                    _rowHelpLabels[index].text = completion.Description ?? string.Empty;
                    continue;
                }

                _rowNameLabels[index].text = _matchNames[index];
                string help = null;
                if (
                    shell != null
                    && shell.Commands.TryGetValue(_matchNames[index], out CommandInfo info)
                )
                {
                    help = info.help;
                }

                _rowHelpLabels[index].text = string.IsNullOrWhiteSpace(help) ? string.Empty : help;
            }

            int excessRowStart = _matchNames.Count;
            int rowCount = _rows.Count;
            for (int index = excessRowStart; index < rowCount; ++index)
            {
                _rows[index].style.display = DisplayStyle.None;
            }
        }

        /*
            Argument completion. When the shell's completion provider answers
            the query (the input resolves to an eligible command and the caret
            sits in an argument stage), the rows show its candidates and Tab
            applies the selected one to the active token. Returns false when
            the query stays a command-name search: blank input, no resolved
            command, no active argument stage, or an ineligible command.
         */
        private bool TryRefreshCompletions(string query, int caret)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                return false;
            }

            _completionsBuffer.Clear();
            bool hasProvider = shell.TryComplete(
                CommandExecutionContext.Current,
                query,
                NormalizeCaret(caret, query),
                _completionsBuffer,
                out CommandCompletionContext context
            );
            if (!hasProvider || !IsEligibleCompletionTarget(context, shell))
            {
                ClearCompletionState();
                return false;
            }

            _completionContext = context;
            _completions.Clear();
            _completions.AddRange(_completionsBuffer);
            _completionMode = true;
            _matchNames.Clear();
            foreach (CommandCompletion completion in _completions)
            {
                _matchNames.Add(completion.EffectiveDisplayLabel);
            }

            _selectionIndex = _matchNames.Count == 0 ? (int?)null : 0;
            RefreshRows();
            UpdateSelectionVisual();
            UpdateResultsVisibility();
            return true;
        }

        /*
            The shell answers providers without checking eligibility, so the
            resolved command is verified here: an Editor-only command must not
            offer its argument candidates from a player or Edit Mode context.
         */
        private bool IsEligibleCompletionTarget(
            CommandCompletionContext context,
            CommandShell shell
        )
        {
            _tokenBuffer.Clear();
            CommandTokenizer.Tokenize(context.Input, _tokenBuffer);
            if (_tokenBuffer.Count == 0)
            {
                return false;
            }

            return shell.Commands.TryGetValue(_tokenBuffer[0].Contents, out CommandInfo info)
                && context.ExecutionContext.IsEligibleFor(info.executionContexts);
        }

        private void ApplyCompletion(CommandCompletion completion)
        {
            string input = _input.value ?? string.Empty;
            int replacementStart;
            int replacementLength;
            if (completion.Replacement is CommandCompletionReplacement replacementOverride)
            {
                replacementStart = replacementOverride.Start;
                replacementLength = replacementOverride.Length;
            }
            else
            {
                replacementStart = _completionContext.ReplacementStart;
                replacementLength = _completionContext.ReplacementLength;
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
                    _completionContext.IsQuoted,
                    out string insertion,
                    out replacementStart,
                    out replacementLength,
                    wholeToken: replacementStart == _completionContext.ReplacementStart
                        && replacementLength == _completionContext.ReplacementLength
                )
            )
            {
                return;
            }
            string newInput = input
                .Remove(replacementStart, replacementLength)
                .Insert(replacementStart, insertion);
            /*
                The value change rides SetValueWithoutNotify so exactly one
                refresh runs, through SetQuery below; the caret lands after
                the inserted token like the terminal's token completion.
             */
            _input.SetValueWithoutNotify(newInput);
            int caretIndex = replacementStart + insertion.Length;
            QueueCaret(caretIndex);
            int completionCaret = caretIndex;
            if (
                1 < insertion.Length
                && CommandArg.Quotes.Contains(insertion[0])
                && insertion[insertion.Length - 1] == insertion[0]
            )
            {
                --completionCaret;
            }
            RefreshQuery(newInput, completionCaret);
            FocusInput();
        }

        private void ClearCompletionState()
        {
            _completionMode = false;
            _completionContext = default;
            _completions.Clear();
        }

        private int NormalizeCaret(int caret, string query)
        {
            return caret < 0 || query.Length < caret ? query.Length : caret;
        }

        private void EnsureRowCapacity(int count)
        {
            while (_rows.Count < count)
            {
                VisualElement row = new() { name = RowName };
                row.AddToClassList("palette-row");
                row.style.height = rowHeight;

                Label nameLabel = new() { name = RowNameLabel };
                nameLabel.AddToClassList("palette-row-name");
                row.Add(nameLabel);

                Label helpLabel = new() { name = RowHelpName };
                helpLabel.AddToClassList("palette-row-help");
                row.Add(helpLabel);

                row.RegisterCallback<ClickEvent>(OnRowClicked);
                _results.Add(row);
                _rows.Add(row);
                _rowNameLabels.Add(nameLabel);
                _rowHelpLabels.Add(helpLabel);
            }
        }

        private void OnRowClicked(ClickEvent evt)
        {
            if (evt.currentTarget is not VisualElement row)
            {
                return;
            }

            int index = _rows.IndexOf(row);
            if (0 <= index)
            {
                RowActivated(index);
            }
        }

        private void HandleKeyDown(KeyDownEvent evt)
        {
            if (!_isOpen)
            {
                return;
            }

            switch (evt.keyCode)
            {
                case KeyCode.DownArrow:
                    Consume(evt);
                    MoveSelection(1);
                    break;
                case KeyCode.UpArrow:
                    Consume(evt);
                    MoveSelection(-1);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    Consume(evt);
                    Submit();
                    break;
                case KeyCode.Escape:
                    Consume(evt);
                    Close();
                    break;
                case KeyCode.Tab:
                    Consume(evt);
                    ApplySelected();
                    break;
            }
        }

        private void OnInputChanged(ChangeEvent<string> evt)
        {
            /*
                A value change from the field is a user edit (programmatic
                writes ride SetValueWithoutNotify and fire no event); it owns
                the caret from here, so any queued caret write is cancelled
                before it can fight the user's typing.
             */
            QueueCaret(null);
            /*
                Programmatic value writes and end-of-line typing leave the
                caret at the new tail; the text field's own cursorIndex can
                lag its value at event time (it stays put on value writes).
                An append therefore completes at the new end, and anything
                else (mid-line edit) reads the live caret.
             */
            string previous = evt.previousValue ?? string.Empty;
            string updated = evt.newValue ?? string.Empty;
            int caret = updated.StartsWith(previous, StringComparison.Ordinal)
                ? updated.Length
                : _input.cursorIndex;
            RefreshQuery(updated, caret);
        }

        private void UpdateSelectionVisual()
        {
            int rowCount = _rows.Count;
            for (int index = 0; index < rowCount; ++index)
            {
                bool selected = _selectionIndex.HasValue && _selectionIndex.Value == index;
                _rows[index].EnableInClassList(SelectedRowClass, selected);
            }

            if (_selectionIndex.HasValue && _selectionIndex.Value < _rows.Count)
            {
                _results.ScrollTo(_rows[_selectionIndex.Value]);
            }
        }

        private void UpdateResultsVisibility()
        {
            bool visible = 0 < _matchNames.Count;
            _results.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _divider.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void DisplaySelected()
        {
            if (!TryGetSelected(out string selected))
            {
                return;
            }

            /*
                Launcher-style auto-load: the input shows the highlighted
                command name while the match list stays put. SetValueWithoutNotify
                fires no change event, so the typed query keeps driving the
                filter until the user edits the text again.
             */
            SetInputValue(selected);
        }

        private void SetInputValue(string value)
        {
            /*
                A value change makes the text element re-run its own caret
                reset after this call, and that reset can land after this
                frame's LateUpdate write, so the write is retried until it
                holds for two consecutive passes (ApplyPendingCaret).
             */
            _input.SetValueWithoutNotify(value);
            QueueCaret(value.Length);
        }

        private void QueueCaret(int? index)
        {
            _pendingCaretIndex = index;
            _caretStickPasses = 0;
        }

        private void FocusInput()
        {
            _input?.Focus();
        }

        /*
            A consumed key must not reach the focused text field or move panel
            focus. Unity 6 replaced PreventDefault with IgnoreEvent; older
            versions keep PreventDefault.
         */
        private void Consume(KeyDownEvent evt)
        {
            evt.StopPropagation();
#if UNITY_6000_0_OR_NEWER
            _paletteRoot.panel.focusController.IgnoreEvent(evt);
#else
            evt.PreventDefault();
#endif
        }

        private void CapturePreviousFocus()
        {
            IPanel panel = _paletteRoot.panel;
            _previousFocus = panel?.focusController?.focusedElement as VisualElement;
        }

        private void RestorePreviousFocus()
        {
            VisualElement focus = _previousFocus;
            _previousFocus = null;
            if (focus != null && focus.panel != null)
            {
                focus.Focus();
            }
        }

        private void CloseTerminalSurface()
        {
            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null || terminal.IsClosed)
            {
                return;
            }

            terminal.Close();
        }

        private bool RunCommandText(string commandText)
        {
            CommandShell shell = Terminal.Shell;
            if (shell == null)
            {
                ShowFeedback("No command shell is available.");
                return false;
            }

            CommandLog buffer = Terminal.Buffer;
            long versionBefore = buffer != null ? buffer.Version : 0;
            _lastRunProducedOutput = false;

            Terminal.Log(TerminalLogType.Input, commandText);
            bool success = shell.RunCommand(commandText);
            string firstError = null;
            while (shell.TryConsumeErrorMessage(out string error))
            {
                firstError ??= error;
                Terminal.Log(TerminalLogType.Error, $"Error: {error}");
            }

            if (firstError != null)
            {
                ShowFeedback($"Error: {firstError}");
                return success;
            }

            List<string> output = CollectOutput(buffer, versionBefore);
            if (0 < output.Count)
            {
                ShowOutput(output);
                return success;
            }

            return success;
        }

        /*
            Output-producing commands print through Terminal.Log into the
            shared buffer; the palette cannot redirect that, so it reads the
            entries added since the run started. The Input echo is skipped so
            only the command's own lines show.
         */
        private List<string> CollectOutput(CommandLog buffer, long versionBefore)
        {
            _outputLines.Clear();
            if (buffer == null)
            {
                return _outputLines;
            }

            int added = (int)(buffer.Version - versionBefore);
            if (added <= 0)
            {
                return _outputLines;
            }

            IReadOnlyList<LogItem> logs = buffer.Logs;
            int logCount = logs.Count;
            int first = Mathf.Max(0, logCount - added);
            for (int index = first; index < logCount; ++index)
            {
                LogItem log = logs[index];
                if (log.type == TerminalLogType.Input)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(log.message))
                {
                    _outputLines.Add(log.message);
                }
            }

            return _outputLines;
        }

        private void ShowOutput(List<string> lines)
        {
            const int MaxOutputLines = 8;
            using CachedStringBuilder.Scope builder = new(256);
            int shown = Mathf.Min(lines.Count, MaxOutputLines);
            for (int index = 0; index < shown; ++index)
            {
                if (0 < index)
                {
                    builder.Builder.Append('\n');
                }

                builder.Builder.Append(lines[index]);
            }

            int remaining = lines.Count - shown;
            if (0 < remaining)
            {
                builder.Builder.Append('\n');
                builder.Builder.Append("(+").Append(remaining).Append(" more lines)");
            }

            _feedback.style.display = DisplayStyle.None;
            _output.text = builder.Builder.ToString();
            _output.style.display = DisplayStyle.Flex;
            _lastRunProducedOutput = true;
        }

        /*
            Drops the result rows without touching the run diagnostics: the
            executed-command cleanup after an output run must keep the output
            visible, while a new blank query clears everything.
         */
        private void CollapseResults()
        {
            ClearCompletionState();
            _matchNames.Clear();
            _selectionIndex = null;
            RefreshRows();
            UpdateSelectionVisual();
            UpdateResultsVisibility();
        }

        /*
            Post-run cleanup for an output-producing command: the executed
            command leaves the bar, the stale result rows collapse, and the
            output stays below. Enter with the cleared bar is a no-op.
         */
        private void ClearInput()
        {
            SetInputValue(string.Empty);
            CollapseResults();
        }

        private void ShowFeedback(string message)
        {
            _output.style.display = DisplayStyle.None;
            _feedback.text = message;
            _feedback.style.display = DisplayStyle.Flex;
        }

        private void ClearFeedback()
        {
            _feedback.text = string.Empty;
            _feedback.style.display = DisplayStyle.None;
            _output.text = string.Empty;
            _output.style.display = DisplayStyle.None;
        }
    }
}
