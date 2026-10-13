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

        What that evidence cannot distinguish is a platform that already held
        exactly this text: copying the same window twice reports success the
        second time whether or not the second write landed. Nor is a read-back
        an acknowledgement. A browser clipboard is promise-based, so the
        read-back can run before the write lands, and a copy that did succeed
        reads back as the old value - reported as "the platform did not keep
        what was written" rather than as a guarantee. It is the only portable
        signal there is.

        One measured limit, and a rule it enforces: on a degraded host session
        (#250) the first read-back after a write can answer empty or the
        previous value for a few hundred milliseconds, so a copy that LANDED
        can be reported as refused here. That is accepted rather than waited
        out, because the wait itself would block the calling thread - the
        main thread for every copy command and key handler - and blocking the
        main thread is a no-go for anything this package does, ever
        (lint:blocking-waits enforces it). A copy is a rare, user-initiated
        command; a false refusal on a degraded session costs one wrong console
        line, and the text is on the clipboard anyway. If the honest answer
        ever needs to survive that race, the design that fits is a deferred
        verification the console amends one frame later - product surface,
        chosen deliberately, never a sleep inside this call.

        A limit worth stating: the clipboard is a main-thread engine API. The
        read side has always been driven from a key handler, but a write from
        `CommandShell.RunCommand` can be driven off the main thread by a caller
        that chose to. The copy commands answer on whatever thread ran them,
        which is the shell's existing contract rather than one this changes.
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

            Compared, not assumed, and answered in the same dispatch: the write
            is a request to a platform that can decline it, and the value the
            clipboard reports back is the only answer available. A platform
            that has no clipboard leaves the previous value in place, a
            platform that truncated the text reports a different one, and a
            degraded session's read-back race (#250) answers stale - all three
            are reported as the refusal they may be, and none of them is
            waited out (see the class header).
         */
        public static bool TryWrite(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            GUIUtility.systemCopyBuffer = text;

            return string.Equals(GUIUtility.systemCopyBuffer, text, StringComparison.Ordinal);
        }
    }
}
