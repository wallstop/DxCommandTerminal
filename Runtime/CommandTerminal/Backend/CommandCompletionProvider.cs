namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;

    /// <summary>
    ///     Synchronous completion callback for a registered command. Receives
    ///     the completion context and fills a caller-owned, reusable result
    ///     buffer with zero or more candidates. Providers own matching,
    ///     ranking, and dynamic queries; the shell deduplicates and preserves
    ///     provider order.
    /// </summary>
    /// <remarks>
    ///     Providers run synchronously on the caller's thread. Exceptions are
    ///     contained by the shell and reported, and never disable later
    ///     completion requests.
    /// </remarks>
    public delegate void CommandCompletionProvider(
        in CommandCompletionContext context,
        List<CommandCompletion> results
    );
}
