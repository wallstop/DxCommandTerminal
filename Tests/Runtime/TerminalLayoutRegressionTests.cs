namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DxCommandTerminal.Tests.Runtime.Components;

    public sealed class TerminalLayoutRegressionTests
    {
        [Test]
        public void AutoCompleteContainerCollapsesWhenHintsCleared()
        {
            GameObject go = new GameObject("AutoCompleteRegressionTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            terminal.makeHintsClickable = false;
            go.SetActive(true);

            try
            {
                ScrollView autoComplete = new ScrollView();
                terminal.InjectAutoCompleteContainerForTests(autoComplete);
                terminal.SetHintDisplayModeForTests(HintDisplayMode.Always);

                terminal.CompletionBufferForTests.Clear();
                terminal.CompletionBufferForTests.Add("help");
                terminal.RefreshAutoCompleteHintsForTests();

                Assert.That(autoComplete.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(autoComplete.contentContainer.childCount, Is.EqualTo(1));

                terminal.CompletionBufferForTests.Clear();
                terminal.RefreshAutoCompleteHintsForTests();

                Assert.That(autoComplete.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(autoComplete.contentContainer.childCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void StandardLayoutPlacesInputAtBottom()
        {
            GameObject go = new GameObject("StandardLayoutRegressionTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                terminalContainer.style.paddingTop = 10f;
                terminalContainer.style.paddingBottom = 5f;
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();

                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );
                terminal.SetWindowHeightsForTests(200f, 200f);
                terminal.ConfigureStandardLayoutForTests(800f);

                Assert.That(terminalContainer.childCount, Is.EqualTo(3));
                Assert.That(terminalContainer[0], Is.SameAs(log));
                Assert.That(terminalContainer[1], Is.SameAs(autoComplete));
                Assert.That(terminalContainer[2], Is.SameAs(inputContainer));

                Assert.That(log.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(log.style.flexGrow.value, Is.EqualTo(1f).Within(0.001f));
                Assert.That(log.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));
                Assert.That(log.style.marginBottom.value, Is.EqualTo(0f).Within(0.001f));

                float expectedContainerHeight =
                    LayoutMeasurementUtility.ComputeStandardContainerHeight(
                        200f,
                        paddingTop: 10f,
                        paddingBottom: 5f
                    );

                Assert.That(
                    terminal.TerminalContainerForTests.style.height.value,
                    Is.EqualTo(expectedContainerHeight).Within(0.001f)
                );
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LauncherHistoryUsesAvailableHeight()
        {
            GameObject go = new GameObject("LauncherHistoryHeightTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();
                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );
                terminal.ArrangeLauncherVisualHierarchyForTests();
                terminal.ForceStateForTests(TerminalState.OpenLauncher);

                LauncherLayoutMetrics metrics = new LauncherLayoutMetrics(
                    width: 640f,
                    height: 260f,
                    left: 0f,
                    top: 0f,
                    historyHeight: 180f,
                    cornerRadius: 12f,
                    insetPadding: 12f,
                    historyVisibleEntryCount: 6,
                    historyFadeExponent: 2f,
                    snapOpen: true,
                    animationDuration: 0.12f
                );

                terminal.SetLauncherMetricsForTests(metrics);
                terminal.SetWindowHeightsForTests(metrics.Height, metrics.Height);
                terminal.SetLauncherContentHeightsForTests(
                    historyHeight: 260f,
                    suggestionHeight: 0f
                );

                TestRuntimeScope.LogItemsForTests.Clear();
                TestRuntimeScope.LogItemsForTests.Add(
                    new LogItem(TerminalLogType.Input, "entry", string.Empty)
                );

                terminal.UpdateLauncherLayoutMetricsForTests();

                float expected = metrics.HistoryHeight;

                Assert.That(log.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(log.style.height.value, Is.EqualTo(expected).Within(0.001f));
                Assert.That(log.style.maxHeight.value, Is.EqualTo(expected).Within(0.001f));
                Assert.That(log.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));
                Assert.That(log.style.marginBottom.value, Is.EqualTo(0f).Within(0.001f));

                TestRuntimeScope.LogItemsForTests.Add(
                    new LogItem(TerminalLogType.Input, "entry-2", string.Empty)
                );
                terminal.RefreshLauncherHistoryForTests();
                terminal.SetLauncherContentHeightsForTests(
                    historyHeight: metrics.HistoryHeight,
                    suggestionHeight: 0f
                );
                terminal.SetWindowHeightsForTests(metrics.Height, metrics.Height);
                terminal.UpdateLauncherLayoutMetricsForTests();

                float heightAllowance = metrics.Height + 0.001f;
                Assert.That(
                    terminal.TargetWindowHeightForTests,
                    Is.LessThanOrEqualTo(heightAllowance)
                );
                Assert.That(
                    log.style.height.value,
                    Is.EqualTo(metrics.HistoryHeight).Within(0.001f)
                );
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LauncherLayoutSnapshotReflectsHistoryAllocation()
        {
            GameObject go = new GameObject("LauncherSnapshotTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            List<TerminalUI.LauncherLayoutSnapshot> snapshots =
                new List<TerminalUI.LauncherLayoutSnapshot>();

            void CaptureSnapshot(TerminalUI.LauncherLayoutSnapshot snapshot)
            {
                snapshots.Add(snapshot);
            }

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();

                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );
                terminal.ArrangeLauncherVisualHierarchyForTests();
                terminal.ForceStateForTests(TerminalState.OpenLauncher);

                LauncherLayoutMetrics metrics = new LauncherLayoutMetrics(
                    width: 620f,
                    height: 240f,
                    left: 0f,
                    top: 0f,
                    historyHeight: 160f,
                    cornerRadius: 10f,
                    insetPadding: 12f,
                    historyVisibleEntryCount: 2,
                    historyFadeExponent: 2f,
                    snapOpen: true,
                    animationDuration: 0.1f
                );

                terminal.SetLauncherMetricsForTests(metrics);
                terminal.SetWindowHeightsForTests(metrics.Height, metrics.Height);

                TestRuntimeScope.LogItemsForTests.Clear();
                TestRuntimeScope.LogItemsForTests.Add(
                    new LogItem(TerminalLogType.Input, "first", string.Empty)
                );
                TestRuntimeScope.LogItemsForTests.Add(
                    new LogItem(TerminalLogType.Input, "second", string.Empty)
                );
                TestRuntimeScope.LogItemsForTests.Add(
                    new LogItem(TerminalLogType.Input, "third", string.Empty)
                );
                TestRuntimeScope.LogItemsForTests.Add(
                    new LogItem(TerminalLogType.Input, "fourth", string.Empty)
                );

                terminal.RefreshLauncherHistoryForTests();
                terminal.SetLauncherContentHeightsForTests(historyHeight: 0f, suggestionHeight: 0f);

                TerminalUI.LauncherLayoutComputed += CaptureSnapshot;

                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(snapshots.Count, Is.GreaterThan(0));

                TerminalUI.LauncherLayoutSnapshot snapshot = snapshots[snapshots.Count - 1];
                Assert.That(snapshot.VisibleHistoryCount, Is.EqualTo(2));

                float estimatedHistoryHeight =
                    snapshot.VisibleHistoryCount
                    * TerminalUI.LauncherEstimatedHistoryRowHeightForTests;
                float expectedHistoryHeight = Mathf.Min(
                    metrics.HistoryHeight,
                    estimatedHistoryHeight
                );

                Assert.That(
                    snapshot.HistoryRowHeightEstimate,
                    Is.EqualTo(TerminalUI.LauncherEstimatedHistoryRowHeightForTests).Within(0.001f)
                );
                Assert.That(
                    snapshot.HistoryTargetHeight,
                    Is.EqualTo(expectedHistoryHeight).Within(0.001f)
                );
                Assert.That(snapshot.ClampedHeight, Is.LessThanOrEqualTo(metrics.Height + 0.001f));
                Assert.That(
                    snapshot.AvailableHistoryHeight,
                    Is.LessThanOrEqualTo(metrics.HistoryHeight + 0.001f)
                );
                Assert.That(
                    terminal.TargetWindowHeightForTests,
                    Is.EqualTo(snapshot.ClampedHeight).Within(0.001f)
                );
            }
            finally
            {
                TerminalUI.LauncherLayoutComputed -= CaptureSnapshot;
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void HistoryAdapterSwitchesJustificationBetweenModes()
        {
            GameObject go = new GameObject("HistoryAdapterAlignmentTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();

                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );

                terminal.ConfigureStandardLayoutForTests(800f);

                Assert.That(
                    log.contentContainer.style.justifyContent.value,
                    Is.EqualTo(Justify.FlexEnd)
                );

                terminal.ArrangeLauncherVisualHierarchyForTests();
                terminal.ForceStateForTests(TerminalState.OpenLauncher);

                LauncherLayoutMetrics metrics = new LauncherLayoutMetrics(
                    width: 600f,
                    height: 300f,
                    left: 100f,
                    top: 100f,
                    historyHeight: 200f,
                    cornerRadius: 10f,
                    insetPadding: 12f,
                    historyVisibleEntryCount: 4,
                    historyFadeExponent: 2f,
                    snapOpen: true,
                    animationDuration: 0.12f
                );

                terminal.SetLauncherMetricsForTests(metrics);
                terminal.SetWindowHeightsForTests(metrics.Height, metrics.Height);
                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(
                    log.contentContainer.style.justifyContent.value,
                    Is.EqualTo(Justify.FlexStart)
                );
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [UnityTest]
        public IEnumerator StandardScrollRestoresAfterReopen()
        {
            yield return TerminalTests.SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.disableUIForTests = true,
                ensureLargeLogBuffer: true
            );

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            VisualElement terminalContainer = new VisualElement();
            VisualElement inputContainer = new VisualElement();
            ScrollView autoComplete = new ScrollView(ScrollViewMode.Horizontal);
            ScrollView logScroll = new ScrollView();

            terminal.InjectLayoutElementsForTests(
                terminalContainer,
                inputContainer,
                autoComplete,
                logScroll
            );

            terminal.SetState(TerminalState.OpenFull);

            CommandLog logBuffer = terminal.Runtime.Log;
            logBuffer.HandleLog("first message", TerminalLogType.Message, includeStackTrace: false);
            logBuffer.HandleLog(
                "second message",
                TerminalLogType.Message,
                includeStackTrace: false
            );
            logBuffer.HandleLog("third message", TerminalLogType.Message, includeStackTrace: false);

            terminal.RefreshUIForTests();

            Scroller scroller = logScroll.verticalScroller;
            Assert.IsNotNull(scroller);
            scroller.highValue = 200f;
            scroller.value = 80f;
            logScroll.scrollOffset = new Vector2(0f, 80f);

            terminal.SetState(TerminalState.Closed);
            terminal.RefreshUIForTests();

            terminal.SetState(TerminalState.OpenFull);
            terminal.RefreshUIForTests();
            yield return TestSceneHelpers.WaitFrames(1);

            scroller = logScroll.verticalScroller;
            Assert.IsNotNull(scroller);
            Assert.That(scroller.value, Is.EqualTo(80f).Within(0.001f));

            yield return TestSceneHelpers.DestroyTerminalAndWait();
        }

        [UnityTest]
        public IEnumerator StandardScrollStaysPutWhenNewLogsArrive()
        {
            yield return TerminalTests.SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.disableUIForTests = true,
                ensureLargeLogBuffer: true
            );

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            VisualElement terminalContainer = new VisualElement();
            VisualElement inputContainer = new VisualElement();
            ScrollView autoComplete = new ScrollView(ScrollViewMode.Horizontal);
            ScrollView logScroll = new ScrollView();

            terminal.InjectLayoutElementsForTests(
                terminalContainer,
                inputContainer,
                autoComplete,
                logScroll
            );

            terminal.SetState(TerminalState.OpenFull);

            CommandLog logBuffer = terminal.Runtime.Log;
            for (int i = 0; i < 5; ++i)
            {
                logBuffer.HandleLog(
                    $"initial-message-{i}",
                    TerminalLogType.Message,
                    includeStackTrace: false
                );
            }

            terminal.RefreshUIForTests();
            yield return null;

            Scroller scroller = logScroll.verticalScroller;
            Assert.IsNotNull(scroller);
            scroller.highValue = 200f;
            scroller.value = 80f;
            logScroll.scrollOffset = new Vector2(0f, 80f);

            logBuffer.HandleLog("new-message", TerminalLogType.Message, includeStackTrace: false);

            terminal.RefreshUIForTests();
            yield return null;

            scroller = logScroll.verticalScroller;
            Assert.IsNotNull(scroller);
            Assert.That(scroller.value, Is.EqualTo(80f).Within(0.001f));

            yield return TestSceneHelpers.DestroyTerminalAndWait();
        }

        [Test]
        public void LauncherOpacityRespectsFadeCurve()
        {
            GameObject go = new GameObject("LauncherOpacityTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                terminal.ForceStateForTests(TerminalState.OpenLauncher);
                LauncherLayoutMetrics metrics = new LauncherLayoutMetrics(
                    width: 580f,
                    height: 300f,
                    left: 0f,
                    top: 0f,
                    historyHeight: 220f,
                    cornerRadius: 12f,
                    insetPadding: 12f,
                    historyVisibleEntryCount: 4,
                    historyFadeExponent: 2f,
                    snapOpen: true,
                    animationDuration: 0.1f
                );

                terminal.SetLauncherMetricsForTests(metrics);

                float fullyVisible = terminal.ComputeLauncherOpacityForTests(0f);
                float midFade = terminal.ComputeLauncherOpacityForTests(0.5f);
                float fullyFaded = terminal.ComputeLauncherOpacityForTests(1f);

                Assert.That(fullyVisible, Is.EqualTo(1f).Within(0.001f));
                Assert.That(midFade, Is.GreaterThan(fullyFaded));
                Assert.That(midFade, Is.LessThan(fullyVisible));
                Assert.That(
                    fullyFaded,
                    Is.EqualTo(terminal.LauncherFadeMinimumForTests).Within(0.001f)
                );
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LauncherSpacingResetsWhenSuggestionsDisappear()
        {
            GameObject go = new GameObject("LauncherSpacingTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();

                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );
                terminal.ArrangeLauncherVisualHierarchyForTests();
                terminal.ForceStateForTests(TerminalState.OpenLauncher);

                LauncherLayoutMetrics metrics = new LauncherLayoutMetrics(
                    width: 620f,
                    height: 240f,
                    left: 0f,
                    top: 0f,
                    historyHeight: 160f,
                    cornerRadius: 12f,
                    insetPadding: 12f,
                    historyVisibleEntryCount: 4,
                    historyFadeExponent: 2f,
                    snapOpen: true,
                    animationDuration: 0.12f
                );

                terminal.SetLauncherMetricsForTests(metrics);
                terminal.SetWindowHeightsForTests(metrics.Height, metrics.Height);

                VisualElement historyEntry = new VisualElement();
                historyEntry.style.display = DisplayStyle.Flex;
                log.contentContainer.Add(historyEntry);

                VisualElement suggestion = new VisualElement();
                suggestion.style.display = DisplayStyle.Flex;
                TestRuntimeScope.AutoCompleteContainerForTests.contentContainer.Add(suggestion);

                terminal.SetLauncherContentHeightsForTests(
                    historyHeight: metrics.HistoryHeight * 0.5f,
                    suggestionHeight: 24f
                );
                terminal.UpdateLauncherLayoutMetricsForTests();

                float marginWithSuggestions = log.contentContainer.style.marginTop.value.value;
                Assert.That(marginWithSuggestions, Is.GreaterThan(0f));

                TestRuntimeScope.AutoCompleteContainerForTests.contentContainer.Clear();
                terminal.SetLauncherContentHeightsForTests(
                    historyHeight: metrics.HistoryHeight * 0.5f,
                    suggestionHeight: 0f
                );
                terminal.UpdateLauncherLayoutMetricsForTests();

                float marginWithoutSuggestions = log.contentContainer.style.marginTop.value.value;
                Assert.That(marginWithoutSuggestions, Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClosedTerminalHidesContainer()
        {
            GameObject go = new GameObject("TerminalVisibilityTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();
                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );

                terminal.ForceStateForTests(TerminalState.Closed);
                terminal.SetWindowHeightsForTests(0f, 0f);
                terminal.UpdateTerminalVisibilityForTests();

                Assert.That(
                    terminal.TerminalContainerForTests.style.display.value,
                    Is.EqualTo(DisplayStyle.None)
                );

                terminal.ForceStateForTests(TerminalState.OpenSmall);
                terminal.SetWindowHeightsForTests(200f, 200f);
                terminal.UpdateTerminalVisibilityForTests();

                Assert.That(
                    terminal.TerminalContainerForTests.style.display.value,
                    Is.EqualTo(DisplayStyle.Flex)
                );
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LauncherPaddingIsSymmetric()
        {
            GameObject go = new GameObject("LauncherPaddingTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();
                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );
                terminal.ForceStateForTests(TerminalState.OpenLauncher);

                LauncherLayoutMetrics metrics = new LauncherLayoutMetrics(
                    width: 640f,
                    height: 260f,
                    left: 0f,
                    top: 0f,
                    historyHeight: 160f,
                    cornerRadius: 12f,
                    insetPadding: 12f,
                    historyVisibleEntryCount: 4,
                    historyFadeExponent: 2f,
                    snapOpen: true,
                    animationDuration: 0.12f
                );

                terminal.SetLauncherMetricsForTests(metrics);
                terminal.SetWindowHeightsForTests(metrics.Height, metrics.Height);
                terminal.ApplyLauncherLayoutForTests(metrics.Width, metrics.Height);

                Assert.That(
                    terminal.TerminalContainerForTests.style.paddingTop.value,
                    Is.EqualTo(terminal.TerminalContainerForTests.style.paddingBottom.value)
                        .Within(0.001f)
                );
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LauncherHistoryRemainsVisibleWhenItemsExist()
        {
            GameObject go = new GameObject("LauncherHistoryRegressionTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();
                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );
                terminal.ArrangeLauncherVisualHierarchyForTests();

                Assert.That(terminalContainer.childCount, Is.EqualTo(3));
                Assert.That(terminalContainer[0], Is.SameAs(inputContainer));
                Assert.That(terminalContainer[1], Is.SameAs(autoComplete));
                Assert.That(terminalContainer[2], Is.SameAs(log));

                terminal.ForceStateForTests(TerminalState.OpenLauncher);
                terminal.SetLauncherMetricsForTests(
                    new LauncherLayoutMetrics(
                        width: 640f,
                        height: 240f,
                        left: 0f,
                        top: 0f,
                        historyHeight: 160f,
                        cornerRadius: 12f,
                        insetPadding: 12f,
                        historyVisibleEntryCount: 4,
                        historyFadeExponent: 2f,
                        snapOpen: true,
                        animationDuration: 0.12f
                    )
                );

                terminal.SetWindowHeightsForTests(200f, 200f);
                terminal.SetLauncherContentHeightsForTests(
                    historyHeight: 64f,
                    suggestionHeight: 0f
                );

                TestRuntimeScope.LogItemsForTests.Clear();
                TestRuntimeScope.LogItemsForTests.Add(
                    new LogItem(TerminalLogType.Input, "run-tests", string.Empty)
                );

                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(log.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(log.style.height.value, Is.GreaterThan(0f));
                Assert.That(log.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));
                Assert.That(autoComplete.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(autoComplete.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));

                TestRuntimeScope.LogItemsForTests.Clear();
                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(log.style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LauncherHistoryNewestEntryAppearsNearInput()
        {
            GameObject go = new GameObject("LauncherHistoryOrderTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();
                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );
                terminal.ArrangeLauncherVisualHierarchyForTests();

                terminal.ForceStateForTests(TerminalState.OpenLauncher);
                terminal.SetLauncherMetricsForTests(
                    new LauncherLayoutMetrics(
                        width: 640f,
                        height: 240f,
                        left: 0f,
                        top: 0f,
                        historyHeight: 160f,
                        cornerRadius: 12f,
                        insetPadding: 12f,
                        historyVisibleEntryCount: 4,
                        historyFadeExponent: 2f,
                        snapOpen: true,
                        animationDuration: 0.12f
                    )
                );

                CommandHistory history = terminal.Runtime.History;
                Assert.IsNotNull(history);
                history.Push("first", true, true);
                history.Push("second", true, true);
                history.Push("third", true, true);

                terminal.RefreshLauncherHistoryForTests();

                Assert.That(TestRuntimeScope.LogItemsForTests.Count, Is.EqualTo(3));
                Assert.That(TestRuntimeScope.LogItemsForTests[0].message, Is.EqualTo("third"));
                Assert.That(
                    TestRuntimeScope.LogItemsForTests[0].type,
                    Is.EqualTo(TerminalLogType.Input)
                );
                Assert.That(TestRuntimeScope.LogItemsForTests[1].message, Is.EqualTo("second"));
                Assert.That(TestRuntimeScope.LogItemsForTests[2].message, Is.EqualTo("first"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void LauncherSuggestionsUseTightSpacing()
        {
            GameObject go = new GameObject("LauncherSuggestionSpacingTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                VisualElement terminalContainer = new VisualElement();
                VisualElement inputContainer = new VisualElement();
                ScrollView autoComplete = new ScrollView();
                ScrollView log = new ScrollView();
                terminal.InjectLayoutElementsForTests(
                    terminalContainer,
                    inputContainer,
                    autoComplete,
                    log
                );
                terminal.ArrangeLauncherVisualHierarchyForTests();
                terminal.ForceStateForTests(TerminalState.OpenLauncher);

                LauncherLayoutMetrics metrics = new LauncherLayoutMetrics(
                    width: 640f,
                    height: 240f,
                    left: 0f,
                    top: 0f,
                    historyHeight: 160f,
                    cornerRadius: 12f,
                    insetPadding: 12f,
                    historyVisibleEntryCount: 4,
                    historyFadeExponent: 2f,
                    snapOpen: true,
                    animationDuration: 0.12f
                );

                terminal.SetLauncherMetricsForTests(metrics);
                terminal.SetWindowHeightsForTests(metrics.Height, metrics.Height);
                terminal.SetLauncherContentHeightsForTests(
                    historyHeight: 100f,
                    suggestionHeight: 40f
                );

                terminal.CompletionBufferForTests.Clear();
                terminal.CompletionBufferForTests.Add("help");
                terminal.RefreshAutoCompleteHintsForTests();

                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(
                    autoComplete.style.marginTop.value,
                    Is.EqualTo(TerminalUI.LauncherAutoCompleteSpacingForTests * 0.5f).Within(0.001f)
                );
                Assert.That(
                    log.style.marginTop.value,
                    Is.EqualTo(TerminalUI.LauncherAutoCompleteSpacingForTests * 0.5f).Within(0.001f)
                );
                Assert.That(log.style.marginBottom.value, Is.EqualTo(0f).Within(0.001f));

                terminal.CompletionBufferForTests.Clear();
                terminal.RefreshAutoCompleteHintsForTests();
                terminal.SetLauncherContentHeightsForTests(
                    historyHeight: 100f,
                    suggestionHeight: 0f
                );
                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(autoComplete.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(autoComplete.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));
                Assert.That(log.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
