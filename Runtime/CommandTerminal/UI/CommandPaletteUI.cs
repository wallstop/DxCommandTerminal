namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using Input;
    using Themes;
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>
    ///     Runtime quick-launch bar: a compact command search surface that
    ///     opens on a configurable hotkey (default Ctrl+Space), ranks command
    ///     names by exact/prefix/fuzzy-subsequence match, and runs the selected
    ///     command through the shared <see cref="Terminal.Shell"/>. Independent
    ///     of <see cref="TerminalUI"/>: opening one surface closes the other.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CommandPaletteUI : MonoBehaviour
    {
        private const string PaletteRootName = "PaletteRoot";
        private const string PalettePanelName = "PalettePanel";
        private const string PaletteResultsName = "PaletteResults";
        private const string PaletteInputName = "PaletteInput";
        private const string PaletteDividerName = "PaletteDivider";
        private const string PaletteFeedbackName = "PaletteFeedback";
        private const string PaletteFooterName = "PaletteFooter";
        private const string RowName = "PaletteRow";
        private const string RowNameLabel = "PaletteRowName";
        private const string RowHelpName = "PaletteRowHelp";
        private const string SelectedRowClass = "palette-row-selected";
        private const string FooterText = "↑↓ select · Enter run · Tab apply · Esc close";

        public event Action Opened;

        public event Action Closed;

        public static CommandPaletteUI Instance { get; private set; }

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
        internal TerminalThemePack _themePack;

        internal VisualElement _paletteRoot;
        internal TextField _input;
        internal ScrollView _results;
        internal VisualElement _divider;
        internal Label _feedback;
        internal readonly List<string> _matchNames = new();

        [SerializeField]
        private Font _font;
        private VisualElement _panel;
        private readonly List<VisualElement> _rows = new();
        private readonly List<Label> _rowNameLabels = new();
        private readonly List<Label> _rowHelpLabels = new();
        private readonly List<string> _sourceNames = new();
        private int? _selectionIndex;
        private bool _isOpen;
        private bool _built;
        private VisualElement _previousFocus;

        public static void CloseActive()
        {
            if (Instance != null)
            {
                Instance.Close();
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

            Attach();
            CloseTerminalSurface();
            CapturePreviousFocus();
            _input.SetValueWithoutNotify(string.Empty);
            ClearFeedback();
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
        ///     Re-runs the palette search for <paramref name="query"/>, rebuilding
        ///     the result rows and resetting the selection to the first match.
        ///     A blank query clears the results, leaving only the search bar.
        /// </summary>
        public void SetQuery(string query)
        {
            if (!_isOpen)
            {
                return;
            }

            string effectiveQuery = query ?? string.Empty;
            if (string.IsNullOrWhiteSpace(effectiveQuery))
            {
                _matchNames.Clear();
            }
            else
            {
                RefreshSource();
                CommandPaletteSearch.Filter(effectiveQuery, _sourceNames, _matchNames);
            }

            _selectionIndex = _matchNames.Count == 0 ? (int?)null : 0;
            RefreshRows();
            UpdateSelectionVisual();
            UpdateResultsVisibility();
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
            DisplaySelected();
            return true;
        }

        /// <summary>
        ///     Applies the selected command name to the input (Tab behavior).
        /// </summary>
        public bool ApplySelected()
        {
            if (!IsOpen || !TryGetSelected(out string selected))
            {
                return false;
            }

            ClearFeedback();
            SetInputValue(selected);
            SetQuery(selected);
            FocusInput();
            return true;
        }

        /// <summary>
        ///     Runs the current input through the command shell. Blank input
        ///     falls back to the selected row when one exists (for example
        ///     after a direct <see cref="SetQuery"/> call). On success the
        ///     palette closes by default; failures keep it open with visible
        ///     feedback.
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
            if (success && closeOnSuccessfulExecution)
            {
                Close();
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

        private void OnEnable()
        {
            /*
                First enabled component owns the static instance so two
                palettes in a scene cannot both react to CloseActive.
             */
            if (Instance == null)
            {
                Instance = this;
            }
        }

        private void OnDisable()
        {
            Close();
            if (_built && _uiDocument != null)
            {
                _uiDocument.rootVisualElement?.Clear();
            }

            _paletteRoot = null;
            _panel = null;
            _results = null;
            _divider = null;
            _input = null;
            _feedback = null;
            _rows.Clear();
            _rowNameLabels.Clear();
            _rowHelpLabels.Clear();
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
            _panel.Add(_results);

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
            Font font = _font != null ? _font : TerminalUI.Instance?.CurrentFont;
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
            for (int index = 0; index < _matchNames.Count; ++index)
            {
                VisualElement row = _rows[index];
                row.style.display = DisplayStyle.Flex;
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

            for (int index = _matchNames.Count; index < _rows.Count; ++index)
            {
                _rows[index].style.display = DisplayStyle.None;
            }
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
            if (index < 0)
            {
                return;
            }

            _selectionIndex = index;
            UpdateSelectionVisual();
            /*
                Sync the input to the clicked row before submitting: after an
                arrow auto-load the input still holds the previously
                highlighted name, and Submit reads the input value.
             */
            DisplaySelected();
            Submit();
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
            ClearFeedback();
            SetQuery(evt.newValue);
        }

        private void UpdateSelectionVisual()
        {
            for (int index = 0; index < _rows.Count; ++index)
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
            _input.SetValueWithoutNotify(value);
            _input.cursorIndex = _input.selectIndex = value.Length;
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
            }

            return success;
        }

        private void ShowFeedback(string message)
        {
            _feedback.text = message;
            _feedback.style.display = DisplayStyle.Flex;
        }

        private void ClearFeedback()
        {
            _feedback.text = string.Empty;
            _feedback.style.display = DisplayStyle.None;
        }
    }
}
