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

            The read-back is part of the arrange, the same two-way rule the
            clipboard write follows: a caret write the field does not report
            back means the fixture's selection cannot be built here, which
            is the environment (#250's degraded host clamps detached-field
            caret writes to 0), not a product verdict. Ignoring names it.
         */
        private static TextField FieldWithArrangedSelection(string value, int selectIndex, int cursorIndex)
        {
            TextField field = new TextField();
            field.value = value;
#if UNITY_2022_1_OR_NEWER
            field.selectIndex = selectIndex;
            field.cursorIndex = cursorIndex;
            if (field.selectIndex != selectIndex || field.cursorIndex != cursorIndex)
            {
                Assert.Ignore(
                    $"The field did not hold the arranged selection (wanted [{selectIndex}, {cursorIndex}], "
                        + $"holds [{field.selectIndex}, {field.cursorIndex}]); the caret fixtures need a host "
                        + "whose detached fields hold caret writes (#250)"
                );
            }
#else
            Assert.Ignore("2021.3 exposes the caret getters only; the engine owns placement, " + "so this fixture's arranged selection cannot be built here");
#endif
            return field;
        }

        /*
            Restores the clipboard the test saved. TryWrite refuses an empty
            text, so a saved empty clipboard falls back to the bare write the
            whole suite used before the retry budget existed.
         */
        private static void RestoreClipboard(string text)
        {
            if (0 < (text ?? string.Empty).Length)
            {
                _ = TerminalClipboard.TryWrite(text);
                return;
            }

            GUIUtility.systemCopyBuffer = string.Empty;
        }

        /*
            The environment gate for the fixtures that assert a copy happened:
            TryApply's answer is the product's own write-then-read-back, and a
            degraded host session (#250) races that read-back in dispatch, so
            a false answer here cannot tell a broken product from a lagging
            host without blocking the thread the product never blocks. The
            product's write-verify logic is pinned for real in the testhost
            runs (its clipboard stub answers deterministically) and on a
            recovered host; here, an unanswered copy ignores with the reason.
            An ANSWERED copy is the full product verdict, and the asserts run.
         */
        private static void AssertCopyAnswered(bool answered, string what)
        {
            if (!answered)
            {
                Assert.Ignore($"{what}: the host clipboard did not answer the copy's read-back in dispatch, " + "so delivery cannot be measured here (#250)");
            }
        }

        /*
            Arranges the clipboard for a fixture that asserts a value it did
            not write is preserved. The arrange write is verified through the
            product's own TryWrite; a host that races the read-back answers
            stale here too, so the later read cannot be trusted and the
            fixture ignores - the same discriminator the copy asserts use,
            applied to the arrange.
         */
        private static void ArrangeClipboard(string text)
        {
            if (!TerminalClipboard.TryWrite(text))
            {
                Assert.Ignore(
                    "The host clipboard did not answer the arrange write's read-back in dispatch, " + "so this fixture's clipboard asserts cannot be trusted here (#250)"
                );
            }
        }

        [SetUp]
        public void SetUp()
        {
            _originalClipboard = GUIUtility.systemCopyBuffer;
        }

        [TearDown]
        public void TearDown()
        {
            RestoreClipboard(_originalClipboard);
        }

        [Test]
        public void CopyCarriesTheSelectedSpanVerbatim()
        {
            TextField field = FieldWithArrangedSelection("spawn item torch 1", 6, 16);
            bool answered = TextFieldCopyCut.TryApply(field, KeyDown(KeyCode.C, EventModifiers.Control), out int? caret);
            AssertCopyAnswered(answered, "A Ctrl+C over a selection");
            Assert.That(caret == null, "A copy reports no caret, it changed no text");
            Assert.AreEqual("item torch", TerminalClipboard.Read(), "The clipboard holds the selected span, byte for byte");
            Assert.AreEqual("spawn item torch 1", field.value, "A copy leaves the field alone");
        }

        [Test]
        public void CutTakesTheSelectedSpanOutOfTheField()
        {
            TextField field = FieldWithArrangedSelection("spawn item torch 1", 6, 16);
            bool answered = TextFieldCopyCut.TryApply(field, KeyDown(KeyCode.X, EventModifiers.Control), out int? caret);
            AssertCopyAnswered(answered, "A Ctrl+X over a selection");
            Assert.AreEqual(6, caret, "The caret reports where the cut left the field");
            Assert.AreEqual("item torch", TerminalClipboard.Read());
            Assert.AreEqual("spawn  1", field.value, "The span is gone from the value");
        }

        [Test]
        public void AnEmptySelectionLeavesTheKeyAlone()
        {
            ArrangeClipboard("untouched");
            TextField field = FieldWithArrangedSelection("spawn item", 6, 6);
            bool answered = TextFieldCopyCut.TryApply(field, KeyDown(KeyCode.C, EventModifiers.Control), out int? caret);
            Assert.That(!answered, "A copy with nothing selected is not answered");
            Assert.That(caret == null);
            Assert.AreEqual("untouched", TerminalClipboard.Read());
            Assert.AreEqual("spawn item", field.value);
        }

        [Test]
        public void ACutOfAnEmptyFieldIsNotAnswered()
        {
            TextField field = FieldWithArrangedSelection(string.Empty, 0, 0);
            Assert.That(!TextFieldCopyCut.TryApply(field, KeyDown(KeyCode.X, EventModifiers.Control), out _), "Nothing selected, nothing to take");
            Assert.AreEqual(string.Empty, field.value);
        }

        [TestCase(KeyCode.C, EventModifiers.None, Description = "A bare C types a C")]
        [TestCase(KeyCode.X, EventModifiers.None, Description = "A bare X types an X")]
        [TestCase(KeyCode.V, EventModifiers.Control, Description = "Paste owns Ctrl+V")]
        [TestCase(KeyCode.C, EventModifiers.Shift, Description = "Shift adds no clipboard meaning")]
        public void KeysThatAreNotThisHelperSAreLeftAlone(KeyCode keyCode, EventModifiers modifiers)
        {
            ArrangeClipboard("untouched");
            TextField field = FieldWithArrangedSelection("spawn item", 0, 5);
            Assert.That(!TextFieldCopyCut.TryApply(field, KeyDown(keyCode, modifiers), out _), $"{keyCode} with {modifiers} is not a copy or a cut");
            Assert.AreEqual("spawn item", field.value);
            Assert.AreEqual("untouched", TerminalClipboard.Read());
        }

        [Test]
        public void FieldsAndEventsThatCannotAnswerAreLeftAlone()
        {
            Assert.That(!TextFieldCopyCut.TryApply(null, KeyDown(KeyCode.C, EventModifiers.Control), out _));
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
            bool answered = TextFieldCopyCut.TryApply(field, KeyDown(KeyCode.C, EventModifiers.Control), out _);
            AssertCopyAnswered(answered, "A Ctrl+C inside a surrogate pair");
            Assert.AreEqual("\ud83d\ude00b", TerminalClipboard.Read());
        }
    }
}
