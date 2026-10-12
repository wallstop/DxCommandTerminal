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
        reads back as the old value - a false negative on a working platform,
        reported as "the platform did not keep what was written" rather than as
        a guarantee. It is the only portable signal there is.

        A limit worth stating: the clipboard is a main-thread engine API. The
        read side has always been driven from a key handler, but a write from
        `CommandShell.RunCommand` can be driven off the main thread by a caller
        that chose to. The copy commands answer on whatever thread ran them,
        which is the shell's existing contract rather than one this changes.
     */
    internal static class TerminalClipboard
    {
        /*
            The read-back retry budget: re-reads after the write, so a copy
            that landed is not reported as refused because the first read
            raced the OS write. The pinned host's settle window measured
            ~100-400 ms (a 150 ms wait answered correctly every time, one
            400 ms wait still answered the previous value once), so the
            delays grow 50 -> 800 ms, ~1.55 s worst case on the path that
            reports a refusal anyway. A platform that will never keep the
            text answers the same wrong value on every read and is not
            changed by waiting.
         */
        private static readonly int[] WriteReadRetryDelaysMs = { 50, 100, 200, 400, 800 };

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

            The read-back is retried under a small, bounded budget. Measured on
            the pinned Windows host (#250): the write lands - the OS clipboard
            holds it - but the first immediate read-back can answer the
            previous value or empty, and a read a moment later answers the
            written value. Reporting a refusal off that first race would tell
            a developer their platform declined a copy that landed, so a
            mismatched read is re-read a few times before false is reported.
            A platform that truly has no clipboard, or one that refuses
            without a gesture, answers the same wrong value on every read, so
            the budget only costs it the settle wait on the path that reports
            the refusal anyway.
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

                The re-reads exist for the read-back race measured on #250's
                host: the write lands, but a read issued in the same breath
                can answer empty or the previous value, and the wrong answer
                can persist for several hundred milliseconds. Each retry only
                re-reads; the write itself is never repeated, so a platform
                that declines cannot be talked into accepting, and a platform
                whose answer never changes costs the whole budget once, on
                the path that reports the refusal anyway.
             */
            if (string.Equals(GUIUtility.systemCopyBuffer, text, StringComparison.Ordinal))
            {
                return true;
            }

            for (int attempt = 0; attempt < WriteReadRetryDelaysMs.Length; ++attempt)
            {
                SpinWait(WriteReadRetryDelaysMs[attempt]);
                if (string.Equals(GUIUtility.systemCopyBuffer, text, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /*
            A time-ordered wait that keeps the caller on the main thread. The
            command shell can run on any thread a caller chose, so this is a
            spin, not a sleep: Thread.Sleep off the main thread is fine, but
            the read-back race it waits out is a main-thread message-pump
            artifact, and a spin measures the same wall time everywhere the
            contract runs.
         */
        private static void SpinWait(int milliseconds)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            long end = start + (long)(milliseconds / 1000d * System.Diagnostics.Stopwatch.Frequency);
            while (System.Diagnostics.Stopwatch.GetTimestamp() < end) { }
        }
    }
}
