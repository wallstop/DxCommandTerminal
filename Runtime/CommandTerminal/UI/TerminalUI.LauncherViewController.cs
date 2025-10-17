namespace WallstopStudios.DxCommandTerminal.UI
{
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed partial class TerminalUI
    {
        private sealed class LauncherViewController
        {
            private readonly TerminalUI _owner;

            internal LauncherViewController(TerminalUI owner)
            {
                _owner = owner;
            }

            internal void ConfigureForStandardMode()
            {
                _owner._historyListAdapter?.SetJustification(Justify.FlexEnd);
                ClearFade();
            }

            internal void ConfigureForLauncherMode()
            {
                _owner._historyListAdapter?.SetJustification(Justify.FlexStart);
            }

            internal void ClampScroll()
            {
                ScrollView scrollView = _owner._logScrollView;
                if (scrollView == null)
                {
                    return;
                }

                Scroller scroller = scrollView.verticalScroller;
                if (scroller == null)
                {
                    return;
                }

                float clamped = Mathf.Clamp(scroller.value, scroller.lowValue, scroller.highValue);
                if (!Mathf.Approximately(clamped, scroller.value))
                {
                    scroller.value = clamped;
                }
            }

            internal void UpdateFade()
            {
                if (
                    !CanFade(
                        out ScrollView scrollView,
                        out VisualElement viewport,
                        out VisualElement historyContent
                    )
                )
                {
                    ClearFade();
                    return;
                }

                Rect viewportWorld = viewport.worldBound;
                float viewportHeight = viewportWorld.height;
                if (viewportHeight <= 0.01f)
                {
                    return;
                }

                float viewportTop = viewportWorld.yMin;
                float viewportBottom = viewportWorld.yMax;

                int childCount = historyContent.childCount;
                for (int i = 0; i < childCount; ++i)
                {
                    VisualElement entry = historyContent[i];
                    if (entry == null || entry.resolvedStyle.display == DisplayStyle.None)
                    {
                        continue;
                    }

                    Rect entryBounds = entry.worldBound;
                    float entryHeight = entryBounds.height;
                    if (entryHeight <= 0.01f)
                    {
                        ApplyOpacity(entry, 1f);
                        continue;
                    }

                    float overlapHeight =
                        Mathf.Min(entryBounds.yMax, viewportBottom)
                        - Mathf.Max(entryBounds.yMin, viewportTop);
                    if (overlapHeight <= 0f)
                    {
                        ApplyOpacity(entry, 1f);
                        continue;
                    }

                    float entryCenter = (entryBounds.yMin + entryBounds.yMax) * 0.5f;
                    float clampedCenter = Mathf.Clamp(entryCenter, viewportTop, viewportBottom);
                    float normalized = Mathf.Clamp01(
                        (clampedCenter - viewportTop) / viewportHeight
                    );

                    float opacity = ComputeOpacity(normalized);

                    ApplyOpacity(entry, opacity);
                }
            }

            internal float ComputeOpacityForTests(float normalized)
            {
                return ComputeOpacity(normalized);
            }

            private float ComputeOpacity(float normalized)
            {
                float rangeFactor = Mathf.Clamp01(_owner.GetHistoryFadeRangeFactor());
                float exponent = Mathf.Max(0.01f, _owner.GetHistoryFadeExponent());
                float minimumOpacity = Mathf.Clamp01(_owner.GetHistoryFadeMinimumOpacity());

                float adjusted = Mathf.Pow(normalized * rangeFactor, exponent);
                return Mathf.Lerp(1f, minimumOpacity, adjusted);
            }

            internal void ScheduleFade()
            {
                ScrollView scrollView = _owner._logScrollView;
                if (scrollView == null)
                {
                    return;
                }

                scrollView.schedule.Execute(UpdateFade).ExecuteLater(0);
            }

            internal void HandleScrollValueChanged(float value)
            {
                if (!_owner.IsLauncherActive || !_owner._launcherMetricsInitialized)
                {
                    return;
                }

                ClampScroll();
                UpdateFade();
                ScheduleFade();
            }

            internal void ClearFade()
            {
                ScrollView scrollView = _owner._logScrollView;
                if (scrollView == null)
                {
                    return;
                }

                VisualElement historyContent = scrollView.contentContainer;
                if (historyContent == null)
                {
                    return;
                }

                int childCount = historyContent.childCount;
                for (int i = 0; i < childCount; ++i)
                {
                    VisualElement entry = historyContent[i];
                    ApplyOpacity(entry, 1f);
                }
            }

            private bool CanFade(
                out ScrollView scrollView,
                out VisualElement viewport,
                out VisualElement historyContent
            )
            {
                scrollView = _owner._logScrollView;
                viewport = scrollView?.contentViewport;
                historyContent = scrollView?.contentContainer;
                return _owner.IsLauncherActive
                    && _owner._launcherMetricsInitialized
                    && scrollView != null
                    && viewport != null
                    && historyContent != null;
            }

            private static void ApplyOpacity(VisualElement entry, float opacity)
            {
                if (entry == null)
                {
                    return;
                }

                entry.style.opacity = opacity;

                Label label = entry as Label ?? entry.Q<Label>(className: "terminal-output-label");
                if (label != null)
                {
                    label.style.opacity = opacity;
                }
            }
        }
    }
}
