namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using Components;
    using Input;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /*
        End-to-end paste routing: TextFieldPasteTests covers the helper's own
        contract in EditMode, and this covers the part only a live panel can
        answer - that a Ctrl+V key event reaches the terminal's input at all,
        and that the value write arrives as the user edit the command text is
        read from. A UI Toolkit TextField has no clipboard of its own, so
        nothing here happens unless the terminal registers the key.

        Keys are injected at the document root, which is what makes the
        event travel down to the field the way a real keystroke does. An
        event aimed at the field itself is dropped: a panel routes a key by
        focus, and this synthetic panel has none. The palette suite injects
        at its own root for the same reason.
     */
    public sealed class TerminalUIPasteTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;
        private string _originalClipboard;

        private static T LoadAsset<T>(string relativePath)
            where T : ScriptableObject
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
        }

        [SetUp]
        public void SetUp()
        {
            _originalClipboard = GUIUtility.systemCopyBuffer;
            DefaultTerminalInput.Instance.CommandText = string.Empty;
        }

        [TearDown]
        public void TearDown()
        {
            GUIUtility.systemCopyBuffer = _originalClipboard;
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
        public IEnumerator PasteReachesTheCommandTextAsAUserEdit()
        {
            yield return SpawnOpenTerminal();

            GUIUtility.systemCopyBuffer = "give\titem\n42";
            using (
                KeyDownEvent key = KeyDownEvent.GetPooled('\0', KeyCode.V, EventModifiers.Control)
            )
            {
                _terminal._uiDocument.rootVisualElement.SendEvent(key);
            }

            yield return null;

            Assert.AreEqual(
                "give item 42",
                _terminal._commandInput.value,
                "The field holds the flattened paste"
            );

            /*
                The paste has to arrive as a user edit, not as a silent write:
                the terminal reads its command text from the change event, so
                Enter would run nothing if the write skipped it.
             */
            yield return WaitForCommandText("give item 42");
        }

        [UnityTest]
        public IEnumerator VWithoutThePasteModifierDoesNotPaste()
        {
            yield return SpawnOpenTerminal();

            GUIUtility.systemCopyBuffer = "give item 42";
            yield return SendKey(KeyCode.V, EventModifiers.None);
            yield return null;

            Assert.AreEqual(
                string.Empty,
                _terminal._commandInput.value,
                "A V with no paste modifier leaves the field alone"
            );
            Assert.AreEqual(
                string.Empty,
                DefaultTerminalInput.Instance.CommandText,
                "A V with no paste modifier leaves the command text alone"
            );
        }

        private IEnumerator SpawnOpenTerminal()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUIPaste");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("Paste routing is editor Play Mode coverage.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);

            /*
                Two frames, not one. SetState flags a command as issued for
                the frame it runs on, and the input change handler reverts
                field writes until that flag clears in a later refresh pass;
                the state frame can also land after that frame's own
                LateUpdate, so the pass that clears it is the frame after the
                one that set it. A paste sent before it clears is reverted,
                which would read as the paste never happening.
             */
            yield return null;
            yield return null;

            int frameBudget = 600;
            while (
                0 < frameBudget--
                && (
                    _terminal._commandInput == null
                    || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex
                )
            )
            {
                yield return null;
            }

            Assert.That(
                _terminal._commandInput != null,
                "The terminal input field should exist after the terminal opens"
            );
        }

        private IEnumerator SendKey(KeyCode keyCode, EventModifiers modifiers)
        {
            using (KeyDownEvent key = KeyDownEvent.GetPooled('\0', keyCode, modifiers))
            {
                _terminal._uiDocument.rootVisualElement.SendEvent(key);
            }

            yield return null;
        }

        private IEnumerator WaitForCommandText(string expected)
        {
            int frameBudget = 120;
            while (
                0 < frameBudget--
                && !string.Equals(
                    DefaultTerminalInput.Instance.CommandText,
                    expected,
                    StringComparison.Ordinal
                )
            )
            {
                yield return null;
            }

            Assert.AreEqual(
                expected,
                DefaultTerminalInput.Instance.CommandText,
                "The pasted text is what Enter would run"
            );
        }
    }
}
