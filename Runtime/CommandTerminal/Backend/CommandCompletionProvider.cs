namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
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

    /// <summary>Helpers for composing completion providers.</summary>
    public static class CommandCompletionProviders
    {
        /// <summary>
        ///     Builds a staged provider: stage <c>k</c> of an invocation
        ///     (the k-th argument) is completed by <c>stages[k]</c>. Requests
        ///     beyond the last stage produce no candidates, and null stages
        ///     produce none either, so commands can leave individual
        ///     arguments to history-based completion.
        /// </summary>
        public static CommandCompletionProvider Staged(params CommandCompletionProvider[] stages)
        {
            if (stages == null)
            {
                throw new ArgumentNullException(nameof(stages));
            }

            return (in CommandCompletionContext context, List<CommandCompletion> results) =>
            {
                int stage = context.ActiveArgumentIndex;
                if (stages.Length <= stage)
                {
                    return;
                }

                stages[stage]?.Invoke(context, results);
            };
        }
    }
}
