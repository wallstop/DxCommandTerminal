namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed partial class TerminalUI
    {
        private VisualElement CreateLogListItem()
        {
            Label label = new();
            label.AddToClassList("terminal-output-label");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = 0f;
            label.style.flexShrink = 0f;
            label.style.alignSelf = Align.FlexStart;
            return label;
        }

        private void BindLogListItem(VisualElement element, int index)
        {
            if (_logListItems == null || index < 0 || index >= _logListItems.Count)
            {
                return;
            }

            LogItem logItem = _logListItems[index];
            switch (element)
            {
                case Label label:
                    label.text = logItem.message;
                    break;
                case TextField textField:
                    textField.value = logItem.message;
                    break;
                case Button button:
                    button.text = logItem.message;
                    break;
            }

            ApplyLogStyling(element, logItem);

            int totalCount = _logListItems.Count;
            if (totalCount <= 0)
            {
                totalCount = 1;
            }

            bool launcherFadeReady = TryGetLauncherFadeContext(out _, out _, out _);
            float targetOpacity = ComputeLogOpacity(index, totalCount);
            element.style.opacity = targetOpacity;

            if (launcherFadeReady)
            {
                LogFadeDiagnostic(
                    $"BindLogListItem index={index}, total={totalCount}, opacity={targetOpacity:F3}"
                );
                _launcherViewController?.ScheduleFade();
            }
        }

        private void RefreshLogs()
        {
            if (_logScrollView == null)
            {
                return;
            }

            CommandLog log = ActiveLog;
            if (log == null)
            {
                return;
            }

            if (IsLauncherActive && _launcherMetricsInitialized)
            {
                RefreshLauncherHistory();
                return;
            }

            IReadOnlyList<LogItem> logs = log.Logs;
            bool dirty = _lastSeenBufferVersion != log.Version || _logListItems.Count != logs.Count;

            if (dirty)
            {
                bool preserveScroll = false;
                float previousScrollValue = 0f;
                if (_logScrollView != null)
                {
                    Scroller scrollerSnapshot = _logScrollView.verticalScroller;
                    if (scrollerSnapshot != null)
                    {
                        previousScrollValue = scrollerSnapshot.value;
                        float highValue = scrollerSnapshot.highValue;
                        preserveScroll =
                            !_isClosingStandard
                            && highValue > 0.01f
                            && scrollerSnapshot.value < highValue - 0.5f;
                        LogScrollDiagnostic(
                            $"RefreshLogs dirty preserve={preserveScroll} value={scrollerSnapshot.value:F3} high={highValue:F3}"
                        );
                    }
                }

                _hasCachedStandardScroll = false;
                _restoreStandardScrollPending = false;
                _cachedStandardScrollValue = 0f;
                _cachedStandardScrollNormalized = 0f;
                _cachedStandardScrollLowValue = 0f;
                _cachedStandardScrollHighValue = 0f;
                _cachedStandardScrollAtEnd = false;
                _logListItems.Clear();
                for (int i = 0; i < logs.Count; ++i)
                {
                    _logListItems.Add(logs[i]);
                }

                RenderStandardLogContent();
                _lastSeenBufferVersion = log.Version;
                if (preserveScroll)
                {
                    RestoreStandardScrollValue(previousScrollValue);
                    LogScrollDiagnostic(
                        $"RefreshLogs restored previous scroll value={previousScrollValue:F3}"
                    );
                    UpdateStandardScrollAlignment(previousScrollValue);
                    _needsScrollToEnd = false;
                }
                else
                {
                    _needsScrollToEnd = true;
                    LogScrollDiagnostic("RefreshLogs will scroll to end on next update");
                }
            }
            else if (ShouldApplyHistoryFade())
            {
                UpdateRenderedLogOpacity(_logListItems.Count);
            }
        }

        private void RefreshLauncherHistory()
        {
            CommandHistory history = ActiveHistory;

            if (history == null)
            {
                _logListItems.Clear();
                RenderLauncherHistoryContent();
                _lastRenderedLauncherHistoryVersion = -1;
                _cachedLauncherScrollVersion = -1;
                _cachedLauncherScrollValue = 0f;
                _launcherHistoryContentHeight = 0f;
                _needsScrollToEnd = false;
                _launcherViewController?.ClearFade();
                LogLauncherDiagnostic("RefreshLauncherHistory cleared (no history)");
                return;
            }

            bool preserveScroll = false;
            float previousScrollValue = 0f;
            if (_logScrollView != null)
            {
                Scroller scrollerSnapshot = _logScrollView.verticalScroller;
                if (scrollerSnapshot != null)
                {
                    previousScrollValue = scrollerSnapshot.value;
                    float highValue = scrollerSnapshot.highValue;
                    preserveScroll = scrollerSnapshot.value > scrollerSnapshot.lowValue + 0.5f;
                    LogLauncherDiagnostic(
                        $"RefreshLauncherHistory preserve={preserveScroll} value={scrollerSnapshot.value:F3} high={highValue:F3}"
                    );
                }
            }

            history.CopyEntriesTo(_launcherHistoryEntries);
            long historyVersion = history.Version;
            LogLauncherDiagnostic(
                $"RefreshLauncherHistory version={historyVersion} cachedVersion={_lastRenderedLauncherHistoryVersion} entries={_launcherHistoryEntries.Count}"
            );

            if (
                _lastRenderedLauncherHistoryVersion == historyVersion
                && _logListItems.Count == _launcherHistoryEntries.Count
            )
            {
                if (ShouldApplyHistoryFade())
                {
                    UpdateRenderedLogOpacity(_logListItems.Count);
                }
                return;
            }

            _logListItems.Clear();
            for (int i = _launcherHistoryEntries.Count - 1; i >= 0; --i)
            {
                CommandHistoryEntry entry = _launcherHistoryEntries[i];
                _logListItems.Add(new LogItem(TerminalLogType.Input, entry.Text, string.Empty));
            }

            RenderLauncherHistoryContent();
            _lastRenderedLauncherHistoryVersion = historyVersion;
            _cachedLauncherScrollVersion = historyVersion;
            _cachedLauncherScrollValue = 0f;
            if (preserveScroll)
            {
                RestoreStandardScrollValue(previousScrollValue);
                LogLauncherDiagnostic(
                    $"RefreshLauncherHistory restored scroll value={previousScrollValue:F3}"
                );
            }
            else
            {
                LogLauncherDiagnostic("RefreshLauncherHistory will reset scroll to end");
            }
            _needsScrollToEnd = false;

            _launcherViewController?.ClampScroll();
            _launcherViewController?.UpdateFade();
            _launcherViewController?.ScheduleFade();
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
                TerminalState.OpenLauncher => 1.0f,
                TerminalState.OpenFull => 1.0f,
                TerminalState.OpenSmall => 0.85f,
                _ => 0.85f,
            };
        }

        private float GetHistoryFadeMinimumOpacity()
        {
            return _state == TerminalState.OpenLauncher ? 0.35f : 0.45f;
        }

        private float GetHistoryFadeExponent()
        {
            if (_state == TerminalState.OpenLauncher && _launcherMetricsInitialized)
            {
                return Mathf.Max(0.01f, _launcherMetrics.HistoryFadeExponent);
            }

            return 1f;
        }

        private float ComputeLogOpacity(int index, int totalCount)
        {
            if (!ShouldApplyHistoryFade() || totalCount <= 1)
            {
                return 1f;
            }

            bool fadeFromTop = _state == TerminalState.OpenLauncher;
            float normalized = fadeFromTop
                ? (float)index / (totalCount - 1)
                : (float)(totalCount - 1 - index) / (totalCount - 1);

            float range = Mathf.Clamp01(GetHistoryFadeRangeFactor());
            float clamped = Mathf.Clamp01(normalized * range);
            float exponent = Mathf.Max(0.01f, GetHistoryFadeExponent());
            float minimumOpacity = Mathf.Clamp01(GetHistoryFadeMinimumOpacity());
            float adjusted = Mathf.Pow(clamped, exponent);
            return Mathf.Lerp(1f, minimumOpacity, adjusted);
        }

        private bool TryGetLauncherFadeContext(
            out ScrollView scrollView,
            out VisualElement viewport,
            out VisualElement historyContent
        )
        {
            scrollView = _logScrollView;
            viewport = scrollView?.contentViewport;
            historyContent = scrollView?.contentContainer;

            bool ready =
                IsLauncherActive
                && _launcherMetricsInitialized
                && ShouldApplyHistoryFade()
                && scrollView != null
                && viewport != null
                && historyContent != null;

            if (enableFadeDiagnostics)
            {
                LogFadeDiagnostic(
                    $"FadeContext ready={ready} viewportHeight={viewport?.resolvedStyle.height ?? 0f:F3} contentChildren={historyContent?.childCount ?? 0}"
                );
            }

            return ready;
        }

        private void RenderStandardLogContent()
        {
            if (_logScrollView == null)
            {
                return;
            }

            VisualElement container = _logScrollView.contentContainer;
            if (container == null)
            {
                return;
            }

            int totalCount = _logListItems.Count;
            for (int i = container.childCount - 1; i >= totalCount; --i)
            {
                container.RemoveAt(i);
            }

            int existingChildren = container.childCount;

            // Update existing children.
            int updateCount = Math.Min(existingChildren, totalCount);
            for (int i = 0; i < updateCount; ++i)
            {
                BindLogListItem(container[i], i);
            }

            // Add any new children needed to represent the buffer.
            for (int i = updateCount; i < totalCount; ++i)
            {
                VisualElement element = CreateLogListItem();
                container.Add(element);
                BindLogListItem(element, i);
            }

            SetHistoryJustification(Justify.FlexEnd);
        }

        private void RenderLauncherHistoryContent()
        {
            if (_logScrollView == null)
            {
                return;
            }

            VisualElement container = _logScrollView.contentContainer;
            if (container == null)
            {
                return;
            }

            int totalCount = _logListItems.Count;
            for (int i = container.childCount - 1; i >= totalCount; --i)
            {
                container.RemoveAt(i);
            }

            int existingChildren = container.childCount;

            int updateCount = Math.Min(existingChildren, totalCount);
            for (int i = 0; i < updateCount; ++i)
            {
                BindLogListItem(container[i], i);
            }

            for (int i = updateCount; i < totalCount; ++i)
            {
                VisualElement element = CreateLogListItem();
                container.Add(element);
                BindLogListItem(element, i);
            }

            SetHistoryJustification(Justify.FlexStart);

            if (totalCount > 0)
            {
                _launcherHistoryContentHeight = totalCount * LauncherEstimatedHistoryRowHeight;
            }
            else
            {
                _launcherHistoryContentHeight = 0f;
            }
        }

        private void UpdateRenderedLogOpacity(int totalCount)
        {
            if (_logScrollView == null)
            {
                return;
            }

            VisualElement container = _logScrollView.contentContainer;
            if (container == null)
            {
                return;
            }

            totalCount = Mathf.Max(totalCount, 1);
            int childCount = container.childCount;
            for (int i = 0; i < childCount; ++i)
            {
                float opacity = ComputeLogOpacity(i, totalCount);
                VisualElement child = container[i];
                child.style.opacity = opacity;

                Label label = child as Label ?? child.Q<Label>(className: "terminal-output-label");
                if (label != null)
                {
                    label.style.opacity = opacity;
                }
            }
        }

        private void RestoreStandardScrollValue(float targetValue)
        {
            if (_logScrollView == null)
            {
                return;
            }

            Scroller scroller = _logScrollView.verticalScroller;
            if (scroller == null)
            {
                return;
            }

            float clamped = Mathf.Clamp(targetValue, scroller.lowValue, scroller.highValue);
            scroller.value = clamped;
            Vector2 offset = _logScrollView.scrollOffset;
            if (!Mathf.Approximately(offset.y, clamped))
            {
                _logScrollView.scrollOffset = new Vector2(offset.x, clamped);
            }
        }
    }
}
