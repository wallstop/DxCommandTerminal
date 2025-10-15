namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using Extensions;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed partial class TerminalUI
    {
        private void RefreshLogs()
        {
            CommandLog log = ActiveLog;
            if (log == null)
            {
                return;
            }

            IReadOnlyList<LogItem> logs = log.Logs;
            
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
            bool dirty = _lastSeenBufferVersion != log.Version;
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
                    _lastSeenBufferVersion = log.Version;
                }
            }

            if (ShouldApplyHistoryFade())
            {
                ApplyHistoryFade(content, fadeFromTop: false);
            }
            else
            {
                ResetHistoryFade(content);
            }
        }

        private void RefreshLauncherHistory()
        {
            if (_logScrollView == null)
            {
                return;
            }

            VisualElement content = _logScrollView.contentContainer;
            CommandHistory history = ActiveHistory;

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
            }

            if (ShouldApplyHistoryFade())
            {
                ApplyHistoryFade(content, fadeFromTop: true);
            }
            else
            {
                ResetHistoryFade(content);
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

    
        private bool ShouldApplyHistoryFade()
        {
            return _state switch
            {
                TerminalState.OpenLauncher => _historyFadeTargets.HasFlagNoAlloc(
                    TerminalHistoryFadeTargets.Launcher
                ),
                TerminalState.OpenSmall => _historyFadeTargets.HasFlagNoAlloc(
                    TerminalHistoryFadeTargets.SmallTerminal
                ),
                TerminalState.OpenFull => _historyFadeTargets.HasFlagNoAlloc(
                    TerminalHistoryFadeTargets.FullTerminal
                ),
                _ => false,
            };
        }

        private float GetHistoryFadeRangeFactor()
        {
            return _state switch
            {
                TerminalState.OpenLauncher => 0.6f,
                TerminalState.OpenFull => 1.0f,
                TerminalState.OpenSmall => 0.85f,
                _ => 0.85f,
            };
        }

        private float GetHistoryFadeMinimumOpacity()
        {
            return _state == TerminalState.OpenLauncher ? 0.35f : 0.45f;
        }

        private float GetHistoryFallbackRowHeight()
        {
            return _state == TerminalState.OpenLauncher
                ? LauncherEstimatedHistoryRowHeight
                : StandardEstimatedHistoryRowHeight;
        }

        private float GetHistoryFadeExponent()
        {
            if (_state == TerminalState.OpenLauncher && _launcherMetricsInitialized)
            {
                return Mathf.Max(0.01f, _launcherMetrics.HistoryFadeExponent);
            }

            return 1f;
        }

        private void ApplyHistoryFade(VisualElement container, bool fadeFromTop)
        {
            if (container == null)
            {
                return;
            }

            Rect viewportBounds = _logViewport?.worldBound ?? Rect.zero;
            bool viewportIsValid = viewportBounds.height > 0.01f;
            float fallbackRowHeight = GetHistoryFallbackRowHeight();

            int childCount = container.childCount;
            if (childCount == 0)
            {
                return;
            }

            int visibleCount = 0;
            for (int i = 0; i < childCount; ++i)
            {
                VisualElement element = container[i];
                if (element == null || element.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                visibleCount++;
            }

            if (visibleCount == 0)
            {
                return;
            }

            if (!viewportIsValid)
            {
                float fallbackHeight = Mathf.Max(1f, fallbackRowHeight * visibleCount);
                viewportBounds.height = fallbackHeight;
            }

            float fadeRange = Mathf.Max(1f, viewportBounds.height * GetHistoryFadeRangeFactor());
            float minimumOpacity = Mathf.Clamp01(GetHistoryFadeMinimumOpacity());

            int visibleIndex = 0;
            for (int i = 0; i < childCount; ++i)
            {
                VisualElement element = container[i];
                if (element == null || element.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                float distance;
                if (viewportIsValid)
                {
                    Rect childBounds = element.worldBound;
                    bool boundsValid = childBounds.height > 0.01f;
                    if (fadeFromTop)
                    {
                        distance = boundsValid
                            ? Mathf.Max(0f, childBounds.yMin - viewportBounds.yMin)
                            : fallbackRowHeight * visibleIndex;
                    }
                    else
                    {
                        int inverseIndex = Math.Max(0, visibleCount - visibleIndex - 1);
                        distance = boundsValid
                            ? Mathf.Max(0f, viewportBounds.yMax - childBounds.yMax)
                            : fallbackRowHeight * inverseIndex;
                    }
                }
                else
                {
                    int indexFromEdge = fadeFromTop
                        ? visibleIndex
                        : Math.Max(0, visibleCount - visibleIndex - 1);
                    distance = fallbackRowHeight * indexFromEdge;
                }

                float normalized = Mathf.Clamp01(distance / fadeRange);
                float adjusted = Mathf.Pow(normalized, GetHistoryFadeExponent());
                float opacity = Mathf.Lerp(1f, minimumOpacity, adjusted);
                element.style.opacity = opacity;

                visibleIndex++;
            }
        }

        private static void ResetHistoryFade(VisualElement container)
        {
            if (container == null)
            {
                return;
            }

            int childCount = container.childCount;
            for (int i = 0; i < childCount; ++i)
            {
                VisualElement element = container[i];
                if (element == null || element.resolvedStyle.display == DisplayStyle.None)
                {
                    continue;
                }

                element.style.opacity = 1f;
            }
        }

        private void CacheLauncherScrollPosition()
        {
            if (_logScrollView?.verticalScroller == null)
            {
                return;
            }

            float highValue = _logScrollView.verticalScroller.highValue;
            float currentValue = Mathf.Clamp(_logScrollView.verticalScroller.value, 0f, highValue);
            _cachedLauncherScrollValue = currentValue;
            _cachedLauncherScrollVersion = ActiveHistory?.Version ?? -1;
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
                .schedule.Execute(() =>
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
                                _input.CommandText = BuildCompletionText(currentHint);
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


    }
}
