namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using System.Runtime.CompilerServices;

    [Flags]
    public enum TerminalHistoryFadeTargets
    {
        None = 0,
        SmallTerminal = 1 << 0,
        FullTerminal = 1 << 1,
        Launcher = 1 << 2,
    }

    internal static class TerminalHistoryFadeTargetsExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlagNoAlloc(
            this TerminalHistoryFadeTargets value,
            TerminalHistoryFadeTargets flag
        )
        {
            if (flag == TerminalHistoryFadeTargets.None)
            {
                return value == TerminalHistoryFadeTargets.None;
            }

            return (value & flag) == flag;
        }
    }
}
