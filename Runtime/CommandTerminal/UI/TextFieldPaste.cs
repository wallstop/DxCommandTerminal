namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Text;
    using Backend;
    using Extensions;
    using Helper;
    using UnityEngine;
    using UnityEngine.UIElements;

    /*
        Neither console surface can paste, because a UI Toolkit TextField has
        no clipboard of its own: the field is a value and a caret, and paste
        is a key the application owns. On 6000.4 the whole TextField
        hierarchy exposes no copy, paste, or clipboard member, so a developer
        copying a save name, a stack trace, or a chat line has to type it.

        The clipboard read is the one cross-version public path,
        GUIUtility.systemCopyBuffer (UnityEngine.IMGUIModule, always
        referenced by an assembly with engine references), and TerminalClipboard
        is where that name is written down, so the paste and the copy paths
        cannot drift onto different ones. It is not universal: tvOS has no
        clipboard, and a platform that needs a user gesture before it will
        answer - a browser clipboard API, as on WebGL - may read empty. An
        empty answer is the detection, and the key is left unconsumed so the
        platform keeps whatever it does with it. Neither case is measured
        here; the degradation is the design, not a fallback that failed.

        A pasted block is not typed input, and it must not arrive as one. A
        stack trace is newlines, a log line is tabs, the field is single-line
        and 22px tall - a newline that lands in the value is invisible, and it
        used to reach the command as one argument. So every run of whitespace
        collapses to the single space that separates arguments, and TryApply
        drops a leading space when the paste lands at the start of the value,
        where a run of whitespace separates nothing.

        A control character that is not whitespace is left in the value,
        which is a stated limit. It cannot be shown in a single-line field,
        but the field is the developer's own input: the character is the one
        they copied, it still reaches the argument intact, and a command
        that prints it gets the escape from the log funnel. Dropping it would
        lose their text and escaping it here would change the command they
        pasted. (A control character that is also whitespace - a tab, a
        newline - is the case above, and collapses.)

        The clipboard is pasted whole, however large. Flatten copies it
        through a pooled builder and the paste copies the result again, so a
        very large clipboard costs a few multiples of its size in transient
        memory and one frame's work. It is a paste, not a keystroke, and the
        pooled builder above the retention ceiling is handed back to the GC
        rather than pinned, so the cost does not stay.
     */
    internal static class TextFieldPaste
    {
        /*
            Returns true when a paste was applied, so the caller can consume
            the key, and hands back the caret the paste left so a caller that
            ranks against a caret can use the same one on every editor - 2021.3
            exposes the caret getters only, so the field's own cursorIndex is
            not an answer there. Every other keystroke, and an empty or
            all-whitespace clipboard, is left alone with the caret at zero.
         */
        public static bool TryApply(TextField field, KeyDownEvent evt, out int caret)
        {
            if (field == null || evt == null || evt.keyCode != KeyCode.V)
            {
                caret = 0;
                return false;
            }

            if (!evt.commandKey && !evt.ctrlKey)
            {
                caret = 0;
                return false;
            }

            string flattened = Flatten(TerminalClipboard.Read());
            if (flattened.Length == 0)
            {
                caret = 0;
                return false;
            }

            string value = field.value ?? string.Empty;
            /*
                The caret is the moving end and selectIndex the anchor, so a
                drag made right-to-left reads as cursorIndex below
                selectIndex; the replaced span is the smaller of the two to the
                larger, and the paste lands at the smaller.

                Both ends are snapped. The field moves its caret a code unit at
                a time, so it can report one inside a character, and a paste
                landing there would split it: the value would hold half a
                surrogate, which is not text, and the argument the command runs
                would carry it. Snapped, the paste lands beside the character
                and replaces all of it.
             */
            int start = value.SnapToTextBoundary(Math.Min(field.cursorIndex, field.selectIndex));
            int end = value.SnapToTextBoundary(Math.Max(field.cursorIndex, field.selectIndex));

            /*
                A run at the very start of the value is dropped. The tokenizer
                skips a leading separator before it reads a token, so a space
                there cannot separate the pasted text from what precedes it
                (nothing) or from what follows it (it is inside the same token
                either way). Keeping it would change the field's appearance and
                nothing else, so the field gets no stray leading space. Pasted
                anywhere else the run is kept, because there it is the break
                the copied line meant.
             */
            if (start == 0)
            {
                flattened = flattened.TrimStart(' ');
                if (flattened.Length == 0)
                {
                    caret = 0;
                    return false;
                }
            }

            /*
                The value write, not SetValueWithoutNotify: both surfaces read
                a paste as a user edit, which is what routes it into the
                command text and refreshes the palette rows. It also runs their
                change handler, which cancels any caret a completion had
                queued, so the write below is the last word on where the caret
                sits and needs no queue of its own.
             */
            field.value = value.Remove(start, end - start).Insert(start, flattened);

            /*
                Read back, not computed: a change handler that reverted the
                field in the same dispatch (a frame a hotkey claimed) leaves
                the old, shorter text here, and a caret past the end of it is
                the out-of-range write TerminalUI.ApplyPendingCaret guards.

                Not snapped, and deliberately. The caret is the end of what was
                just inserted, a position the developer can see, and the one
                way it can be inside a character is a clipboard that ends
                mid-sequence: a truncated emoji whose invisible joiner is
                followed by a character that joins with it. Snapping then
                floors the caret past the joiner and past the character in
                front of it, which is before the text just pasted - measured,
                a caret of 10 lands at 7 and one of 8 at 5. Every read of a
                caret is snapped, so the position a completion acts on is a
                character boundary either way; leaving this one alone keeps
                the caret where the paste ended.
             */
            caret = Math.Min(start + flattened.Length, field.value.Length);

#if UNITY_2022_1_OR_NEWER
            field.cursorIndex = caret;
            field.selectIndex = caret;
#else
            /* 2021.3 exposes the caret getters only; the engine owns placement. */
#endif
            return true;
        }

        /*
            One space per whitespace run, whatever the run was made of and
            including a run at either end, so a copied line keeps the spacing
            that separates it from what it is pasted next to. A clipboard that
            is nothing but whitespace flattens to a single space, which the
            caller then drops when the paste starts the value.

            The same class as the tokenizer's separator, deliberately: this
            runs first, so it leaves only plain spaces behind. That is what
            makes a quote still group - a run collapsed to a space is still
            inside the quotes, so the argument stays one argument. It is also
            why a newline inside a quoted argument becomes a space rather
            than surviving: a paste normalizes, a typed quote keeps the text.
         */
        internal static string Flatten(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int length = text.Length;
            using CachedStringBuilder.Scope scope = CachedStringBuilder.Rent(length);
            StringBuilder builder = scope.Builder;
            bool pendingSpace = false;

            /*
                Counting, not foreach: a string's enumerator is a class, so
                foreach here would add the one allocation this pass exists to
                avoid (rule 11).
             */
            for (int i = 0; i < length; ++i)
            {
                char c = text[i];
                if (CommandTokenizer.IsSeparator(c))
                {
                    pendingSpace = true;
                    continue;
                }

                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(c);
            }

            if (pendingSpace)
            {
                builder.Append(' ');
            }

            return builder.ToString();
        }
    }
}
