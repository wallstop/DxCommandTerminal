namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Execution environments a command may be eligible for. Eligibility is
    ///     checked when a command is dispatched; a command whose context set
    ///     does not include the current environment is rejected with an error
    ///     instead of running.
    /// </summary>
    [Flags]
    public enum CommandExecutionContexts
    {
        /// <summary>The command never runs. Rejects every dispatch.</summary>
        None = 0,

        /// <summary>The Unity Editor outside Play Mode.</summary>
        EditorEditMode = 1 << 0,

        /// <summary>The Unity Editor inside Play Mode.</summary>
        EditorPlayMode = 1 << 1,

        /// <summary>A built player, including development builds.</summary>
        Player = 1 << 2,

        /// <summary>Gameplay environments: Editor Play Mode and players.</summary>
        Gameplay = EditorPlayMode | Player,

        /// <summary>Every environment. The default for registrations that carry
        /// no explicit context metadata, preserving their previous availability.</summary>
        All = EditorEditMode | EditorPlayMode | Player,
    }
}
