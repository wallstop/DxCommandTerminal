namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class TextFieldPasteTests
    {
        private string _originalClipboard;

        private static KeyDownEvent PasteKeyDown(EventModifiers modifiers)
        {
            return PasteKeyDown(KeyCode.V, modifiers);
        }

        private static KeyDownEvent PasteKeyDown(KeyCode keyCode, EventModifiers modifiers)
        {
            return KeyDownEvent.GetPooled('\0', keyCode, modifiers);
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

        [TestCase("give item 42", "give item 42", Description = "Plain text is untouched")]
        [TestCase("give\nitem\n42", "give item 42", Description = "Newlines become spaces")]
        [TestCase("give\r\nitem", "give item", Description = "A CRLF is one separator")]
        [TestCase("give\ta\tb", "give a b", Description = "Tabs become spaces")]
        [TestCase(
            "give   item",
            "give item",
            Description = "An interior run collapses to one space"
        )]
        [TestCase("  give", " give", Description = "A leading run keeps its one space")]
        [TestCase("give  ", "give ", Description = "A trailing run keeps its one space")]
        [TestCase("a\n\n\nb", "a b", Description = "Blank pasted lines are one separator")]
        [TestCase("give\u00a0item", "give item", Description = "A non-breaking space separates")]
        [TestCase("   ", " ", Description = "An all-whitespace clipboard is one space")]
        [TestCase("", "", Description = "An empty clipboard flattens to nothing")]
        [TestCase(
            "set name \"two\nlines\"",
            "set name \"two lines\"",
            Description = "A quote still groups, but the run inside it is normalized"
        )]
        public void FlattenCollapsesWhitespaceRuns(string clipboard, string expected)
        {
            Assert.AreEqual(expected, TextFieldPaste.Flatten(clipboard), $"Flatten '{clipboard}'");
        }

        [TestCase(
            "",
            0,
            0,
            "give item 42",
            "give item 42",
            12,
            Description = "Into an empty field"
        )]
        [TestCase(
            "give ",
            5,
            5,
            "item 42",
            "give item 42",
            12,
            Description = "At the end of the field"
        )]
        [TestCase(
            "give 42",
            4,
            4,
            " item",
            "give item 42",
            9,
            Description = "In the middle of the field"
        )]
        [TestCase(
            "give",
            4,
            4,
            " spawn\n enemy",
            "give spawn enemy",
            16,
            Description = "A pasted block flattens on the way in"
        )]
        [TestCase(
            "give ",
            5,
            5,
            "   ",
            "give  ",
            6,
            Description = "An all-whitespace paste is one space"
        )]
        [TestCase(
            "",
            0,
            0,
            "  give item",
            "give item",
            9,
            Description = "A leading run at the start is dropped"
        )]
        public void TryApplyReplacesTheSelection(
            string value,
            int selectIndex,
            int cursorIndex,
            string clipboard,
            string expectedValue,
            int expectedCaret
        )
        {
            TextField field = new TextField();
            field.value = value;
            field.selectIndex = selectIndex;
            field.cursorIndex = cursorIndex;
            GUIUtility.systemCopyBuffer = clipboard;

            using KeyDownEvent paste = PasteKeyDown(EventModifiers.Control);
            Assert.IsTrue(
                TextFieldPaste.TryApply(field, paste, out _),
                $"Ctrl+V should paste '{clipboard}'"
            );
            Assert.AreEqual(expectedValue, field.value, "Pasted value");
            Assert.AreEqual(expectedCaret, field.cursorIndex, "Caret after the paste");
            Assert.AreEqual(expectedCaret, field.selectIndex, "Selection after the paste");
        }

        [Test]
        public void TryApplyReplacesTheSelectedSpan()
        {
            TextField field = new TextField();
            field.value = "give item 42";
            field.selectIndex = 5;
            field.cursorIndex = 9;
            GUIUtility.systemCopyBuffer = "torch\npick";

            using KeyDownEvent paste = PasteKeyDown(EventModifiers.Control);
            Assert.IsTrue(TextFieldPaste.TryApply(field, paste, out _), "Ctrl+V should paste");
            Assert.AreEqual("give torch pick 42", field.value, "The selection was replaced");
            Assert.AreEqual(15, field.cursorIndex, "Caret sits after the pasted text");
        }

        [Test]
        public void AnAllWhitespacePasteAtTheStartDoesNothing()
        {
            TextField field = new TextField();
            field.value = "give";
            field.selectIndex = 0;
            field.cursorIndex = 0;
            GUIUtility.systemCopyBuffer = "   ";

            using KeyDownEvent paste = PasteKeyDown(EventModifiers.Control);
            Assert.IsFalse(
                TextFieldPaste.TryApply(field, paste, out _),
                "A leading whitespace run separates nothing, so there is no paste to apply"
            );
            Assert.AreEqual("give", field.value, "The field is untouched");
        }

        [Test]
        public void ABackwardsSelectionIsReplaced()
        {
            TextField field = new TextField();
            field.value = "give item 42";
            field.cursorIndex = 5;
            field.selectIndex = 9;
            GUIUtility.systemCopyBuffer = "torch";

            using KeyDownEvent paste = PasteKeyDown(EventModifiers.Command);
            Assert.IsTrue(
                TextFieldPaste.TryApply(field, paste, out int caret),
                "A macOS Cmd+V pastes the same way"
            );
            Assert.AreEqual("give torch 42", field.value, "The selection was replaced");
            Assert.AreEqual(10, caret, "The reported caret is where the paste landed");
            Assert.AreEqual(caret, field.cursorIndex, "The field was left on the same caret");
        }

        /*
            The reported caret is the contract the quick-launch bar ranks
            against, and it has to hold where the caret setters do not exist
            (2021.3). A key that is not a paste reports zero, so a caller
            cannot mistake a failed paste for a position.
         */
        [TestCase("give", 4, 4, "item 42", 12, Description = "At the end of the field")]
        [TestCase("give 42", 4, 4, " item", 9, Description = "In the middle of the field")]
        [TestCase("give item 42", 5, 9, "torch pick", 15, Description = "Over a selection")]
        [TestCase("", 0, 0, "give\titem", 9, Description = "A pasted block, flattened")]
        public void TheReportedCaretIsWhereThePasteLanded(
            string value,
            int selectIndex,
            int cursorIndex,
            string clipboard,
            int expectedCaret
        )
        {
            TextField field = new TextField();
            field.value = value;
            field.selectIndex = selectIndex;
            field.cursorIndex = cursorIndex;
            GUIUtility.systemCopyBuffer = clipboard;

            using KeyDownEvent paste = PasteKeyDown(EventModifiers.Control);
            Assert.IsTrue(
                TextFieldPaste.TryApply(field, paste, out int caret),
                "Ctrl+V should paste"
            );
            Assert.AreEqual(
                expectedCaret,
                caret,
                $"The reported caret for '{value}' + '{clipboard}'"
            );
            Assert.AreEqual(expectedCaret, field.cursorIndex, "The field holds that caret too");
        }

        [Test]
        public void AKeyThatIsNotAPasteReportsNoCaret()
        {
            TextField field = new TextField();
            field.value = "give";
            field.selectIndex = 4;
            field.cursorIndex = 4;
            GUIUtility.systemCopyBuffer = "item 42";

            using KeyDownEvent key = PasteKeyDown(KeyCode.V, EventModifiers.None);
            Assert.IsFalse(
                TextFieldPaste.TryApply(field, key, out int caret),
                "A plain V is not a paste"
            );
            Assert.AreEqual(0, caret, "A failed paste reports no position");
        }

        [TestCase(KeyCode.V, EventModifiers.None, Description = "A plain V types a V")]
        [TestCase(KeyCode.V, EventModifiers.Shift, Description = "Shift+V types a V")]
        [TestCase(KeyCode.V, EventModifiers.Alt, Description = "Alt+V is not a paste")]
        [TestCase(KeyCode.T, EventModifiers.Control, Description = "Ctrl+T is not a paste")]
        [TestCase(
            KeyCode.LeftArrow,
            EventModifiers.Control,
            Description = "Ctrl+Left still moves the caret"
        )]
        public void NonPasteKeysAreLeftAlone(KeyCode keyCode, EventModifiers modifiers)
        {
            TextField field = new TextField();
            field.value = "give";
            field.selectIndex = 4;
            field.cursorIndex = 4;
            GUIUtility.systemCopyBuffer = "item";

            using KeyDownEvent key = PasteKeyDown(keyCode, modifiers);
            Assert.IsFalse(
                TextFieldPaste.TryApply(field, key, out _),
                $"'{keyCode}' with {modifiers} is not a paste"
            );
            Assert.AreEqual("give", field.value, "The field is untouched");
        }

        [Test]
        public void AnEmptyClipboardIsNotAConsumedPaste()
        {
            TextField field = new TextField();
            field.value = "give";
            field.selectIndex = 4;
            field.cursorIndex = 4;
            GUIUtility.systemCopyBuffer = string.Empty;

            using KeyDownEvent paste = PasteKeyDown(EventModifiers.Control);
            Assert.IsFalse(
                TextFieldPaste.TryApply(field, paste, out _),
                "tvOS has no clipboard and a platform that will not answer reads empty; that is not a paste"
            );
            Assert.AreEqual("give", field.value, "The field is untouched");
        }

        [Test]
        public void AMissingFieldOrKeyIsNotAPaste()
        {
            TextField field = new TextField();
            GUIUtility.systemCopyBuffer = "item";
            using KeyDownEvent paste = PasteKeyDown(EventModifiers.Control);

            Assert.IsFalse(
                TextFieldPaste.TryApply(null, paste, out _),
                "No field means nothing to paste into"
            );
            Assert.IsFalse(
                TextFieldPaste.TryApply(field, null, out _),
                "No key means nothing was asked for"
            );
            Assert.AreEqual(string.Empty, field.value, "Neither call wrote the field");
        }

        [Test]
        public void AControlCharacterInAClipIsLeftToTheCommand()
        {
            /*
                The stated limit, pinned: a control character cannot be shown
                in a single-line field, but it is the text the developer
                copied and it still has to reach the argument intact. The log
                funnel is what renders it safely once the command prints it.
             */
            string clipboard = "give" + ((char)7) + "item";

            Assert.AreEqual(clipboard, TextFieldPaste.Flatten(clipboard), "Flatten keeps it");

            TextField field = new TextField();
            GUIUtility.systemCopyBuffer = clipboard;
            using KeyDownEvent paste = PasteKeyDown(EventModifiers.Control);
            Assert.IsTrue(TextFieldPaste.TryApply(field, paste, out _), "The paste still happens");
            Assert.AreEqual(clipboard, field.value, "The copied text is what lands in the field");
        }
    }
}
