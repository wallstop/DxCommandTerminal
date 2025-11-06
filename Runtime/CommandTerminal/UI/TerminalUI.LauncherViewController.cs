namespace WallstopStudios.DxCommandTerminal.UI
{
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed partial class TerminalUI
    {
        private sealed class LauncherViewController
        {
            private readonly TerminalUI _owner;
            private const float FadeMomentumWindowSeconds = 0.35f;
            private IVisualElementScheduledItem _fadeMomentumSchedule;
            private float _fadeMomentumExpiry;

            internal LauncherViewController(TerminalUI owner)
            {
                _owner = owner;
            }

            internal void ConfigureForStandardMode()
            {
                _owner.SetHistoryJustification(Justify.FlexEnd);
                ClearFade();
            }

            internal void ConfigureForLauncherMode()
            {
                _owner.SetHistoryJustification(Justify.FlexStart);
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
                    !_owner.TryGetLauncherFadeContext(
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

                Scroller scroller = scrollView.verticalScroller;
                float scrollerHigh = scroller != null ? scroller.highValue : 0f;
                _owner.LogFadeDiagnostic(
                    $"UpdateFade viewportHeight={viewportHeight:F3} childCount={historyContent.childCount} scrollerHigh={scrollerHigh:F3}"
                );
                _owner.LogLauncherDiagnostic(
                    $"UpdateFade viewport={viewportHeight:F3} scrollerHigh={scrollerHigh:F3} childCount={historyContent.childCount}"
                );

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

                _owner.LogLauncherDiagnostic("ScheduleFade requested");
                scrollView.schedule.Execute(UpdateFade).ExecuteLater(0);
                StartFadeMomentumMonitor(scrollView);
            }

            internal void HandleScrollValueChanged(float value)
            {
                if (
                    !_owner.TryGetLauncherFadeContext(
                        out ScrollView scrollView,
                        out VisualElement viewport,
                        out VisualElement historyContent
                    )
                )
                {
                    return;
                }

                _owner.LogFadeDiagnostic(
                    $"HandleScrollValueChanged value={value:F3} viewportHeight={viewport.resolvedStyle.height:F3} contentChildren={historyContent.childCount}"
                );
                Scroller scroller = scrollView.verticalScroller;
                float scrollerHigh = scroller != null ? scroller.highValue : 0f;
                _owner.LogLauncherDiagnostic(
                    $"HandleScrollValueChanged value={value:F3} scrollerHigh={scrollerHigh:F3}"
                );

                ClampScroll();
                UpdateFade();
                ScheduleFade();
            }

            internal void ClearFade()
            {
                StopFadeMomentumMonitor();

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

            private void StartFadeMomentumMonitor(ScrollView scrollView)
            {
                if (scrollView == null)
                {
                    return;
                }

                _fadeMomentumExpiry = Time.unscaledTime + FadeMomentumWindowSeconds;

                if (_fadeMomentumSchedule != null)
                {
                    return;
                }

                _owner.LogFadeDiagnostic("Starting fade momentum monitor");
                _fadeMomentumSchedule = scrollView.schedule.Execute(FadeMomentumTick).Every(16);
            }

            private void StopFadeMomentumMonitor()
            {
                if (_fadeMomentumSchedule == null)
                {
                    return;
                }

                _owner.LogFadeDiagnostic("Stopping fade momentum monitor");
                _fadeMomentumSchedule.Pause();
                _fadeMomentumSchedule = null;
            }

            private void FadeMomentumTick()
            {
                if (_fadeMomentumSchedule == null)
                {
                    return;
                }

                if (Time.unscaledTime >= _fadeMomentumExpiry || !_owner.IsLauncherActive)
                {
                    StopFadeMomentumMonitor();
                    return;
                }

                _owner.LogFadeDiagnostic("Fade momentum tick");
                UpdateFade();
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
