namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Pins the closed-state lifecycle contract of TerminalUI: the visual
        tree is built on the first open (not on enable), a fully closed
        terminal stops its per-frame UI work, and a re-enabled component
        rebuilds on the next open. The rig uses zero-duration open/close
        animations so the state settles on the next frame regardless of the
        editor's tick rate.
     */
    public sealed class TerminalUILifecycleTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private const int FrameBudget = 600;

        private PanelSettings _panelSettings;
        private GameObject _terminalObject;
        private TerminalUI _terminal;

        private static T LoadAsset<T>(string relativePath)
            where T : ScriptableObject
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
        }

        private static IEnumerator WaitForInputVisible(string message)
        {
            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && (
                    TerminalUI.Instance._commandInput == null
                    || TerminalUI.Instance._commandInput.resolvedStyle.display != DisplayStyle.Flex
                )
            )
            {
                yield return null;
            }

            Assert.IsNotNull(
                TerminalUI.Instance._commandInput,
                $"{message}: the command input must exist"
            );
            Assert.AreEqual(
                DisplayStyle.Flex,
                TerminalUI.Instance._commandInput.resolvedStyle.display,
                message
            );
        }

        [TearDown]
        public void TearDown()
        {
            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator EnableDoesNotBuildUiWhileClosed()
        {
            yield return SpawnTerminalWithDocument();

            Assert.AreEqual(
                0,
                _terminal._uiDocument.rootVisualElement.childCount,
                "A terminal that starts closed must not build its visual tree on enable"
            );
            Assert.IsNull(
                _terminal._commandInput,
                "The command input must not exist before the first open"
            );

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Opening the terminal builds and shows the input");

            Assert.AreEqual(
                1,
                _terminal._uiDocument.rootVisualElement.childCount,
                "The terminal root is attached after the first open"
            );
        }

        [UnityTest]
        public IEnumerator ClosedTerminalDefersLogSyncUntilOpen()
        {
            yield return SpawnTerminalWithDocument();

            _terminal.SetState(TerminalState.OpenFull);
            yield return null;

            _terminal.SetState(TerminalState.Closed);
            int frameBudget = 10;
            while (0 < frameBudget-- && !_terminal.IsClosed)
            {
                yield return null;
            }

            Assert.IsTrue(
                _terminal.IsClosed,
                "Sanity: the zero-duration close settles on the next frame"
            );

            Terminal.Log(TerminalLogType.Message, "while-closed");
            frameBudget = 10;
            while (0 < frameBudget--)
            {
                yield return null;
            }

            ScrollView logView = _terminal._uiDocument.rootVisualElement.Q<ScrollView>(
                "LogScrollView"
            );
            Assert.IsNotNull(logView, "Sanity: the log view exists after the open/close cycle");
            Assert.AreEqual(
                0,
                logView.contentContainer.childCount,
                "A fully closed terminal must not sync buffered logs every frame"
            );

            _terminal.SetState(TerminalState.OpenFull);
            frameBudget = FrameBudget;
            while (0 < frameBudget-- && logView.contentContainer.childCount == 0)
            {
                yield return null;
            }

            Assert.AreEqual(
                1,
                logView.contentContainer.childCount,
                "Opening the terminal syncs output logged while it was closed"
            );
            Label loggedLine = logView.contentContainer[0] as Label;
            Assert.AreEqual(
                "while-closed",
                loggedLine?.text,
                "The buffered message must survive the closed window"
            );
        }

        [UnityTest]
        public IEnumerator ReenableRebuildsUiOnNextOpen()
        {
            yield return SpawnTerminalWithDocument();

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Sanity: the terminal builds on the first open");

            /*
                Freeze the resolved font as the persisted one so the
                re-enable below re-runs SetupUI without new font setup logs
                (a stale null persisted font re-logs the pack resolution).
             */
            _terminal._persistedFont = _terminal.CurrentFont;

            _terminal.enabled = false;
            Assert.AreEqual(
                0,
                _terminal._uiDocument.rootVisualElement.childCount,
                "Disabling detaches the terminal tree"
            );

            _terminal.enabled = true;
            Assert.IsNull(
                _terminal._commandInput,
                "Re-enabling must not rebuild the visual tree while the terminal stays closed"
            );

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Reopening after re-enable rebuilds the tree");

            Assert.AreEqual(
                1,
                _terminal._uiDocument.rootVisualElement.childCount,
                "The rebuilt terminal root is attached after reopening"
            );
        }

        private IEnumerator SpawnTerminalWithDocument()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUILifecycle");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
        }
    }
}
