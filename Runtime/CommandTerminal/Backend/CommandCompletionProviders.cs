namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;

    /// <summary>Helpers for composing completion providers.</summary>
    public static class CommandCompletionProviders
    {
        /// <summary>
        ///     Builds a one-stage provider: stage 0 of an invocation is
        ///     completed by <paramref name="stage"/>; later stages produce no
        ///     candidates.
        /// </summary>
        public static CommandCompletionProvider Staged(CommandCompletionProvider stage)
        {
            if (stage == null)
            {
                throw new ArgumentNullException(nameof(stage));
            }

            return (in CommandCompletionContext context, List<CommandCompletion> results) =>
            {
                if (context.ActiveArgumentIndex == 0)
                {
                    stage(context, results);
                }
            };
        }

        /// <summary>
        ///     Builds a two-stage provider: stage <c>k</c> of an invocation
        ///     (the k-th argument) is completed by the k-th provider; later
        ///     stages produce no candidates.
        /// </summary>
        public static CommandCompletionProvider Staged(
            CommandCompletionProvider stage0,
            CommandCompletionProvider stage1
        )
        {
            if (stage0 == null)
            {
                throw new ArgumentNullException(nameof(stage0));
            }

            if (stage1 == null)
            {
                throw new ArgumentNullException(nameof(stage1));
            }

            return (in CommandCompletionContext context, List<CommandCompletion> results) =>
            {
                switch (context.ActiveArgumentIndex)
                {
                    case 0:
                    {
                        stage0(context, results);
                        break;
                    }
                    case 1:
                    {
                        stage1(context, results);
                        break;
                    }
                }
            };
        }

        /// <summary>
        ///     Builds a three-stage provider; stages beyond the third produce
        ///     no candidates.
        /// </summary>
        public static CommandCompletionProvider Staged(
            CommandCompletionProvider stage0,
            CommandCompletionProvider stage1,
            CommandCompletionProvider stage2
        )
        {
            if (stage0 == null)
            {
                throw new ArgumentNullException(nameof(stage0));
            }

            if (stage1 == null)
            {
                throw new ArgumentNullException(nameof(stage1));
            }

            if (stage2 == null)
            {
                throw new ArgumentNullException(nameof(stage2));
            }

            return (in CommandCompletionContext context, List<CommandCompletion> results) =>
            {
                switch (context.ActiveArgumentIndex)
                {
                    case 0:
                    {
                        stage0(context, results);
                        break;
                    }
                    case 1:
                    {
                        stage1(context, results);
                        break;
                    }
                    case 2:
                    {
                        stage2(context, results);
                        break;
                    }
                }
            };
        }

        /// <summary>
        ///     Builds a provider from an explicit stage list for four or more
        ///     stages: stage <c>k</c> of an invocation is completed by
        ///     <c>stages[k]</c>; requests beyond the list produce no
        ///     candidates. Null stages produce no candidates, so commands can
        ///     leave individual arguments to history-based completion.
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
