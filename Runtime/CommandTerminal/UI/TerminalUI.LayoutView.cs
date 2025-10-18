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

            bool useLauncherLayout =
                _launcherMetricsInitialized && (IsLauncherActive || _isClosingLauncher);
            if (useLauncherLayout)
            {
                ApplyLauncherLayout(screenWidth, screenHeight);
                UpdateLauncherLayoutMetrics();
            }
            else
            {
                ApplyStandardLayout(screenWidth);
            }

            _terminalContainer.style.height = _currentWindowHeight;

            bool shouldDisplayTerminal =
                (_state != TerminalState.Closed || _isAnimating) && _currentWindowHeight > 0.1f;

            UpdateTerminalVisibility(shouldDisplayTerminal);

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
                _needsScrollToEnd = !ScrollToEnd();
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
            bool closingLauncher = _isClosingLauncher && wasLauncher;
            if (_state != TerminalState.OpenLauncher && !closingLauncher)
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
                        bool keepLauncherMetrics = closingLauncher && wasLauncher;
                        if (!keepLauncherMetrics)
                        {
                            _launcherMetricsInitialized = false;
                            _launcherSuggestionContentHeight = 0f;
                            _launcherHistoryContentHeight = 0f;
                        }
                        _realWindowHeight = keepLauncherMetrics
                            ? Mathf.Max(_currentWindowHeight, 0f)
                            : height * maxHeight * smallTerminalRatio;
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
                        _state == TerminalState.OpenLauncher && _launcherMetrics.SnapOpen;
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
            float verticalPadding = Mathf.Max(6f, _launcherMetrics.InsetPadding * 0.35f);
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

            float paddingTop = LayoutMeasurementUtility.ResolvePadding(
                _terminalContainer.resolvedStyle.paddingTop,
                _terminalContainer.style.paddingTop
            );
            float paddingBottom = LayoutMeasurementUtility.ResolvePadding(
                _terminalContainer.resolvedStyle.paddingBottom,
                _terminalContainer.style.paddingBottom
            );
            float containerHeight = LayoutMeasurementUtility.ComputeStandardContainerHeight(
                _currentWindowHeight,
                paddingTop,
                paddingBottom
            );
            _terminalContainer.style.height = containerHeight;
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
                _historyListAdapter?.SetJustification(Justify.FlexEnd);
                _launcherViewController?.ConfigureForStandardMode();
                RestoreStandardScrollBounds();
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
            if ((!IsLauncherActive && !_isClosingLauncher) || !_launcherMetricsInitialized)
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

            VisualElement historyContent = _logScrollView.contentContainer;
            int visibleHistoryCount = 0;
            float measuredHistoryHeightSum = 0f;
            int historyChildCount = historyContent.childCount;
            for (int i = 0; i < historyChildCount; ++i)
            {
                VisualElement entry = historyContent[i];
                if (entry == null || entry.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                visibleHistoryCount++;

                float entryHeight = entry.resolvedStyle.height;
                if (!float.IsNaN(entryHeight) && entryHeight > 0.5f)
                {
                    measuredHistoryHeightSum += entryHeight;
                }
            }

            int maximumVisibleEntries = _launcherMetrics.HistoryVisibleEntryCount;
            if (visibleHistoryCount == 0)
            {
                int pendingLogs = ActiveHistory?.Count ?? 0;
                if (maximumVisibleEntries > 0)
                {
                    visibleHistoryCount = Mathf.Min(pendingLogs, maximumVisibleEntries);
                }
                else
                {
                    visibleHistoryCount = pendingLogs;
                }

                if (visibleHistoryCount == 0)
                {
                    _launcherHistoryContentHeight = 0f;
                }
            }

            if (maximumVisibleEntries > 0 && visibleHistoryCount > maximumVisibleEntries)
            {
                visibleHistoryCount = maximumVisibleEntries;
            }

            bool hasHistoryContent = _logListItems.Count > 0;

            float measuredHistoryHeight = _launcherHistoryContentHeight;
            if (measuredHistoryHeight <= 0f && measuredHistoryHeightSum > 0f)
            {
                measuredHistoryHeight = measuredHistoryHeightSum;
            }
            if (measuredHistoryHeight < 0f || float.IsNaN(measuredHistoryHeight))
            {
                measuredHistoryHeight = 0f;
            }

            LauncherLayoutSnapshot snapshot = CalculateLauncherLayoutSnapshot(
                _launcherMetrics,
                _currentWindowHeight,
                horizontalPadding,
                verticalPadding,
                inputHeight,
                IsLauncherActive || _isClosingLauncher,
                hasSuggestions,
                effectiveSuggestionHeight,
                visibleHistoryCount,
                measuredHistoryHeight
            );

            if (hasSuggestions)
            {
                float suggestionsHeight = Mathf.Max(0f, snapshot.SuggestionsHeight);
                _autoCompleteContainer.style.height = suggestionsHeight;
                if (_autoCompleteViewport != null)
                {
                    _autoCompleteViewport.style.height = suggestionsHeight;
                }
            }

            _autoCompleteContainer.style.marginBottom = 0;

            float clampedHeight = snapshot.ClampedHeight;
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
                        snapshot.MinimumHeight,
                        _launcherMetrics.Height
                    );
                    if (!Mathf.Approximately(previousTarget, _targetWindowHeight))
                    {
                        StartHeightAnimation();
                    }
                }
            }

            snapshot = snapshot.WithCurrentHeight(_currentWindowHeight);

            float availableForHistory = snapshot.AvailableHistoryHeight;
            float spacingAboveLog = snapshot.SpacingAboveHistory;

            int historyEntryCap = snapshot.Metrics.HistoryVisibleEntryCount;
            bool exceedsEntryCap = historyEntryCap > 0 && _logListItems.Count > historyEntryCap;
            bool hasHistoryOverflow =
                hasHistoryContent
                && (
                    exceedsEntryCap
                    || (
                        snapshot.VisibleHistoryCount > 0
                        && _logListItems.Count > snapshot.VisibleHistoryCount
                    )
                    || snapshot.HistoryMeasuredHeight > availableForHistory + 0.5f
                    || snapshot.HistoryEstimatedHeight > availableForHistory + 0.5f
                );

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
                UpdateLauncherScrollBar(snapshot, availableForHistory, hasHistoryOverflow);
                _historyListAdapter?.SetJustification(Justify.FlexStart);
                _launcherViewController?.ConfigureForLauncherMode();
                _launcherViewController?.ClampScroll();
                _launcherViewController?.UpdateFade();
                _launcherViewController?.ScheduleFade();
            }

            ReportLauncherLayoutSnapshot(snapshot);
        }

        private void UpdateLauncherFade()
        {
            _launcherViewController?.UpdateFade();
        }

        private void RequestLauncherFade()
        {
            _launcherViewController?.ScheduleFade();
        }

        private void UpdateLauncherScrollBar(
            LauncherLayoutSnapshot snapshot,
            float availableForHistory,
            bool shouldShow
        )
        {
            ScrollView scrollView = _logScrollView;
            if (scrollView == null)
            {
                return;
            }

            scrollView.verticalScrollerVisibility = shouldShow
                ? ScrollerVisibility.Auto
                : ScrollerVisibility.Hidden;

            Scroller scroller = scrollView.verticalScroller;
            if (scroller == null)
            {
                return;
            }

            if (!shouldShow)
            {
                scroller.style.display = DisplayStyle.None;
                scroller.highValue = 0f;
                scroller.value = 0f;
                return;
            }

            scroller.style.display = StyleKeyword.Null;

            float viewportHeight =
                scrollView.contentViewport?.layout.height ?? Mathf.Max(availableForHistory, 0f);
            float measuredHeight = Mathf.Max(
                _launcherHistoryContentHeight,
                LayoutMeasurementUtility.ClampPositive(scrollView.contentContainer.layout.height),
                snapshot.HistoryMeasuredHeight
            );

            float estimatedHeight = Mathf.Max(
                measuredHeight,
                snapshot.HistoryEstimatedHeight,
                snapshot.VisibleHistoryCount * snapshot.HistoryRowHeightEstimate
            );

            if (_logListItems.Count > 0)
            {
                float totalEstimate = _logListItems.Count * snapshot.HistoryRowHeightEstimate;
                estimatedHeight = Mathf.Max(estimatedHeight, totalEstimate);
            }

            int entryCap = snapshot.Metrics.HistoryVisibleEntryCount;
            if (entryCap > 0 && _logListItems.Count > entryCap)
            {
                float cappedEstimate = (entryCap + 1) * snapshot.HistoryRowHeightEstimate;
                estimatedHeight = Mathf.Max(estimatedHeight, cappedEstimate);
            }

            float scrollRange = Mathf.Max(0f, estimatedHeight - viewportHeight);
            scroller.highValue = scrollRange;
            scroller.value = Mathf.Clamp(scroller.value, scroller.lowValue, scroller.highValue);

            scrollView
                .schedule.Execute(() =>
                {
                    Scroller deferredScroller = scrollView.verticalScroller;
                    if (deferredScroller == null)
                    {
                        return;
                    }

                    float deferredViewport =
                        scrollView.contentViewport?.layout.height ?? viewportHeight;
                    float deferredContent =
                        scrollView.contentContainer?.layout.height ?? measuredHeight;
                    float deferredRange = Mathf.Max(0f, deferredContent - deferredViewport);
                    deferredScroller.highValue = deferredRange;
                    deferredScroller.value = Mathf.Clamp(
                        deferredScroller.value,
                        deferredScroller.lowValue,
                        deferredScroller.highValue
                    );
                })
                .ExecuteLater(0);
        }

        private void RestoreStandardScrollBounds()
        {
            ScrollView scrollView = _logScrollView;
            if (scrollView == null)
            {
                return;
            }

            scrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;

            float GetClosingScrollTarget(Scroller scrollerLocal)
            {
                if (scrollerLocal == null)
                {
                    return 0f;
                }

                float highValue = scrollerLocal.highValue;
                if (highValue > 0.001f)
                {
                    return highValue;
                }

                VisualElement viewport = scrollView.contentViewport;
                VisualElement content = scrollView.contentContainer;
                float viewportHeight = viewport != null ? viewport.layout.height : 0f;
                float contentHeight = content != null ? content.layout.height : 0f;
                float fallback = Mathf.Max(0f, contentHeight - viewportHeight);
                return Mathf.Max(highValue, fallback);
            }

            void SnapClosingScroll(Scroller scrollerLocal)
            {
                if (scrollerLocal == null)
                {
                    return;
                }

                float target = GetClosingScrollTarget(scrollerLocal);
                if (!Mathf.Approximately(scrollerLocal.value, target))
                {
                    scrollerLocal.value = target;
                }

                Vector2 offset = scrollView.scrollOffset;
                if (!Mathf.Approximately(offset.y, target))
                {
                    scrollView.scrollOffset = new Vector2(offset.x, target);
                }
            }

            float GetRestoredStandardScrollValue(Scroller scrollerLocal)
            {
                if (scrollerLocal == null)
                {
                    return 0f;
                }

                float lowValue = scrollerLocal.lowValue;
                float highValue = scrollerLocal.highValue;
                float clamped = Mathf.Clamp(_cachedStandardScrollValue, lowValue, highValue);

                float cachedRange = _cachedStandardScrollHighValue - _cachedStandardScrollLowValue;
                float currentRange = highValue - lowValue;
                if (cachedRange <= 0.0001f || currentRange <= 0.0001f)
                {
                    return clamped;
                }

                float normalized = Mathf.Clamp01(_cachedStandardScrollNormalized);
                float ratioValue = (normalized * currentRange) + lowValue;
                return Mathf.Clamp(ratioValue, lowValue, highValue);
            }

            void AdjustBounds()
            {
                Scroller scroller = scrollView.verticalScroller;
                if (scroller == null)
                {
                    return;
                }

                scroller.style.display = StyleKeyword.Null;

                float clampedValue = Mathf.Clamp(
                    scroller.value,
                    scroller.lowValue,
                    scroller.highValue
                );
                if (!Mathf.Approximately(clampedValue, scroller.value))
                {
                    scroller.value = clampedValue;
                }

                if (_restoreStandardScrollPending && _hasCachedStandardScroll)
                {
                    float lowValue = scroller.lowValue;
                    float highValue = scroller.highValue;
                    float range = highValue - lowValue;
                    bool hasRange = range > 0.01f;
                    bool cachedIsNearLow =
                        Mathf.Abs(_cachedStandardScrollValue - lowValue) <= 0.01f;
                    if (hasRange || cachedIsNearLow)
                    {
                        float restored = GetRestoredStandardScrollValue(scroller);
                        scroller.value = restored;
                        UpdateStandardScrollAlignment(restored);
                        _restoreStandardScrollPending = false;
                    }

                    if (_restoreStandardScrollPending)
                    {
                        return;
                    }
                }

                if (_isClosingStandard)
                {
                    SnapClosingScroll(scroller);
                    _historyListAdapter?.SetJustification(Justify.FlexEnd);
                    return;
                }

                UpdateStandardScrollAlignment(scroller.value);
            }

            AdjustBounds();
            scrollView
                .schedule.Execute(() =>
                {
                    Scroller scroller = scrollView.verticalScroller;
                    if (scroller == null)
                    {
                        return;
                    }

                    scroller.style.display = StyleKeyword.Null;
                    float clampedValue = Mathf.Clamp(
                        scroller.value,
                        scroller.lowValue,
                        scroller.highValue
                    );
                    if (!Mathf.Approximately(clampedValue, scroller.value))
                    {
                        scroller.value = clampedValue;
                    }

                    if (_restoreStandardScrollPending && _hasCachedStandardScroll)
                    {
                        float lowValue = scroller.lowValue;
                        float highValue = scroller.highValue;
                        float range = highValue - lowValue;
                        bool hasRange = range > 0.01f;
                        bool cachedIsNearLow =
                            Mathf.Abs(_cachedStandardScrollValue - lowValue) <= 0.01f;
                        if (hasRange || cachedIsNearLow)
                        {
                            float restored = GetRestoredStandardScrollValue(scroller);
                            scroller.value = restored;
                            UpdateStandardScrollAlignment(restored);
                            _restoreStandardScrollPending = false;
                        }

                        if (_restoreStandardScrollPending)
                        {
                            return;
                        }
                    }

                    if (_isClosingStandard)
                    {
                        SnapClosingScroll(scroller);
                        _historyListAdapter?.SetJustification(Justify.FlexEnd);
                        return;
                    }

                    UpdateStandardScrollAlignment(scroller.value);
                })
                .ExecuteLater(0);
        }

        private void ClearLauncherFade()
        {
            _launcherViewController?.ClearFade();
        }

        private void ClampLauncherScroll()
        {
            _launcherViewController?.ClampScroll();
        }

        private void OnLogScrollValueChanged(float value)
        {
            if (IsLauncherActive)
            {
                _launcherViewController?.HandleScrollValueChanged(value);
                return;
            }

            if (_isClosingStandard)
            {
                return;
            }

            UpdateStandardScrollAlignment(value);
        }

        private void UpdateStandardScrollAlignment()
        {
            if (_logScrollView == null || _isClosingStandard)
            {
                return;
            }

            Scroller scroller = _logScrollView.verticalScroller;
            if (scroller == null)
            {
                return;
            }

            UpdateStandardScrollAlignment(scroller.value);
        }

        private void UpdateStandardScrollAlignment(float scrollerValue)
        {
            if (IsLauncherActive || _isClosingStandard)
            {
                return;
            }

            ScrollView scrollView = _logScrollView;
            if (scrollView == null)
            {
                return;
            }

            Scroller scroller = scrollView.verticalScroller;
            if (scroller == null)
            {
                return;
            }

            bool hasOverflow = scroller.highValue > 0.01f;
            bool nearBottom = !hasOverflow || scrollerValue >= scroller.highValue - 0.5f;
            _historyListAdapter?.SetJustification(nearBottom ? Justify.FlexEnd : Justify.FlexStart);
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

        private LauncherLayoutSnapshot CalculateLauncherLayoutSnapshot(
            LauncherLayoutMetrics metrics,
            float currentWindowHeight,
            float horizontalPadding,
            float verticalPadding,
            float inputHeight,
            bool isLauncherActive,
            bool hasSuggestions,
            float suggestionMeasuredHeight,
            int visibleHistoryCount,
            float measuredHistoryHeight
        )
        {
            float historyLimit = Mathf.Max(0f, metrics.HistoryHeight);

            bool hasHistory = visibleHistoryCount > 0;

            if (!hasHistory)
            {
                measuredHistoryHeight = 0f;
            }

            measuredHistoryHeight = LayoutMeasurementUtility.ClampPositive(measuredHistoryHeight);

            float sanitizedHistoryContentHeight = hasHistory
                ? Mathf.Min(measuredHistoryHeight, historyLimit)
                : 0f;
            sanitizedHistoryContentHeight = LayoutMeasurementUtility.ClampPositive(
                sanitizedHistoryContentHeight
            );

            float rowHeightEstimate = LayoutMeasurementUtility.ComputeAverageRowHeight(
                sanitizedHistoryContentHeight,
                visibleHistoryCount,
                LauncherEstimatedHistoryRowHeight
            );

            rowHeightEstimate = LayoutMeasurementUtility.ClampRowHeightEstimate(
                rowHeightEstimate,
                LauncherEstimatedHistoryRowHeight,
                4f,
                512f
            );

            float suggestionsHeight = 0f;
            if (hasSuggestions)
            {
                float suggestionHeightSource = suggestionMeasuredHeight;
                if (suggestionHeightSource <= 0f)
                {
                    suggestionHeightSource = LauncherEstimatedSuggestionRowHeight;
                }

                suggestionsHeight = Mathf.Clamp(suggestionHeightSource, 0f, historyLimit);
            }

            const float MinimumSpacing = 1f;
            float spacingAboveHistory = 0f;
            if (hasHistory && hasSuggestions)
            {
                spacingAboveHistory = Mathf.Max(MinimumSpacing, LauncherAutoCompleteSpacing * 0.5f);
            }

            float reservedSuggestionHeight =
                LayoutMeasurementUtility.ComputeReservedSuggestionHeight(
                    IsLauncherActive || _isClosingLauncher,
                    hasSuggestions,
                    suggestionsHeight,
                    spacingAboveHistory,
                    LauncherAutoCompleteSpacing
                );

            float estimatedHistoryHeight = hasHistory
                ? Mathf.Max(0f, visibleHistoryCount * rowHeightEstimate)
                : 0f;

            float fallbackHistoryHeight = estimatedHistoryHeight;
            if (fallbackHistoryHeight <= 0f && hasHistory)
            {
                fallbackHistoryHeight = Mathf.Max(
                    visibleHistoryCount * rowHeightEstimate,
                    rowHeightEstimate
                );
            }

            if (sanitizedHistoryContentHeight > 0f && hasHistory)
            {
                float cappedEstimate = Mathf.Max(
                    rowHeightEstimate,
                    visibleHistoryCount * rowHeightEstimate
                );
                fallbackHistoryHeight = Mathf.Min(sanitizedHistoryContentHeight, cappedEstimate);
            }

            float desiredHistoryHeight = LayoutMeasurementUtility.ComputeDesiredHistoryHeight(
                hasHistory,
                fallbackHistoryHeight,
                historyLimit
            );

            float minimumHeight = (verticalPadding * 2f) + inputHeight + reservedSuggestionHeight;
            float desiredHeight = minimumHeight + desiredHistoryHeight;
            float clampedHeight = Mathf.Clamp(desiredHeight, minimumHeight, metrics.Height);

            float historyTargetHeight = LayoutMeasurementUtility.ClampToHistoryLimit(
                Mathf.Max(0f, clampedHeight - minimumHeight),
                historyLimit
            );
            float cappedHeight = minimumHeight + historyTargetHeight;
            if (!Mathf.Approximately(cappedHeight, clampedHeight))
            {
                clampedHeight = cappedHeight;
            }

            float availableHistoryHeight = Mathf.Clamp(
                currentWindowHeight - minimumHeight,
                0f,
                historyLimit
            );

            return new LauncherLayoutSnapshot(
                metrics,
                horizontalPadding,
                verticalPadding,
                inputHeight,
                IsLauncherActive || _isClosingLauncher,
                hasSuggestions,
                suggestionsHeight,
                reservedSuggestionHeight,
                spacingAboveHistory,
                hasHistory,
                visibleHistoryCount,
                sanitizedHistoryContentHeight,
                estimatedHistoryHeight,
                rowHeightEstimate,
                desiredHistoryHeight,
                historyTargetHeight,
                minimumHeight,
                desiredHeight,
                clampedHeight,
                currentWindowHeight,
                availableHistoryHeight
            );
        }

        private void ReportLauncherLayoutSnapshot(LauncherLayoutSnapshot snapshot)
        {
            LauncherLayoutComputed?.Invoke(snapshot);
        }

        internal static event Action<LauncherLayoutSnapshot> LauncherLayoutComputed;

        internal readonly struct LauncherLayoutSnapshot
        {
            internal LauncherLayoutSnapshot(
                LauncherLayoutMetrics metrics,
                float horizontalPadding,
                float verticalPadding,
                float inputHeight,
                bool isLauncherActive,
                bool hasSuggestions,
                float suggestionsHeight,
                float reservedSuggestionsHeight,
                float spacingAboveHistory,
                bool hasHistory,
                int visibleHistoryCount,
                float historyMeasuredHeight,
                float historyEstimatedHeight,
                float historyRowHeightEstimate,
                float historyDesiredHeight,
                float historyTargetHeight,
                float minimumHeight,
                float desiredHeight,
                float clampedHeight,
                float currentWindowHeight,
                float availableHistoryHeight
            )
            {
                Metrics = metrics;
                HorizontalPadding = horizontalPadding;
                VerticalPadding = verticalPadding;
                InputHeight = inputHeight;
                IsLauncherActive = isLauncherActive;
                HasSuggestions = hasSuggestions;
                SuggestionsHeight = suggestionsHeight;
                ReservedSuggestionsHeight = reservedSuggestionsHeight;
                SpacingAboveHistory = spacingAboveHistory;
                HasHistory = hasHistory;
                VisibleHistoryCount = visibleHistoryCount;
                HistoryMeasuredHeight = historyMeasuredHeight;
                HistoryEstimatedHeight = historyEstimatedHeight;
                HistoryRowHeightEstimate = historyRowHeightEstimate;
                HistoryDesiredHeight = historyDesiredHeight;
                HistoryTargetHeight = historyTargetHeight;
                MinimumHeight = minimumHeight;
                DesiredHeight = desiredHeight;
                ClampedHeight = clampedHeight;
                CurrentWindowHeight = currentWindowHeight;
                HistoryLimit = metrics.HistoryHeight;
                AvailableHistoryHeight = Mathf.Clamp(availableHistoryHeight, 0f, HistoryLimit);
            }

            internal LauncherLayoutSnapshot WithCurrentHeight(float currentHeight)
            {
                float recalculatedAvailableHistory = Mathf.Clamp(
                    currentHeight - MinimumHeight,
                    0f,
                    HistoryLimit
                );

                return new LauncherLayoutSnapshot(
                    Metrics,
                    HorizontalPadding,
                    VerticalPadding,
                    InputHeight,
                    IsLauncherActive,
                    HasSuggestions,
                    SuggestionsHeight,
                    ReservedSuggestionsHeight,
                    SpacingAboveHistory,
                    HasHistory,
                    VisibleHistoryCount,
                    HistoryMeasuredHeight,
                    HistoryEstimatedHeight,
                    HistoryRowHeightEstimate,
                    HistoryDesiredHeight,
                    HistoryTargetHeight,
                    MinimumHeight,
                    DesiredHeight,
                    ClampedHeight,
                    currentHeight,
                    recalculatedAvailableHistory
                );
            }

            public override string ToString()
            {
                return "LauncherLayoutSnapshot "
                    + $"(InputHeight={InputHeight:F2}, Suggestions={SuggestionsHeight:F2}, Reserved={ReservedSuggestionsHeight:F2}, "
                    + $"MeasuredHistory={HistoryMeasuredHeight:F2}, EstimatedHistory={HistoryEstimatedHeight:F2}, DesiredHistory={HistoryDesiredHeight:F2}, "
                    + $"RowEstimate={HistoryRowHeightEstimate:F2}, TargetHistory={HistoryTargetHeight:F2}, HistoryLimit={HistoryLimit:F2}, MinimumHeight={MinimumHeight:F2}, DesiredHeight={DesiredHeight:F2}, "
                    + $"ClampedHeight={ClampedHeight:F2}, CurrentHeight={CurrentWindowHeight:F2}, AvailableHistory={AvailableHistoryHeight:F2}, "
                    + $"VisibleHistoryCount={VisibleHistoryCount}, HasSuggestions={HasSuggestions}, HasHistory={HasHistory}, SpacingAboveHistory={SpacingAboveHistory:F2})";
            }

            internal LauncherLayoutMetrics Metrics { get; }

            internal float HorizontalPadding { get; }

            internal float VerticalPadding { get; }

            internal float InputHeight { get; }

            internal bool IsLauncherActive { get; }

            internal bool HasSuggestions { get; }

            internal float SuggestionsHeight { get; }

            internal float ReservedSuggestionsHeight { get; }

            internal float SpacingAboveHistory { get; }

            internal bool HasHistory { get; }

            internal int VisibleHistoryCount { get; }

            internal float HistoryMeasuredHeight { get; }

            internal float HistoryEstimatedHeight { get; }

            internal float HistoryRowHeightEstimate { get; }

            internal float HistoryDesiredHeight { get; }

            internal float HistoryTargetHeight { get; }

            internal float HistoryLimit { get; }

            internal float MinimumHeight { get; }

            internal float DesiredHeight { get; }

            internal float ClampedHeight { get; }

            internal float CurrentWindowHeight { get; }

            internal float AvailableHistoryHeight { get; }
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
