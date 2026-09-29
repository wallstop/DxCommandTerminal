namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;

    /* What a key asked the log view to do. A key the log does not answer is
       not a member here: TryResolve reports that with a false return, so the
       only values that exist are the scrolls a log can make. The ordinals are
       explicit so a member added later cannot renumber one already compiled
       against. */
    internal enum LogScrollIntent
    {
        [Obsolete("A key the log does not answer has no intent; TryResolve returns false")]
        None = 0,

        /* One viewport of the log, in the direction named. */
        PageUp = 1,
        PageDown = 2,

        /* The oldest line the buffer still holds, and its newest. */
        ToStart = 3,
        ToEnd = 4,
    }
}
