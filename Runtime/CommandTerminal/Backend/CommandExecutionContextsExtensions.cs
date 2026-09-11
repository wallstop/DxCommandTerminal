namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    ///     Readable flag checks for <see cref="CommandExecutionContexts"/>,
    ///     keeping bitwise math out of call sites.
    /// </summary>
    public static class CommandExecutionContextsExtensions
    {
        /// <summary>
        ///     True when every bit of <paramref name="flag"/> is set. Matches
        ///     <c>Enum.HasFlag</c> without boxing.
        /// </summary>
        public static bool HasFlagNoAlloc(
            this CommandExecutionContexts value,
            CommandExecutionContexts flag
        )
        {
            return ((int)value & (int)flag) == (int)flag;
        }
    }
}
