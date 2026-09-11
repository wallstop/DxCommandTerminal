namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    ///     Parses one argument string into a value. Registered through
    ///     <see cref="CommandArg.RegisterParser{T}"/> and consulted by
    ///     <see cref="CommandArg.TryGet{T}"/>.
    /// </summary>
    public delegate bool CommandArgParser<T>(string input, out T parsed);
}
