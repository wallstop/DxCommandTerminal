namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed partial class TerminalUI
    {
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
            float verticalPadding = Mathf.Max(6f, _launcherMetrics.InsetPadding * 0.3f);
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
                ApplyLogDisplay(
                    DisplayStyle.Flex,
                    new StyleLength(_launcherMetrics.HistoryHeight),
                    new StyleLength(_launcherMetrics.HistoryHeight),
                    new StyleLength(0f),
                    0f,
                    1f,
                    new StyleLength(0f),
                    new StyleLength(0f)
                );
            }
            else
            {
                ApplyLogDisplay(
                    DisplayStyle.None,
                    new StyleLength(0f),
                    new StyleLength(0f),
                    new StyleLength(0f),
                    0f,
                    1f,
                    new StyleLength(0f),
                    new StyleLength(0f)
                );
                _launcherHistoryContentHeight = 0f;
            }

            if (_logScrollView != null)
            {
                _logScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }

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

            VisualElement logElement = GetLogOrderElement();
            EnsureChildOrder(
                _terminalContainer,
                _inputContainer,
                _autoCompleteContainer,
                logElement
            );
        }
        private void ApplyStandardLayout(float screenWidth)
        {
            if (_uiDocument == null)
            {
                return;
            }

            VisualElement rootElement = _uiDocument.rootVisualElement;
            if (rootElement == null)
            {
                return;
            }

            rootElement.style.width = screenWidth;
            rootElement.style.height = _currentWindowHeight;

            ConfigureStandardLayoutElements(screenWidth);
        }

        private void ConfigureStandardLayoutElements(float screenWidth)
        {
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

            ApplyLogDisplay(
                DisplayStyle.Flex,
                new StyleLength(StyleKeyword.Null),
                new StyleLength(StyleKeyword.Null),
                new StyleLength(StyleKeyword.Null),
                1f,
                1f,
                new StyleLength(0f),
                new StyleLength(0f)
            );
            if (_logScrollView != null)
            {
                _logScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }

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

            VisualElement logElement = GetLogOrderElement();
            EnsureChildOrder(
                _terminalContainer,
                logElement,
                _autoCompleteContainer,
                _inputContainer
            );
        }
        private void UpdateLauncherLayoutMetrics()
        {
            if (!IsLauncherActive || !_launcherMetricsInitialized)
            {
                return;
            }

            float horizontalPadding = _launcherMetrics.InsetPadding;
            float verticalPadding = Mathf.Max(6f, horizontalPadding * 0.3f);
            float inputHeight = Mathf.Max(_inputContainer.resolvedStyle.height, 0f);
            if (inputHeight <= 0f)
            {
                inputHeight = LauncherInputFallbackHeight;
            }
            float availableWidth = Mathf.Max(0f, _launcherMetrics.Width - (horizontalPadding * 2f));
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
                _autoCompleteContainer.style.marginTop = LauncherAutoCompleteSpacing * 0.5f;
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
                int pendingLogs = ActiveHistory?.Count ?? 0;
                visibleHistoryCount = Mathf.Min(
                    pendingLogs,
                    _launcherMetrics.HistoryVisibleEntryCount
                );
                if (visibleHistoryCount == 0)
                {
                    _launcherHistoryContentHeight = 0f;
                }
            }

            bool hasHistory = visibleHistoryCount > 0;

            const float MinimumSpacing = 2f;
            float spacingAboveLog = 0f;
            if (hasHistory && hasSuggestions)
            {
                spacingAboveLog = Mathf.Max(MinimumSpacing, LauncherAutoCompleteSpacing * 0.5f);
            }

            float reservedForSuggestions = hasSuggestions
                ? suggestionsHeight + spacingAboveLog
                : 0f;

            float historyHeightFromContent = hasHistory ? _launcherHistoryContentHeight : 0f;
            if (float.IsNaN(historyHeightFromContent) || historyHeightFromContent < 0f)
            {
                historyHeightFromContent = 0f;
            }

            float estimatedHistoryHeight = hasHistory
                ? visibleHistoryCount * LauncherEstimatedHistoryRowHeight
                : 0f;

            float maximumHistoryHeight = Mathf.Max(0f, _launcherMetrics.HistoryHeight);

            float desiredHistoryHeight = hasHistory
                ? Mathf.Min(
                    Mathf.Max(historyHeightFromContent, estimatedHistoryHeight),
                    maximumHistoryHeight
                )
                : 0f;
            if (desiredHistoryHeight < 0f)
            {
                desiredHistoryHeight = 0f;
            }

            float minimumHeight = (verticalPadding * 2f) + inputHeight + reservedForSuggestions;
            float desiredHeight = minimumHeight + desiredHistoryHeight;
            float clampedHeight = Mathf.Clamp(
                desiredHeight,
                minimumHeight,
                _launcherMetrics.Height
            );

            if (!Mathf.Approximately(clampedHeight, _targetWindowHeight))
            {
                float previousTarget = _targetWindowHeight;
                _targetWindowHeight = clampedHeight;

                if (_launcherMetrics.SnapOpen)
                {
                    _currentWindowHeight = _targetWindowHeight;
                    _isAnimating = false;
                }
                else
                {
                    _initialWindowHeight = Mathf.Clamp(
                        _currentWindowHeight,
                        minimumHeight,
                        _launcherMetrics.Height
                    );
                    if (!Mathf.Approximately(previousTarget, _targetWindowHeight))
                    {
                        StartHeightAnimation();
                    }
                }
            }

            float availableForHistory =
                _currentWindowHeight
                - (verticalPadding * 2f)
                - inputHeight
                - reservedForSuggestions;
            availableForHistory = Mathf.Min(availableForHistory, maximumHistoryHeight);
            availableForHistory = Mathf.Max(0f, availableForHistory);

            bool hasHistoryContent = _logListItems.Count > 0;

            if (availableForHistory <= 0.01f || !hasHistoryContent)
            {
                ApplyLogDisplay(
                    DisplayStyle.None,
                    new StyleLength(0f),
                    new StyleLength(0f),
                    new StyleLength(0f),
                    0f,
                    1f,
                    new StyleLength(spacingAboveLog),
                    new StyleLength(0f)
                );
                _launcherHistoryContentHeight = 0f;
            }
            else
            {
                StyleLength historyLength = new StyleLength(availableForHistory);
                ApplyLogDisplay(
                    DisplayStyle.Flex,
                    historyLength,
                    historyLength,
                    new StyleLength(0f),
                    0f,
                    1f,
                    new StyleLength(spacingAboveLog),
                    new StyleLength(0f)
                );
            }

            if (_logScrollView != null)
            {
                _logScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
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


        internal void ArrangeStandardVisualHierarchyForTests()
        {
            VisualElement logElement = GetLogOrderElement();
            EnsureChildOrder(
                _terminalContainer,
                logElement,
                _autoCompleteContainer,
                _inputContainer
            );
        }

        internal void ConfigureStandardLayoutForTests(float screenWidth)
        {
            ConfigureStandardLayoutElements(screenWidth);
        }


        internal void ApplyLauncherLayoutForTests(float width, float height)
        {
            ApplyLauncherLayout(width, height);
        }

        internal void ArrangeLauncherVisualHierarchyForTests()
        {
            VisualElement logElement = GetLogOrderElement();
            EnsureChildOrder(
                _terminalContainer,
                _inputContainer,
                _autoCompleteContainer,
                logElement
            );
        }


        private VisualElement GetLogOrderElement()
        {
            if (_logListView != null)
            {
                return _logListView;
            }

            return _logScrollView;
        }

        private void ApplyLogDisplay(
            DisplayStyle display,
            StyleLength height,
            StyleLength maxHeight,
            StyleLength minHeight,
            float flexGrow,
            float flexShrink,
            StyleLength marginTop,
            StyleLength marginBottom
        )
        {
            void Apply(VisualElement element)
            {
                if (element == null)
                {
                    return;
                }

                element.style.display = display;
                element.style.height = height;
                element.style.maxHeight = maxHeight;
                element.style.minHeight = minHeight;
                element.style.flexGrow = flexGrow;
                element.style.flexShrink = flexShrink;
                element.style.marginTop = marginTop;
                element.style.marginBottom = marginBottom;
            }

            Apply(_logListView);
            Apply(_logScrollView);
        }

        private static void EnsureChildOrder(VisualElement parent, params VisualElement[] children)
        {
            if (parent == null || children == null || children.Length == 0)
            {
                return;
            }

            int insertIndex = 0;
            for (int i = 0; i < children.Length; ++i)
            {
                VisualElement child = children[i];
                if (child == null)
                {
                    continue;
                }

                if (child.parent != parent)
                {
                    parent.Add(child);
                }

                int currentIndex = parent.IndexOf(child);
                if (currentIndex != insertIndex)
                {
                    parent.Remove(child);
                    int boundedIndex = Mathf.Clamp(insertIndex, 0, parent.childCount);
                    parent.Insert(boundedIndex, child);
                }

                insertIndex += 1;
            }
        }
    }
}
