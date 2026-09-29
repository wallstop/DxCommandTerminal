namespace WallstopStudios.DxCommandTerminal.UI
{
    using UnityEngine;

    /*
        The keys that step a search, which is the one part of finding that a
        command cannot do for you.

        F3 is the key every tool that searches text has bound to "the next
        one", and Shift+F3 to the one before it. The command line holds panel
        focus for as long as the console is open, so the key arrives at the
        command field and the terminal routes it, exactly as it routes Page Up
        to the log.

        Nothing else is claimed. A key with no search set is left alone, which
        keeps it the developer's: F3 does nothing in a text field anywhere
        else either, and a message about a search nobody set would be noise on
        a key nobody pressed to search.
     */
    internal static class LogFindKeys
    {
        public static bool IsStepForward(KeyCode keyCode, bool shiftKey)
        {
            return KeyCode.F3 == keyCode && !shiftKey;
        }

        public static bool IsStepBackward(KeyCode keyCode, bool shiftKey)
        {
            return KeyCode.F3 == keyCode && shiftKey;
        }
    }
}
