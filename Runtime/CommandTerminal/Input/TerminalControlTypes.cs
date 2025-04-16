namespace WallstopStudios.DxCommandTerminal.Input
{
    using System;

    public enum TerminalControlTypes
    {
        [Obsolete]
        None = 0,
        Close = 1,
        EnterCommand = 2,
        Previous = 3,
        Next = 4,
        ToggleFull = 5,
        ToggleSmall = 6,
        CompleteForward = 7,
        CompleteBackward = 8,
    }
}
