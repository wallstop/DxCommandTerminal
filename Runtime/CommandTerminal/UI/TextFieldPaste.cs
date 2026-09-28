namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Text;
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
        referenced by an assembly with engine references). It is not
        universal: tvOS has no clipboard, and a platform whose clipboard is
        asynchronous (WebGL) answers with an empty string. An empty answer is
        the detection, and the key is left unconsumed so the platform keeps
        whatever it does with it.

        A pasted block is not typed input, and it must not arrive as one. A
        stack trace is newlines, a log line is tabs, the field is single-line
        and 22px tall - a newline that lands in the value is invisible, and it
        used to reach the command as one argument. So every run of
        whitespace collapses to the single space that separates arguments,
        and the caller drops a leading space at the start of the value, where
        a run of whitespace separates nothing.
     */
    internal static class TextFieldPaste
    {
        /*
            Returns true when a paste was applied, so the caller can consume
            the key. Every other keystroke, and an empty or all-whitespace
            clipboard, is left alone.
         */
        public static bool TryApply(TextField field, KeyDownEvent evt)
        {
            if (field == null || evt == null || evt.keyCode != KeyCode.V)
            {
                return false;
            }

            if (!evt.commandKey && !evt.ctrlKey)
            {
                return false;
            }

            string flattened = Flatten(GUIUtility.systemCopyBuffer);
            if (flattened.Length == 0)
            {
                return false;
            }

            string value = field.value ?? string.Empty;
            /*
                The caret is the moving end and selectIndex the anchor, so a
                drag made right-to-left reads as cursorIndex below
                selectIndex; the replaced span is the smaller of the two to
                the larger, and the paste lands at the smaller.
             */
            int start = Math.Clamp(Math.Min(field.cursorIndex, field.selectIndex), 0, value.Length);
            int end = Math.Clamp(Math.Max(field.cursorIndex, field.selectIndex), 0, value.Length);
            if (start == 0)
            {
                flattened = flattened.TrimStart(' ');
                if (flattened.Length == 0)
                {
                    return false;
                }
            }

            /*
                The value write, not SetValueWithoutNotify: both surfaces read
                a paste as a user edit, which is what routes it into the
                command text and refreshes the palette rows.
             */
            field.value = value.Remove(start, end - start).Insert(start, flattened);

#if UNITY_2022_1_OR_NEWER
            int caret = start + flattened.Length;
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
            is nothing but whitespace flattens to a single space, which still
            opens the argument slot a copied line's own trailing newline meant.
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
            for (int i = 0; i < length; ++i)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
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
