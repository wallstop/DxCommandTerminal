namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.UIElements;

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
                terminal.SetLauncherContentHeightsForTests(historyHeight: 260f, suggestionHeight: 0f);

                terminal.LogItemsForTests.Clear();
                terminal.LogItemsForTests.Add(new LogItem(TerminalLogType.Input, "entry", string.Empty));

                terminal.UpdateLauncherLayoutMetricsForTests();

                float expected = metrics.HistoryHeight;

                Assert.That(log.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(log.style.height.value, Is.EqualTo(expected).Within(0.001f));
                Assert.That(log.style.maxHeight.value, Is.EqualTo(expected).Within(0.001f));
                Assert.That(log.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));
                Assert.That(log.style.marginBottom.value, Is.EqualTo(0f).Within(0.001f));
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
                    Is.EqualTo(terminal.TerminalContainerForTests.style.paddingBottom.value).Within(0.001f)
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
                terminal.SetLauncherContentHeightsForTests(historyHeight: 64f, suggestionHeight: 0f);

                terminal.LogItemsForTests.Clear();
                terminal.LogItemsForTests.Add(
                    new LogItem(TerminalLogType.Input, "run-tests", string.Empty)
                );

                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(log.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(log.style.height.value, Is.GreaterThan(0f));
                Assert.That(log.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));
                Assert.That(autoComplete.style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(autoComplete.style.marginTop.value, Is.EqualTo(0f).Within(0.001f));

                terminal.LogItemsForTests.Clear();
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

                Assert.That(terminal.LogItemsForTests.Count, Is.EqualTo(3));
                Assert.That(terminal.LogItemsForTests[0].message, Is.EqualTo("third"));
                Assert.That(terminal.LogItemsForTests[0].type, Is.EqualTo(TerminalLogType.Input));
                Assert.That(terminal.LogItemsForTests[1].message, Is.EqualTo("second"));
                Assert.That(terminal.LogItemsForTests[2].message, Is.EqualTo("first"));
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
                terminal.SetLauncherContentHeightsForTests(historyHeight: 100f, suggestionHeight: 40f);

                terminal.CompletionBufferForTests.Clear();
                terminal.CompletionBufferForTests.Add("help");
                terminal.RefreshAutoCompleteHintsForTests();

                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(autoComplete.style.marginTop.value, Is.EqualTo(TerminalUI.LauncherAutoCompleteSpacingForTests * 0.5f).Within(0.001f));
                Assert.That(log.style.marginTop.value, Is.EqualTo(TerminalUI.LauncherAutoCompleteSpacingForTests * 0.5f).Within(0.001f));
                Assert.That(log.style.marginBottom.value, Is.EqualTo(0f).Within(0.001f));

                terminal.CompletionBufferForTests.Clear();
                terminal.RefreshAutoCompleteHintsForTests();
                terminal.SetLauncherContentHeightsForTests(historyHeight: 100f, suggestionHeight: 0f);
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

        [Test]
        public void LogEmptyLabelHidden()
        {
            ListView listView = new ListView();
            TerminalUI.ConfigureEmptyLabelForTests(listView);

            Label emptyLabel = new Label("List is empty") { name = "unity-list-view__empty-label" };
            listView.Add(emptyLabel);
            TerminalUI.ConfigureEmptyLabelForTests(listView);
            Assert.That(emptyLabel.text, Is.Empty);
            Assert.That(emptyLabel.style.display.value, Is.EqualTo(DisplayStyle.None));
        }
    }
}
