namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using UnityEngine;

    /*
        The system clipboard, in both directions, and the one place that knows
        what it is.

        GUIUtility.systemCopyBuffer (UnityEngine.IMGUIModule) is the only
        cross-version public path, and it is always reachable from an assembly
        with engine references. It is not universal: tvOS has no clipboard, and
        a platform that will not answer without a user gesture - a browser
        clipboard API, as on WebGL - may refuse a write or read back empty. The
        paste direction detects that by the empty answer it gets; a write has
        no such answer, so the only portable evidence is to write and read back
        and compare. A platform that kept the text reads it back identically,
        and one that dropped it does not, which is what makes a refused copy
        reportable instead of silent.

        The read-back is the platform's copy, so it is what the paste direction
        then pastes. That is a second read, not a stored value, so a platform
        that answers reads are still answered by the platform and not by this
        class.

        A limit worth stating: on a browser, a clipboard that accepts the write
        synchronously but drops it before the paste is not something this can
        see. The report is "the platform did not keep what was written", not
        "the copy is now on the system clipboard", and the callers word it that
        way.
     */
    internal static class TerminalClipboard
    {
        /*
            The clipboard's text, or empty where the platform has none. The
            empty answer is the detection: tvOS has no clipboard, and a
            platform that needs a gesture may read empty, and neither is an
            error the caller has to raise.
         */
        public static string Read()
        {
            return GUIUtility.systemCopyBuffer ?? string.Empty;
        }

        /*
            Writes the text, and reports whether the platform kept it. False is
            not a failure the caller should hide: it is the only way a copy
            that never happened can be told apart from one that did.
         */
        public static bool TryWrite(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            GUIUtility.systemCopyBuffer = text;

            /*
                Compared, not assumed: the write is a request to a platform
                that can decline it, and the value the clipboard reports back
                is the only answer available. A platform that has no clipboard
                leaves the previous value in place, and a platform that
                truncated the text reports a different one; both are a copy the
                developer did not get.
             */
            return string.Equals(GUIUtility.systemCopyBuffer, text, StringComparison.Ordinal);
        }
    }
}
