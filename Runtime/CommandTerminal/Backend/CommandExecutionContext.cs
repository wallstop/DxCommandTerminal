namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    ///     Immutable environment identification for one command invocation or
    ///     completion request, plus a caller-supplied context object. The
    ///     shell receives one per invocation; new-style command handlers and
    ///     completion providers read it instead of probing the environment
    ///     themselves.
    /// </summary>
    public readonly struct CommandExecutionContext
    {
        /// <summary>
        ///     The environment the invocation runs in. Editors resolve Edit
        ///     Mode versus Play Mode; players always report
        ///     <see cref="CommandExecutionContexts.Player"/>.
        /// </summary>
        public CommandExecutionContexts Environment { get; }

        /// <summary>
        ///     Caller-supplied context object or services reference, valid for
        ///     the current invocation only. Null unless the caller supplied one.
        /// </summary>
        public object UserContext { get; }

        public CommandExecutionContext(
            CommandExecutionContexts environment,
            object userContext = null
        )
        {
            Environment = environment;
            UserContext = userContext;
        }

        /// <summary>
        ///     Resolves the current invocation context. Tests and Editor tooling
        ///     may replace the ambient source through the internal
        ///     <see cref="AmbientContextProvider"/> hook; otherwise the Unity
        ///     environment decides.
        /// </summary>
        public static CommandExecutionContext Current =>
            AmbientContextProvider?.Invoke() ?? new CommandExecutionContext(ResolveEnvironment());

        /// <summary>
        ///     Internal test hook. When set, <see cref="Current"/> delegates to
        ///     it; must not be consumed by production call sites.
        /// </summary>
        internal static Func<CommandExecutionContext> AmbientContextProvider { get; set; }

        public bool IsEligibleFor(CommandExecutionContexts allowedContexts)
        {
            return (Environment & allowedContexts) != 0;
        }

        private static CommandExecutionContexts ResolveEnvironment()
        {
#if UNITY_EDITOR
            return EditorApplication.isPlaying
                ? CommandExecutionContexts.EditorPlayMode
                : CommandExecutionContexts.EditorEditMode;
#else
            return CommandExecutionContexts.Player;
#endif
        }
    }
}
