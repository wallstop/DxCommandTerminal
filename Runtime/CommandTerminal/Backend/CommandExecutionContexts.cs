namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Execution environments a command may be eligible for. Members are
    ///     single bits; named composites live in
    ///     <see cref="CommandExecutionContextSets"/>.
    /// </summary>
    [Flags]
    public enum CommandExecutionContexts
    {
        /// <summary>The command never runs. Rejects every dispatch.</summary>
        None = 0,

        /// <summary>The Unity Editor outside Play Mode.</summary>
        EditorEditMode = 1,

        /// <summary>The Unity Editor inside Play Mode.</summary>
        EditorPlayMode = 2,

        /// <summary>A built player, including development builds.</summary>
        Player = 4,
    }
}
