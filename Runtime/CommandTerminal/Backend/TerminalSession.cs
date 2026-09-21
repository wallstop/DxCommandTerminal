namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    ///     Owns the backend objects exposed through the <see cref="Terminal"/>
    ///     facade: the log buffer, history, shell, and auto-complete. Applying a
    ///     configuration creates missing objects, resizes and resynchronizes
    ///     existing ones, and reports whether the shell needed reconfiguration,
    ///     so UI components stay thin delegates over this owner.
    /// </summary>
    internal sealed class TerminalSession
    {
        /// <summary>
        ///     The process-wide session backing the <see cref="Terminal"/> facade.
        /// </summary>
        public static TerminalSession Current { get; } = new();

        private static readonly TerminalLogType[] EmptyLogTypes = Array.Empty<TerminalLogType>();

        private static readonly string[] EmptyStrings = Array.Empty<string>();

        public CommandLog Buffer { get; internal set; }

        public CommandHistory History { get; internal set; }

        public CommandShell Shell { get; internal set; }

        public CommandAutoComplete AutoComplete { get; internal set; }

        /// <summary>
        ///     Applies <paramref name="config"/> to the backend objects. With
        ///     <paramref name="force"/>, every object is recreated; otherwise
        ///     existing objects are reused, resized to the configured capacities,
        ///     and resynchronized with the configured filters. Shell auto-command
        ///     configuration is applied immediately while discovery stays deferred
        ///     to the first command request.
        /// </summary>
        /// <returns>
        ///     True when the shell's auto-command configuration was reapplied and
        ///     callers holding completion state should reset it.
        /// </returns>
        public bool Apply(Config config, bool force)
        {
            int logBufferSize = Mathf.Max(0, config.LogBufferSize);
            if (force || Buffer == null)
            {
                Buffer = new CommandLog(logBufferSize, config.IgnoredLogTypes)
                {
                    stackTraceMode = config.StackTraceMode,
                };
            }
            else
            {
                if (Buffer.Capacity != logBufferSize)
                {
                    Buffer.Resize(logBufferSize);
                }

                if (Buffer.stackTraceMode != config.StackTraceMode)
                {
                    Buffer.stackTraceMode = config.StackTraceMode;
                }

                if (!Buffer.ignoredLogTypes.SetEquals(config.IgnoredLogTypes ?? EmptyLogTypes))
                {
                    Buffer.ignoredLogTypes.Clear();
                    Buffer.ignoredLogTypes.UnionWith(config.IgnoredLogTypes ?? EmptyLogTypes);
                }
            }

            int historyBufferSize = Mathf.Max(0, config.HistoryBufferSize);
            if (force || History == null)
            {
                History = new CommandHistory(historyBufferSize);
            }
            else if (History.Capacity != historyBufferSize)
            {
                History.Resize(historyBufferSize);
            }

            if (force || Shell == null)
            {
                Shell = new CommandShell(History);
            }

            if (force || AutoComplete == null)
            {
                AutoComplete = new CommandAutoComplete(History, Shell);
            }

            bool reconfigureShell =
                Shell.IgnoringDefaultCommands != config.IgnoreDefaultCommands
                || !Shell.AutoCommandsRegistered
                || !Shell.IgnoredCommands.SetEquals(config.DisabledCommands ?? EmptyStrings);
            if (reconfigureShell)
            {
                Shell.ClearAutoRegisteredCommands();
                Shell.InitializeAutoRegisteredCommands(
                    ignoredCommands: config.DisabledCommands,
                    ignoreDefaultCommands: config.IgnoreDefaultCommands,
                    deferRegistration: true
                );
            }

            return reconfigureShell;
        }

        /// <summary>
        ///     Explicit readiness boundary for callers without a TerminalUI
        ///     component (custom bootstrap code): applies
        ///     <paramref name="config"/> to the backend objects and completes
        ///     deferred auto-command registration synchronously, so the first
        ///     command request and logging work before any UI enables.
        ///     Idempotent: repeated calls reuse the existing objects.
        /// </summary>
        public void EnsureReady(Config config, bool force)
        {
            Apply(config, force);
            Shell.EnsureAutoCommandsRegistered();
        }

        /// <summary>
        ///     Drops the backend objects so the next <see cref="Apply"/>
        ///     recreates them from scratch. Used by the play-session reset:
        ///     with disabled domain reload, a previous Play Mode session's
        ///     backends (and any registrations made through them) would
        ///     otherwise survive into the next session.
        /// </summary>
        public void ResetState()
        {
            Buffer = null;
            History = null;
            Shell = null;
            AutoComplete = null;
        }

        /// <summary>
        ///     Serialized terminal configuration for one <see cref="Apply"/> call.
        ///     Null lists are treated as empty.
        /// </summary>
        public readonly struct Config
        {
            /// <summary>
            ///     The default buffer capacities, shared by every component
            ///     that owns serialized defaults so the defaults cannot drift
            ///     apart (<see cref="UI.TerminalUI"/>, <see cref="UI.TerminalSettings"/>).
            /// </summary>
            public const int DefaultLogBufferSize = 256;

            /// <summary>
            ///     The default history capacity. See <see cref="DefaultLogBufferSize"/>.
            /// </summary>
            public const int DefaultHistoryBufferSize = 512;

            /// <summary>
            ///     The default configuration. Used when a component without
            ///     serialized configuration of its own (a palette with no
            ///     settings asset assigned) bootstraps the session.
            /// </summary>
            public static Config Default { get; } =
                new(DefaultLogBufferSize, DefaultHistoryBufferSize, null, null, false);

            public int LogBufferSize { get; }

            public int HistoryBufferSize { get; }

            public IReadOnlyList<TerminalLogType> IgnoredLogTypes { get; }

            public IReadOnlyList<string> DisabledCommands { get; }

            public bool IgnoreDefaultCommands { get; }

            public TerminalStackTraceMode StackTraceMode { get; }

            public Config(
                int logBufferSize,
                int historyBufferSize,
                IReadOnlyList<TerminalLogType> ignoredLogTypes,
                IReadOnlyList<string> disabledCommands,
                bool ignoreDefaultCommands,
                TerminalStackTraceMode stackTraceMode = TerminalStackTraceMode.All
            )
            {
                LogBufferSize = logBufferSize;
                HistoryBufferSize = historyBufferSize;
                IgnoredLogTypes = ignoredLogTypes;
                DisabledCommands = disabledCommands;
                IgnoreDefaultCommands = ignoreDefaultCommands;
                StackTraceMode = stackTraceMode;
            }
        }
    }
}
