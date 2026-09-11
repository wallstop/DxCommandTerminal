namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     Handler for a context-aware command. Receives the immutable
    ///     execution context and a read-only, invocation-scoped view over the
    ///     parsed arguments. The borrowed view is valid only during the
    ///     callback; copy out anything that must outlive it.
    /// </summary>
    public delegate void CommandHandler(
        CommandExecutionContext context,
        BorrowedCommandArguments arguments
    );
}
