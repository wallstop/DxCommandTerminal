namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    ///     Named composites of <see cref="CommandExecutionContexts"/>. Flag
    ///     enum members stay single bits; sets compose them here.
    /// </summary>
    public static class CommandExecutionContextSets
    {
        /// <summary>Gameplay environments: Editor Play Mode and players.</summary>
        public const CommandExecutionContexts Gameplay =
            CommandExecutionContexts.EditorPlayMode | CommandExecutionContexts.Player;

        /// <summary>Every environment.</summary>
        public const CommandExecutionContexts All =
            Gameplay | CommandExecutionContexts.EditorEditMode;
    }
}
