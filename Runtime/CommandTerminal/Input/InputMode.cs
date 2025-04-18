namespace WallstopStudios.DxCommandTerminal.Input
{
    using System;

    public enum InputMode
    {
        [Obsolete]
        None = 0,
#if !ENABLE_LEGACY_INPUT_MANAGER
        [Obsolete]
#endif
        LegacyInputSystem = 1 << 0
        ,
#if !ENABLE_INPUT_SYSTEM
        [Obsolete]
#endif
        NewInputSystem = 1 << 1
        ,
    }
}
