namespace WallstopStudios.DxCommandTerminal.UI
{
    using UnityEngine;

    /*
        How a key moves the log view, and what the view's scroll position
        becomes.

        The command line owns panel focus for as long as the console is open,
        so Page Up, Page Down, Home, and End all arrive at the command field
        instead of at the log. Routing them is the terminal's job, and it is
        split out here so the decision is a table rather than a branch buried
        in a key handler.

        What the log does not take is as much of the rule as what it does.
        Home and End move the caret in a text field, and a developer editing
        the command they are about to run needs that, so plain Home and End
        stay with the field and the log is reached with Ctrl (or Cmd) held -
        the same chord that means "the whole document" everywhere else. Page Up
        and Page Down are free: a one-line field has nothing to page.

        A key the log does not want resolves to None and is left alone, which
        is what keeps typing, history recall, completion, and closing working
        exactly as they did.
     */
    internal static class LogScrollKeys
    {
        /*
            Where a scroll value wants to land. The value is clamped to the
            scroller's own range on the way, so a page that runs past either
            end stops there rather than leaving the view past its content.
         */
        public static float Target(LogScrollIntent intent, float value, float highValue, float page)
        {
            return intent switch
            {
                LogScrollIntent.ToStart => 0f,
                LogScrollIntent.ToEnd => highValue,
                LogScrollIntent.PageUp => Clamp(value - page, highValue),
                LogScrollIntent.PageDown => Clamp(value + page, highValue),
                _ => Clamp(value, highValue),
            };
        }

        /*
            Which scroll a key asks for. False means the key is not the log's
            to answer and the caller leaves it alone, which is what keeps
            typing, history recall, completion, and closing untouched.

            `commandKey` is the platform's own command modifier, so this is
            Ctrl on Windows and Linux and Cmd on macOS without the terminal
            having to know which.
         */
        public static bool TryResolve(KeyCode keyCode, bool commandKey, out LogScrollIntent intent)
        {
            switch (keyCode)
            {
                case KeyCode.PageUp:
                    intent = LogScrollIntent.PageUp;
                    return true;
                case KeyCode.PageDown:
                    intent = LogScrollIntent.PageDown;
                    return true;
                case KeyCode.Home when commandKey:
                    intent = LogScrollIntent.ToStart;
                    return true;
                case KeyCode.End when commandKey:
                    intent = LogScrollIntent.ToEnd;
                    return true;
                default:
                    intent = default;
                    return false;
            }
        }

        /*
            Clamped on both sides. The scroller clamps a write to the extent it
            holds, but a value computed from a viewport height taken before a
            layout pass can land outside the range, and the tail follower reads
            whatever the scroller ended up holding as the developer's position.
         */
        private static float Clamp(float value, float highValue)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return highValue < value ? highValue : value;
        }
    }
}
