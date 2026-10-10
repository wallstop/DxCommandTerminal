namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.UIElements;

    /*
        The helper's own contract on a detached field, the same split
        TextFieldPasteTests uses: the routing that needs a live panel is
        covered in Play Mode, and everything decidable from the field and
        the event is decided here.
     */
    public sealed class TextFieldCopyCutTests
    {
        private string _originalClipboard;

        private static KeyDownEvent KeyDown(KeyCode keyCode, EventModifiers modifiers)
        {
            return KeyDownEvent.GetPooled('\0', keyCode, modifiers);
        }

        /*
            Arranges a selection on a detached field. 2022.1 made the caret
            settable (the same verified constant TextFieldPaste itself
            guards); 2021.3 exposes the getters only and the engine owns
            placement, so no public path builds an arbitrary selection on a
            field that is not in a panel there. A fixture that needs an
            arranged selection therefore ignores itself on 2021.3 with that
            reason and runs unchanged everywhere the setter exists.
         */
        private static TextField FieldWithArrangedSelection(
            string value,
            int selectIndex,
            int cursorIndex
        )
        {
            TextField field = new TextField();
            field.value = value;
#if UNITY_2022_1_OR_NEWER
            field.selectIndex = selectIndex;
            field.cursorIndex = cursorIndex;
#else
            Assert.Ignore(
                "2021.3 exposes the caret getters only; the engine owns placement, "
                    + "so this fixture's arranged selection cannot be built here"
            );
#endif
            return field;
        }

        [SetUp]
        public void SetUp()
        {
            _originalClipboard = GUIUtility.systemCopyBuffer;
        }

        [TearDown]
        public void TearDown()
        {
            GUIUtility.systemCopyBuffer = _originalClipboard;
        }

        [Test]
        public void CopyCarriesTheSelectedSpanVerbatim()
        {
            TextField field = FieldWithArrangedSelection("spawn item torch 1", 6, 16);
            bool answered = TextFieldCopyCut.TryApply(
                field,
                KeyDown(KeyCode.C, EventModifiers.Control),
                out int? caret
            );
            Assert.That(answered, "A Ctrl+C over a selection is answered");
            Assert.That(caret == null, "A copy reports no caret, it changed no text");
            Assert.AreEqual(
                "item torch",
                TerminalClipboard.Read(),
                "The clipboard holds the selected span, byte for byte"
            );
            Assert.AreEqual("spawn item torch 1", field.value, "A copy leaves the field alone");
        }

        [Test]
        public void CutTakesTheSelectedSpanOutOfTheField()
        {
            TextField field = FieldWithArrangedSelection("spawn item torch 1", 6, 16);
            bool answered = TextFieldCopyCut.TryApply(
                field,
                KeyDown(KeyCode.X, EventModifiers.Control),
                out int? caret
            );
            Assert.That(answered, "A Ctrl+X over a selection is answered");
            Assert.AreEqual(6, caret, "The caret reports where the cut left the field");
            Assert.AreEqual("item torch", TerminalClipboard.Read());
            Assert.AreEqual("spawn  1", field.value, "The span is gone from the value");
        }

        [Test]
        public void AnEmptySelectionLeavesTheKeyAlone()
        {
            GUIUtility.systemCopyBuffer = "untouched";
            TextField field = FieldWithArrangedSelection("spawn item", 6, 6);
            bool answered = TextFieldCopyCut.TryApply(
                field,
                KeyDown(KeyCode.C, EventModifiers.Control),
                out int? caret
            );
            Assert.That(!answered, "A copy with nothing selected is not answered");
            Assert.That(caret == null);
            Assert.AreEqual("untouched", TerminalClipboard.Read());
            Assert.AreEqual("spawn item", field.value);
        }

        [Test]
        public void ACutOfAnEmptyFieldIsNotAnswered()
        {
            TextField field = FieldWithArrangedSelection(string.Empty, 0, 0);
            Assert.That(
                !TextFieldCopyCut.TryApply(
                    field,
                    KeyDown(KeyCode.X, EventModifiers.Control),
                    out _
                ),
                "Nothing selected, nothing to take"
            );
            Assert.AreEqual(string.Empty, field.value);
        }

        [TestCase(KeyCode.C, EventModifiers.None, Description = "A bare C types a C")]
        [TestCase(KeyCode.X, EventModifiers.None, Description = "A bare X types an X")]
        [TestCase(KeyCode.V, EventModifiers.Control, Description = "Paste owns Ctrl+V")]
        [TestCase(KeyCode.C, EventModifiers.Shift, Description = "Shift adds no clipboard meaning")]
        public void KeysThatAreNotThisHelperSAreLeftAlone(KeyCode keyCode, EventModifiers modifiers)
        {
            TextField field = FieldWithArrangedSelection("spawn item", 0, 5);
            GUIUtility.systemCopyBuffer = "untouched";
            Assert.That(
                !TextFieldCopyCut.TryApply(field, KeyDown(keyCode, modifiers), out _),
                $"{keyCode} with {modifiers} is not a copy or a cut"
            );
            Assert.AreEqual("spawn item", field.value);
            Assert.AreEqual("untouched", TerminalClipboard.Read());
        }

        [Test]
        public void FieldsAndEventsThatCannotAnswerAreLeftAlone()
        {
            Assert.That(
                !TextFieldCopyCut.TryApply(null, KeyDown(KeyCode.C, EventModifiers.Control), out _)
            );
            Assert.That(!TextFieldCopyCut.TryApply(new TextField(), null, out _));
        }

        [Test]
        public void ASnappedSelectionCannotPutHalfACharacterOnTheClipboard()
        {
            /*
                The field can report a caret inside a surrogate pair - "a😀b"
                is a, a high surrogate, a low surrogate, b - and the copied
                span snaps outward the way a paste's span does: a caret
                between the surrogates floors to the character's start, so the
                clipboard carries whole characters only.
             */
            TextField field = FieldWithArrangedSelection("a\ud83d\ude00b", 4, 2);
            Assert.That(
                TextFieldCopyCut.TryApply(field, KeyDown(KeyCode.C, EventModifiers.Control), out _)
            );
            Assert.AreEqual("\ud83d\ude00b", TerminalClipboard.Read());
        }
    }
}
