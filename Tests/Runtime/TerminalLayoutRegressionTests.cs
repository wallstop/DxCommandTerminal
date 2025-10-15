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
                    new LogItem(TerminalLogType.Message, "run-tests", string.Empty)
                );

                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(log.style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(log.style.height.value, Is.GreaterThan(0f));

                terminal.LogItemsForTests.Clear();
                terminal.UpdateLauncherLayoutMetricsForTests();

                Assert.That(log.style.display.value, Is.EqualTo(DisplayStyle.None));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
