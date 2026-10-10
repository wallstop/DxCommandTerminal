namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using Backend;
    using Extensions;
    using UnityEngine;
    using UnityEngine.UIElements;

    /*
        Neither console surface can copy or cut, because a UI Toolkit TextField
        has no clipboard of its own: the field is a value and a caret, and
        copy is a key the application owns. A developer who selects half a
        command line with shift+arrows could paste over it and nothing else -
        the selection could not leave the field (#194).

        The write goes through TerminalClipboard.TryWrite, the same verified
        path the copy commands use, so a platform that will not keep the text
        (tvOS has no clipboard, a browser may demand a gesture) reports the
        refusal instead of failing silently. A refused copy leaves the key
        unconsumed and nothing changed; a refused cut also leaves the
        selection in the field, because deleting text the clipboard never
        took is data loss, not a cut. The degradation is the paste path's
        own: an empty clipboard read leaves the key alone the same way.

        The copied span is snapped to character boundaries like a paste, so a
        caret the field reports inside a surrogate pair cannot put half a
        character on the clipboard. The span is copied verbatim: the field is
        the developer's own input, and a copy that normalized it would put
        different text on the clipboard than the line they selected.
     */
    internal static class TextFieldCopyCut
    {
        /*
            Returns true when the key is a copy or a cut this class answers,
            so the caller consumes it. `caret` reports where a cut left the
            caret - null after a copy, which changes no text, so a caller
            that ranks against a caret (the palette) re-derives only when the
            value actually moved.
         */
        public static bool TryApply(TextField field, KeyDownEvent evt, out int? caret)
        {
            if (field == null || evt == null)
            {
                caret = null;
                return false;
            }

            bool isCut = evt.keyCode == KeyCode.X;
            if (!isCut && evt.keyCode != KeyCode.C)
            {
                caret = null;
                return false;
            }

            if (!evt.commandKey && !evt.ctrlKey)
            {
                caret = null;
                return false;
            }

            string value = field.value ?? string.Empty;
            /*
                The caret is the moving end and selectIndex the anchor, so a
                drag made right-to-left reads as cursorIndex below
                selectIndex; the copied span is the smaller of the two to the
                larger, snapped like a paste so it cannot split a character.
             */
            int start = value.SnapToTextBoundary(Math.Min(field.cursorIndex, field.selectIndex));
            int end = value.SnapToTextBoundary(Math.Max(field.cursorIndex, field.selectIndex));
            if (end <= start)
            {
                caret = null;
                return false;
            }

            if (!TerminalClipboard.TryWrite(value.Substring(start, end - start)))
            {
                caret = null;
                return false;
            }

            if (!isCut)
            {
                caret = null;
                return true;
            }

            /*
                The value write, not SetValueWithoutNotify: a cut arrives as a
                user edit for the same reason a paste does - the terminal
                reads its command text from the change event, and the palette
                re-derives its query from the caret the edit left.
             */
            field.value = value.Remove(start, end - start);

#if UNITY_2022_1_OR_NEWER
            field.cursorIndex = start;
            field.selectIndex = start;
#else
            /* 2021.3 exposes the caret getters only; the engine owns placement. */
#endif
            /*
                The reported caret is the computed start on every version,
                the same contract the paste reports on: 2021.3 cannot place
                the field's caret programmatically, so the value there is
                where the engine left it, but the position a caller ranks
                against is still the span's start - the one position the cut
                can state with certainty.
             */
            caret = start;
            return true;
        }
    }
}
