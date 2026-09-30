namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using UnityEngine;

    /*
        Neither console surface could undo. A UI Toolkit text field carries no
        text history: the pinned 2021.3.33 UI Toolkit module declares no member
        named Undo or Redo at all, and the package's own surfaces had no stack
        either, so Ctrl+Z in the command line, and in the quick-launch bar's
        search field, did nothing on every supported editor. A typo in a long
        command was then corrected one Backspace at a time, which is the worst
        case for the longest commands.

        One stack per surface, over the text the field holds. A surface calls
        Observe whenever the field's text changes, and Step when a history key
        arrives, so the top of the stack is always the state the field is in,
        whichever write put it there. A completion, a history recall, a pasted
        line, and a keystroke are therefore all one step back, and a write path
        added later cannot leave the stack stale: Step records the field's text
        for itself, so a path that forgets is repaired before anything reads the
        stack - it costs that path a step too far, not a lost line.

        That is also the answer to whether an edit the developer did not type is
        undoable. It is: undo returns to the state before the last thing that
        happened to the line, which is what the key means everywhere else.
        Running a command clears the line, and that clear is a state like any
        other, so one more Ctrl+Z brings the executed command back - which is
        also the answer to wanting it back without recalling it from history.

        One step per change rather than one per run of typing. The bound is
        MaxEntries and the oldest state is dropped when it is reached, so a
        session costs a fixed amount however long it runs.

        Ctrl+Z steps back and Ctrl+Shift+Z steps forward (Cmd on macOS). Both
        flags decide the command modifier, for the reason the paste path and
        the log paging path read both: which flag a command modifier arrives in
        is a per-editor detail.
     */
    internal sealed class TextFieldUndo
    {
        /*
            Sixty-four states is more undo than a developer reaches for by
            hand, and it keeps the stack a fixed cost per surface: a string
            reference and two ints per entry, and nothing copied out of the
            entries when the oldest is dropped.
         */
        public const int MaxEntries = 64;

        private readonly List<Entry> _entries = new(MaxEntries);

        /*
            The entry the field is in. Everything above it is redo, and is
            dropped the moment the field holds a state the stack has not seen.
         */
        private int _index;

        public TextFieldUndo()
        {
            _entries.Add(new Entry(string.Empty, 0));
        }

        /*
            Undo and redo are one predicate pair rather than one method, so a
            caller reads the two keys it is answering instead of a boolean that
            could be either. Shift is what separates them, and the same way it
            separates them in every editor the developer types in.
         */
        public static bool IsUndo(KeyCode keyCode, bool commandOrCtrl, bool shiftKey)
        {
            return KeyCode.Z == keyCode && commandOrCtrl && !shiftKey;
        }

        public static bool IsRedo(KeyCode keyCode, bool commandOrCtrl, bool shiftKey)
        {
            return KeyCode.Z == keyCode && commandOrCtrl && shiftKey;
        }

        /*
            A surface tears its field down and builds a new one. The states a
            rebuilt field held describe a line that is gone, so a stack that
            still offered them would fill the fresh field with text from before
            the console was closed.
         */
        public void Clear()
        {
            _entries.Clear();
            _entries.Add(new Entry(string.Empty, 0));
            _index = 0;
        }

        /*
            Records what the field holds now. Every path that changes the field's
            text calls this, so the stack's top is the state the field is in
            rather than the state it was in when a key was last pressed. That is
            the whole difference between stepping back one keystroke and
            emptying the line.

            The caret is snapped against the text being recorded rather than
            against the caller's own string, because the two are not always the
            same string: a value write reaches the field on its own schedule, so
            a caret read on the frame it is handed over can sit past the end of
            the text it is being recorded for.
         */
        public void Observe(string fieldText, int fieldCaret)
        {
            string value = fieldText ?? string.Empty;
            if (string.Equals(_entries[_index].Text, value, StringComparison.Ordinal))
            {
                /*
                    A state the stack already holds, which is the echo of a
                    write the stack just made. Recording it again would put a
                    second identical entry in the stack, and the second Ctrl+Z
                    would land on the state the first one had already left - a
                    key that visibly does nothing.
                 */
                return;
            }

            /*
                A new state ends the redo tail: the states after it describe a
                line that no longer exists, and leaving them in place would let
                a redo hand the developer text they undid away from.
             */
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
            _entries.Add(new Entry(value, value.SnapToTextBoundary(fieldCaret)));

            if (MaxEntries < _entries.Count)
            {
                _entries.RemoveAt(0);
            }

            _index = _entries.Count - 1;
        }

        /*
            Moves one state and hands back the text and caret to write.

            False means there is no state in that direction, and the outs are the
            empty state: the field is left alone, so a key with nothing to undo
            does nothing at all rather than clearing the line. A surface
            therefore must not act on the returned text alone, because an empty
            line and nothing to undo return the same one.
         */
        public bool Step(
            string fieldText,
            int fieldCaret,
            bool forward,
            out string text,
            out int caret
        )
        {
            Observe(fieldText, fieldCaret);

            int next = forward ? _index + 1 : _index - 1;
            if (next < 0 || _entries.Count <= next)
            {
                text = string.Empty;
                caret = 0;
                return false;
            }

            _index = next;
            text = _entries[next].Text;
            caret = _entries[next].Caret;
            return true;
        }

        /*
            The text the field held and where its caret was, as one state. A
            caret is stored rather than re-derived: undoing a character typed
            in the middle of a line has to leave the caret in the middle of the
            line, and the end of the text is the only position derivable from
            the text alone.

            On 2021.3 the engine has no caret setter, so a surface there can
            only take the placement the engine makes on a value write - the end
            of the line. The text is exact on every editor; the caret is exact
            from 2022.1.
         */
        private readonly struct Entry
        {
            public readonly string Text;
            public readonly int Caret;

            public Entry(string text, int caret)
            {
                Text = text;
                Caret = caret;
            }
        }
    }
}
