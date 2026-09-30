namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using NUnit.Framework;
    using UI;
    using UnityEngine;

    /*
        The undo stack is pure state over the text a field holds, so it is
        driven here the way a surface drives it: hand the stack what the
        field holds, and take the text it answers with as what the field now
        holds. No panel is involved, so the whole state machine - including
        the states a live panel cannot be made to produce on demand - is
        reachable from EditMode.

        TerminalUIHistoryTests covers the part only a real surface can
        answer: that the key reaches the field at all.
     */
    public sealed class TextFieldUndoTests
    {
        /*
            Types into a field the way a surface does: the stack is handed what
            the field holds. A new state has nothing in front of it, so nothing
            is stepped - this is the recording, not a step.
         */
        private static void Type(TextFieldUndo undo, string text)
        {
            Type(undo, text, text.Length);
        }

        private static void Type(TextFieldUndo undo, string text, int caret)
        {
            undo.Observe(text, caret);
        }

        private static string Take(TextFieldUndo undo, bool forward, string fieldText)
        {
            Assert.IsTrue(
                undo.Step(fieldText, fieldText.Length, forward, out string text, out _),
                $"There is a state to {(forward ? "redo" : "undo")} from '{fieldText}'"
            );
            return text;
        }

        /*
            Both flags count as the command modifier, because which one it
            arrives in is a per-editor detail - Command on macOS, Control
            elsewhere. A surface that read one would answer the key on one
            platform and not on the other.
         */
        [TestCase(KeyCode.Z, true, false, true, false)]
        [TestCase(KeyCode.Z, true, true, false, true)]
        [TestCase(KeyCode.Z, false, false, false, false)]
        [TestCase(KeyCode.Z, false, true, false, false)]
        [TestCase(KeyCode.Y, true, false, false, false)]
        [TestCase(KeyCode.V, true, false, false, false)]
        [TestCase(KeyCode.LeftArrow, false, false, false, false)]
        public void OnlyTheModifiedZIsHistory(
            KeyCode keyCode,
            bool commandOrCtrl,
            bool shiftKey,
            bool isUndo,
            bool isRedo
        )
        {
            Assert.AreEqual(
                isUndo,
                TextFieldUndo.IsUndo(keyCode, commandOrCtrl, shiftKey),
                "IsUndo for the given keys"
            );
            Assert.AreEqual(
                isRedo,
                TextFieldUndo.IsRedo(keyCode, commandOrCtrl, shiftKey),
                "IsRedo for the given keys"
            );
        }

        /*
            The reported gap: a line a developer typed came back one keystroke
            at a time, because there was no history behind it at all. Typing
            is a state like any other, so three keystrokes are three steps
            back to the empty line the field started on.
         */
        [Test]
        public void UndoStepsBackThroughEveryStateTheFieldHeld()
        {
            TextFieldUndo undo = new();
            Type(undo, "g");
            Type(undo, "gi");
            Type(undo, "giv");

            Assert.AreEqual("gi", Take(undo, false, "giv"), "The first undo drops one keystroke");
            Assert.AreEqual("g", Take(undo, false, "gi"), "The second drops the one before it");
            Assert.AreEqual(
                string.Empty,
                Take(undo, false, "g"),
                "The last undo lands on the empty line the field started on"
            );
        }

        /*
            "Stops at the start rather than doing nothing" is the whole of
            what a key with nothing left to undo may do. It must not report a
            step it did not take, and it must not hand back the empty state as
            though it had - a surface that wrote that would clear a line the
            developer was looking at.
         */
        [Test]
        public void AnUndoWithNothingLeftToUndoReportsNoStep()
        {
            TextFieldUndo undo = new();

            Assert.IsFalse(undo.Step(string.Empty, 0, false, out string text, out _));
            Assert.AreEqual(string.Empty, text, "And it hands back the empty state, not a step");
        }

        [Test]
        public void AForwardStepWithNothingLeftToRedoReportsNoStep()
        {
            TextFieldUndo undo = new();
            Type(undo, "g");
            Type(undo, "gi");

            Assert.IsFalse(undo.Step("gi", 2, true, out string text, out _));
            Assert.AreEqual(string.Empty, text, "And it hands back the empty state, not a step");
        }

        /*
            A surface calls Observe whenever the field's text changes, and
            hands the stack the text a step answered with on the next change -
            so the echo arrives every time. Recording it again would put a
            second identical entry in the stack, and the second Ctrl+Z would
            land on the state the first one had already left: a key that
            visibly does nothing.
         */
        [Test]
        public void AStateTheStackAlreadyHoldsIsNotRecordedTwice()
        {
            TextFieldUndo undo = new();
            Type(undo, "g");
            Type(undo, "gi");
            Assert.AreEqual("g", Take(undo, false, "gi"), "Sanity: the undo moved");

            Assert.AreEqual(
                string.Empty,
                Take(undo, false, "g"),
                "The second undo goes on past the state the first one left"
            );
        }

        /*
            The recording a surface does when the field's text changes, which
            is the thing that makes an undo a step back rather than a wipe: a
            stack that only learned states when a key was pressed had never
            seen the typing at all, and the first Ctrl+Z answered with the
            empty state the stack was seeded with.

            The keystrokes here are what a surface does - a change event, or a
            write of its own - rather than a step, which is the distinction
            this pins.
         */
        [Test]
        public void ObservingAKeystrokeIsWhatMakesTheFirstUndoAStepBack()
        {
            TextFieldUndo undo = new();
            undo.Observe("giv", 3);
            undo.Observe("givx", 4);

            Assert.AreEqual(
                "giv",
                Take(undo, false, "givx"),
                "One undo drops the keystroke that was observed last"
            );
        }

        /*
            Observe takes the caret the field has and the text it holds, and
            those can disagree for a frame: a value write reaches the field on
            its own schedule, so a caret read on the frame the text is handed
            over can sit past the end of it. Storing it unclamped would make
            the next step hand a surface a position its own ApplyPendingCaret
            has to reject.
         */
        [Test]
        public void ARecordedCaretIsClampedToTheTextItIsRecordedFor()
        {
            TextFieldUndo undo = new();
            undo.Observe("givx", 99);
            undo.Observe("gi", 99);

            undo.Step("gi", 2, false, out string undone, out _);
            Assert.AreEqual("givx", undone, "Sanity: the undo reached the state before it");

            undo.Step("givx", 4, true, out string redone, out int clamped);
            Assert.AreEqual("gi", redone, "Sanity: the redo came back to the state it left");

            Assert.AreEqual(
                2,
                clamped,
                "The caret that state carries is its own end, not the 99 it was observed with"
            );
        }

        /*
            A surface tears its field down and builds a new one. The states a
            rebuilt field held describe a line that is gone, so a stack that
            still offered them would fill the fresh field with text from before
            the console was closed.
         */
        /*
            The per-keystroke cost of recording. This is a hot path: a developer
            types, and every keystroke lands here. One List slot, and nothing
            copied out of an entry when the oldest is dropped - the strings are
            the field's own, which the field already holds, so nothing is
            duplicated.
         */
        [Test]
        public void RecordingTheSameTextRepeatedlyIsOneState()
        {
            TextFieldUndo undo = new();
            string held = string.Empty;
            undo.Observe(held, 0);

            held = new string('a', 64);
            for (int observed = 0; observed < TextFieldUndo.MaxEntries * 4; ++observed)
            {
                undo.Observe(held, 64);
            }

            int steps = 0;
            string undone;
            while (undo.Step(held, 64, false, out undone, out _))
            {
                held = undone;
                ++steps;
            }

            Assert.AreEqual(
                1,
                steps,
                "Every recording of that text was the state the stack already held"
            );
        }

        [Test]
        public void AClearedStackForgetsEveryStateTheFieldHeld()
        {
            TextFieldUndo undo = new();
            Type(undo, "give sword 5");
            undo.Clear();

            Assert.IsFalse(
                undo.Step(string.Empty, 0, false, out _, out _),
                "A cleared stack has nothing to undo, so the key does nothing"
            );
        }

        /*
            A completion, a history recall, and a pasted line are written by
            the terminal rather than typed. They are still states the field
            held, and undo returns to the state before the one the terminal
            wrote - which is what makes the stack correct without every write
            path announcing itself.
         */
        [Test]
        public void AStateTheSurfaceItselfWroteIsStillAStep()
        {
            TextFieldUndo undo = new();
            Type(undo, "give sw");
            Type(undo, "give sword");

            Assert.AreEqual(
                "give sw",
                Take(undo, false, "give sword"),
                "The state the terminal wrote is one step back"
            );
        }

        [Test]
        public void AForwardStepReachesTheStateThatWasUndone()
        {
            TextFieldUndo undo = new();
            Type(undo, "g");
            Type(undo, "gi");
            Assert.AreEqual("g", Take(undo, false, "gi"), "Sanity: the undo moved");

            Assert.AreEqual("gi", Take(undo, true, "g"), "The redo returns to the undone state");
        }

        /*
            The states above a redo describe a line that no longer exists, so
            a new keystroke ends them. A redo that could still reach them
            would hand the developer text they had just undone away from.
         */
        [Test]
        public void ANewStateDropsTheRedoTail()
        {
            TextFieldUndo undo = new();
            Type(undo, "g");
            Type(undo, "gi");
            Type(undo, "giv");
            Assert.AreEqual("gi", Take(undo, false, "giv"), "Sanity: the undo moved");

            Type(undo, "give");

            Assert.IsFalse(
                undo.Step("give", 4, true, out _, out _),
                "The state after the undone one was dropped, so there is nothing to redo"
            );
        }

        /*
            The caret is a state too, not a position derived from the text.
            Undoing a character typed in the middle of a line has to leave the
            caret in the middle of the line, and the end of the text is the
            only position derivable from the text alone.
         */
        [Test]
        public void UndoRestoresTheCaretAsWellAsTheText()
        {
            TextFieldUndo undo = new();
            Type(undo, "give s", 6);
            Type(undo, "give sw", 7);

            undo.Step("give sw", 7, false, out _, out int caret);
            Assert.AreEqual(6, caret, "The caret returns to where the undone state had it");
        }

        /*
            A session cannot grow the stack without bound. The oldest state is
            dropped at the ceiling, so the cost per surface is fixed and the
            newest MaxEntries states are still all reachable.
         */
        [Test]
        public void TheStackIsBoundedAndDropsTheOldestState()
        {
            TextFieldUndo undo = new();
            for (int typed = 1; typed <= TextFieldUndo.MaxEntries + 1; ++typed)
            {
                Type(undo, new string('a', typed));
            }

            string held = new string('a', TextFieldUndo.MaxEntries + 1);
            int steps = 0;
            string undone;
            while (undo.Step(held, held.Length, false, out undone, out _))
            {
                held = undone;
                ++steps;
            }

            Assert.AreEqual(
                TextFieldUndo.MaxEntries - 1,
                steps,
                "The ceiling dropped one state, so one fewer than was typed is reachable"
            );
            Assert.AreEqual(
                new string('a', 2),
                held,
                "The oldest state still reachable is the second, not the empty line it started on"
            );
        }
    }
}
