namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    public enum SceneObjectAmbiguityPolicy
    {
        [Obsolete("Use a valid value")]
        Unknown = 0,
        FirstMatch = 1,
        RequireUnique = 2,
    }
}
