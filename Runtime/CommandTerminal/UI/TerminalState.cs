namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;

    public enum TerminalState
    {
        [Obsolete("Use a valid value")]
        Unknown = 0,
        Closed = 1,
        OpenSmall = 2,
        OpenFull = 3,
    }
}
