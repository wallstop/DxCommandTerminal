namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    public enum HintDisplayMode
    {
        [Obsolete("Use a valid value")]
        Unknown = 0,
        Always = 1,
        AutoCompleteOnly = 2,
        Never = 3,
    }
}
