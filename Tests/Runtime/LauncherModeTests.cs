namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    public sealed class LauncherModeTests
    {
        [Test]
        public void LauncherMetricsRespectSizingModes()
        {
            var settings = new TerminalLauncherSettings
            {
                width = LauncherDimension.RelativeToScreen(0.5f),
                height = LauncherDimension.RelativeToScreen(0.33f),
                historyHeight = LauncherDimension.RelativeToLauncher(0.5f),
                minimumWidth = 300f,
                minimumHeight = 120f,
                screenPadding = 40f,
                historyVisibleEntryCount = 4,
                historyFadeExponent = 2f,
            };

            LauncherLayoutMetrics metrics = settings.ComputeMetrics(1920, 1080);

            Assert.That(metrics.Width, Is.EqualTo(960f).Within(0.001f));
            Assert.That(metrics.Height, Is.EqualTo(194.4f).Within(0.001f));
            Assert.That(metrics.Left, Is.EqualTo(480f).Within(0.001f));
            Assert.That(metrics.Top, Is.EqualTo(442.8f).Within(0.5f));
            Assert.That(metrics.HistoryHeight, Is.EqualTo(metrics.Height * 0.5f).Within(0.001f));
            Assert.That(metrics.HistoryVisibleEntryCount, Is.EqualTo(4));
            Assert.That(metrics.HistoryFadeExponent, Is.EqualTo(2f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator ToggleLauncherTogglesState()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);
            Assert.That(terminal.IsClosed, Is.True);

            terminal.ToggleLauncher();
            yield return TestSceneHelpers.WaitFrames(2);

            Assert.That(terminal.CurrentStateForTests, Is.EqualTo(TerminalState.OpenLauncher));
            Assert.That(terminal.IsClosed, Is.False);

            terminal.ToggleLauncher();
            yield return TestSceneHelpers.WaitFrames(2);

            Assert.That(terminal.CurrentStateForTests, Is.EqualTo(TerminalState.Closed));
            Assert.That(terminal.IsClosed, Is.True);
        }

        [UnityTest]
        public IEnumerator RefreshLauncherHistoryProducesFadedEntries()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            CommandHistory previousHistory = Terminal.History;
            var history = new CommandHistory(16);
            Terminal.History = history;

            try
            {
                history.Push("first", true, true);
                history.Push("second", true, true);
                history.Push("third", true, true);

                var metrics = new LauncherLayoutMetrics(
                    width: 640f,
                    height: 160f,
                    left: 100f,
                    top: 200f,
                    historyHeight: 120f,
                    cornerRadius: 14f,
                    insetPadding: 12f,
                    historyVisibleEntryCount: 3,
                    historyFadeExponent: 2f,
                    snapOpen: true,
                    animationDuration: 0.1f
                );

                var scroll = new ScrollView();
                terminal.SetLogScrollViewForTests(scroll);
                terminal.SetLauncherMetricsForTests(metrics);
                terminal.SetState(TerminalState.OpenLauncher);
                terminal.RefreshLauncherHistoryForTests();

                VisualElement content = terminal.LogScrollViewForTests.contentContainer;
                Assert.That(content.childCount, Is.EqualTo(3));

                // Verify newest entry is first and fully opaque
                var newest = content[0] as Label;
                Assert.IsNotNull(newest);
                Assert.That(newest!.text, Is.EqualTo("third"));
                Assert.That(newest.style.opacity.value, Is.EqualTo(1f).Within(0.001f));

                // Middle entry has partial opacity
                var middle = content[1] as Label;
                Assert.IsNotNull(middle);
                Assert.That(middle!.text, Is.EqualTo("second"));
                Assert.That(middle.style.opacity.value, Is.LessThan(1f).And.GreaterThan(0f));

                // Oldest entry is faded out
                var oldest = content[2] as Label;
                Assert.IsNotNull(oldest);
                Assert.That(oldest!.text, Is.EqualTo("first"));
                Assert.That(oldest.style.opacity.value, Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                Terminal.History = previousHistory;
            }

            yield return TestSceneHelpers.DestroyTerminalAndWait();
        }

        [UnityTest]
        public IEnumerator LauncherResetMaintainsDynamicTargetHeight()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            terminal.ToggleLauncher();
            yield return TestSceneHelpers.WaitFrames(2);

            Assert.That(terminal.CurrentStateForTests, Is.EqualTo(TerminalState.OpenLauncher));
            Assert.That(terminal.LauncherMetricsInitializedForTests, Is.True);

            float launcherMaxHeight = terminal.LauncherMetricsForTests.Height;
            Assert.That(launcherMaxHeight, Is.GreaterThan(0f));

            float reducedTarget = Mathf.Max(60f, launcherMaxHeight * 0.25f);
            terminal.SetWindowHeightsForTests(reducedTarget, reducedTarget);

            Assert.That(
                terminal.TargetWindowHeightForTests,
                Is.EqualTo(reducedTarget).Within(0.001f)
            );

            terminal.ResetWindowForTests();

            Assert.That(
                terminal.TargetWindowHeightForTests,
                Is.EqualTo(reducedTarget).Within(0.001f)
            );

            float excessiveTarget = launcherMaxHeight * 1.5f;
            terminal.SetWindowHeightsForTests(excessiveTarget, excessiveTarget);

            terminal.ResetWindowForTests();

            Assert.That(
                terminal.TargetWindowHeightForTests,
                Is.EqualTo(terminal.LauncherMetricsForTests.Height).Within(0.001f)
            );

            yield return TestSceneHelpers.DestroyTerminalAndWait();
        }
    }
}
