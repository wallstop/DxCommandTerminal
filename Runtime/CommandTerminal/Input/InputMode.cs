namespace WallstopStudios.DxCommandTerminal.Input
{
    using System;

    public enum InputMode
    {
        [Obsolete]
        None = 0,
        LegacyInputSystem = 1,
#if !ENABLE_INPUT_SYSTEM
        [Obsolete]
#endif
        NewInputSystem = 2
        ,
    }
}
