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

        private VisualElement CreateLogListItem()
        {
            Label label = new();
            label.AddToClassList("terminal-output-label");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.flexGrow = 1f;
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
            element.style.opacity = ComputeLogOpacity(index, _logListItems.Count);
        }

        private void RefreshLogs()
        {
            if (_logListView == null)
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
            bool dirty =
                _lastSeenBufferVersion != log.Version
                || _logListItems.Count != logs.Count;

            if (dirty)
            {
                _logListItems.Clear();
                for (int i = 0; i < logs.Count; ++i)
                {
                    _logListItems.Add(logs[i]);
                }

                if (_logListView.itemsSource != _logListItems)
                {
                    _logListView.itemsSource = _logListItems;
                }

                _logListView.Rebuild();
                _lastSeenBufferVersion = log.Version;
                _needsScrollToEnd = true;
            }
            else if (ShouldApplyHistoryFade())
            {
                _logListView.RefreshItems();
            }
        }

        private void RefreshLauncherHistory()
        {
            CommandHistory history = ActiveHistory;

            if (history == null)
            {
                _logListItems.Clear();
                if (_logListView != null)
                {
                    _logListView.Rebuild();
                }
                else
                {
                    PopulateManualLauncherHistory();
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

            _logListItems.Clear();
            for (int i = _launcherHistoryEntries.Count - 1; i >= 0; --i)
            {
                CommandHistoryEntry entry = _launcherHistoryEntries[i];
                _logListItems.Add(
                    new LogItem(TerminalLogType.Input, entry.Text, string.Empty)
                );
            }

            if (_logListView != null)
            {
                if (_logListView.itemsSource != _logListItems)
                {
                    _logListView.itemsSource = _logListItems;
                }

                _logListView.Rebuild();
            }
            else
            {
                PopulateManualLauncherHistory();
            }
            _lastRenderedLauncherHistoryVersion = historyVersion;
            _cachedLauncherScrollVersion = historyVersion;
            _cachedLauncherScrollValue = 0f;
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

        private void PopulateManualLauncherHistory()
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

            container.Clear();
            container.style.justifyContent = Justify.FlexEnd;

            int totalCount = _logListItems.Count;
            for (int i = 0; i < totalCount; ++i)
            {
                LogItem logItem = _logListItems[i];
                VisualElement element = CreateLogListItem();
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
                element.style.opacity = ComputeLogOpacity(i, totalCount);
                container.Add(element);
            }

            if (totalCount > 0)
            {
                _launcherHistoryContentHeight = totalCount * LauncherEstimatedHistoryRowHeight;
            }
            else
            {
                _launcherHistoryContentHeight = 0f;
            }
        }

    }
}
