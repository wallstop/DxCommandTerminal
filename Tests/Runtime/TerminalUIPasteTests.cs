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
        End-to-end clipboard key routing: TextFieldPasteTests and
        TextFieldCopyCutTests cover the helpers' own contracts in EditMode,
        and this covers the part only a live panel can answer - that a
        Ctrl+V, Ctrl+C, or Ctrl+X key event reaches the terminal's input at
        all, and that a value write arrives as the user edit the command
        text is read from. A UI Toolkit TextField has no clipboard of its
        own, so nothing here happens unless the terminal registers the key.

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

            /*
                The send retries until the field holds the flattened paste:
                this host drops clipboard writes intermittently (#207), and a
                write that vanished between the set and the V the handler
                reads would fail the fixture for the environment's sake.
                Eight exhausted attempts probe the host; the probe decides
                whether exhaustion was the environment or a routing defect.
             */
            for (int attempt = 0; attempt < 8; ++attempt)
            {
                GUIUtility.systemCopyBuffer = "give\titem\n42";
                using (
                    KeyDownEvent key = KeyDownEvent.GetPooled(
                        '\0',
                        KeyCode.V,
                        EventModifiers.Control
                    )
                )
                {
                    _terminal._uiDocument.rootVisualElement.SendEvent(key);
                }

                yield return null;
                if (_terminal._commandInput.value == "give item 42")
                {
                    yield return WaitForCommandText("give item 42");
                    yield break;
                }
            }

            /*
                A host that holds a direct probe twice in a row is healthy,
                so eight straight failures name a paste-routing defect, not
                the environment; a host stuck in a bad window ignores from
                the probe.
             */
            yield return ProbeClipboard();
            Assert.Fail(
                "The host clipboard holds a direct write, but no paste held "
                    + "the flattened text; the V did not route to the field"
            );
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

        [UnityTest]
        public IEnumerator CopyCarriesTheSelectedCommandText()
        {
            yield return SpawnOpenTerminal();

            yield return SendClipboardKey(KeyCode.C, "item torch", "spawn item torch");

            Assert.AreEqual(
                "item torch",
                GUIUtility.systemCopyBuffer,
                "The selected span reached the system clipboard"
            );
            Assert.AreEqual(
                "spawn item torch",
                _terminal._commandInput.value,
                "A copy leaves the command line alone"
            );
        }

        [UnityTest]
        public IEnumerator CutRemovesTheSelectedCommandTextAsAUserEdit()
        {
            yield return SpawnOpenTerminal();

            yield return SendClipboardKey(KeyCode.X, "item torch", "spawn ");
            yield return null;

            Assert.AreEqual(
                "item torch",
                GUIUtility.systemCopyBuffer,
                "The cut span reached the system clipboard"
            );
            Assert.AreEqual(
                "spawn ",
                _terminal._commandInput.value,
                "The cut span is gone from the field"
            );
            yield return WaitForCommandText("spawn ");
        }

        [UnityTest]
        public IEnumerator CBareOrWithoutASelectionChangesNothing()
        {
            yield return SpawnOpenTerminal();

            GUIUtility.systemCopyBuffer = "untouched";
            _terminal._commandInput.value = "spawn item";
            yield return SendKey(KeyCode.C, EventModifiers.None);
            yield return null;
            Assert.AreEqual(
                "untouched",
                GUIUtility.systemCopyBuffer,
                "A bare C is a typed character, not a copy"
            );

#if UNITY_2022_1_OR_NEWER
            _terminal._commandInput.cursorIndex = 6;
            _terminal._commandInput.selectIndex = 6;
#endif
            yield return SendKey(KeyCode.C, EventModifiers.Control);
            yield return null;
            Assert.AreEqual(
                "untouched",
                GUIUtility.systemCopyBuffer,
                "A Ctrl+C with no selection copies nothing"
            );
            Assert.AreEqual("spawn item", _terminal._commandInput.value);
        }

        private void SetCommandSelection(string value, int selectIndex, int cursorIndex)
        {
            _terminal._commandInput.value = value;
#if UNITY_2022_1_OR_NEWER
            _terminal._commandInput.selectIndex = selectIndex;
            _terminal._commandInput.cursorIndex = cursorIndex;
#else
            Assert.Ignore("2021.3 exposes the caret getters only; the engine owns placement");
#endif
        }

        /*
            Sends a copy or cut until the full postcondition holds: the
            clipboard carries the span and the field is in the state the key
            leaves. The key handler writes through the verified
            TerminalClipboard path, and the same host race that drops a
            fixture's write can drop the product's - or answer the read-back
            behind it wrong, which is why the field is checked too and not
            just the clipboard. The selection is re-arranged every attempt,
            in the frame the key is sent, so a caret the panel re-clamped
            between frames cannot make the handler read an empty selection.

            Eight exhausted attempts probe the host once: a clipboard that
            cannot hold a direct write across two consecutive passes is the
            environment (#207) and the fixture ignores; one that answers
            twice in a row means the key never reached the handler, and that
            is a failure the retry must not absorb. The two-pass hold is
            what keeps a host that drops writes in windows (#207) from being
            called healthy because its bad window ended between the attempts
            and the probe.
         */
        private IEnumerator SendClipboardKey(
            KeyCode keyCode,
            string expectedClipboard,
            string expectedValue
        )
        {
            EventModifiers modifiers = EventModifiers.Control;
            for (int attempt = 0; attempt < 8; ++attempt)
            {
                SetCommandSelection("spawn item torch", 6, 16);
                using (KeyDownEvent key = KeyDownEvent.GetPooled('\0', keyCode, modifiers))
                {
                    _terminal._uiDocument.rootVisualElement.SendEvent(key);
                }

                yield return null;
                if (
                    GUIUtility.systemCopyBuffer == expectedClipboard
                    && _terminal._commandInput.value == expectedValue
                )
                {
                    yield break;
                }
            }

            yield return ProbeClipboard();
            Assert.Fail(
                $"The host clipboard holds a direct write, but a {keyCode} never produced "
                    + $"\"{expectedClipboard}\" on \"{expectedValue}\"; the key did not route"
            );
        }

        /*
            Writes and reads back a probe string through the system clipboard,
            the same evidence TerminalClipboard.TryWrite uses, until it holds
            on two consecutive passes. A host that never holds ignores from
            here - the #207 environment, not a regression - and one that
            holds twice lets the caller's Assert.Fail stand as a defect
            verdict.
         */
        private IEnumerator ProbeClipboard()
        {
            string original = GUIUtility.systemCopyBuffer;
            const string probe = "dxct-clipboard-probe";
            int heldPasses = 0;
            for (int attempt = 0; attempt < 10 && heldPasses < 2; ++attempt)
            {
                GUIUtility.systemCopyBuffer = probe;
                yield return null;
                heldPasses = GUIUtility.systemCopyBuffer == probe ? heldPasses + 1 : 0;
            }

            if (heldPasses < 2)
            {
                GUIUtility.systemCopyBuffer = original;
                Assert.Ignore(
                    "The host clipboard did not hold a direct probe twice in a row; "
                        + "the paste fixtures need a platform that answers (#207)"
                );
            }

            GUIUtility.systemCopyBuffer = original;
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
