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

        /*
            The frame the close animation snaps to the closed target is the
            frame the final height (0) and the hidden input display are
            written; a gate evaluated after the snap would skip that write
            and freeze the surface at the last animated height (with a
            zero-duration close, at the full open height).
         */
        [UnityTest]
        public IEnumerator ClosingWritesFinalClosedHeights()
        {
            yield return SpawnTerminalWithDocument();

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Sanity: the terminal is open and laid out");

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
            yield return null;

            Assert.AreEqual(
                0f,
                _terminal._uiDocument.rootVisualElement.resolvedStyle.height,
                "The closing frame must write the final closed height to the document root"
            );
            Assert.AreEqual(
                DisplayStyle.None,
                _terminal
                    ._uiDocument.rootVisualElement.Q<VisualElement>("InputContainer")
                    .resolvedStyle.display,
                "The closing frame must hide the input container"
            );
        }

        /*
            On-screen state buttons are the open controls for a closed
            terminal; the opt-in showGUIButtons mode builds its tree eagerly
            on enable so the buttons exist before any open.
         */
        [UnityTest]
        public IEnumerator StateButtonsBuildEagerlyWhileClosed()
        {
            yield return SpawnTerminalWithDocument(showButtons: true);

            VisualElement stateButtons = _terminal._uiDocument.rootVisualElement.Q(
                "StateButtonContainer"
            );
            Assert.AreEqual(
                1,
                _terminal._uiDocument.rootVisualElement.childCount,
                "The showGUIButtons mode builds its tree on enable"
            );
            Assert.IsNotNull(
                stateButtons,
                "The state button container exists while the terminal is closed"
            );
            Assert.AreEqual(
                2,
                stateButtons.childCount,
                "Both state buttons are built while the terminal is closed"
            );
        }

        private IEnumerator SpawnTerminalWithDocument(bool showButtons = false)
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
            _terminal.showGUIButtons = showButtons;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
        }
    }
}
