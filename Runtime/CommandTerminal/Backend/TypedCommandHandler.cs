namespace WallstopStudios.DxCommandTerminal.Backend
{
    /// <summary>
    ///     Handler for a typed builder command. Runs only after every argument
    ///     parsed and validated against its definition, including the
    ///     build-time-validated default of any omitted optional argument;
    ///     <paramref name="arguments"/> exposes the parsed values.
    /// </summary>
    public delegate void TypedCommandHandler(
        CommandExecutionContext context,
        CommandArguments arguments
    );
}
