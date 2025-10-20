namespace WallstopStudios.DxCommandTerminal.Backend.Profiles
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using UnityEngine;

    /// <summary>
    /// C# serialisable container describing allow/block lists for command registration.
    /// </summary>
    [Serializable]
    public sealed class TerminalCommandFilterConfiguration
    {
        [Tooltip("When true, built-in DX Command Terminal commands are registered automatically.")]
        public bool includeDefaultCommands = true;

        [Tooltip(
            "Commands that should always be available. Leave empty to allow all registered commands."
        )]
        public List<string> allowedCommands = new();

        [Tooltip("Commands that should be removed/disabled after registration.")]
        public List<string> blockedCommands = new();

        internal IReadOnlyList<string> Allowed => allowedCommands;

        internal IReadOnlyList<string> Blocked => blockedCommands;

        internal bool IncludeDefaults => includeDefaultCommands;
    }

    /// <summary>
    /// C# serialisable container describing allow/block lists for log routing into the terminal.
    /// </summary>
    [Serializable]
    public sealed class TerminalLogFilterConfiguration
    {
        [Tooltip(
            "Log types that should be routed into the terminal. Leave empty to allow all types."
        )]
        public List<TerminalLogType> allowedLogTypes = new();

        [Tooltip("Log types that should be filtered out of the terminal buffer.")]
        public List<TerminalLogType> blockedLogTypes = new();

        internal IReadOnlyList<TerminalLogType> Allowed => allowedLogTypes;

        internal IReadOnlyList<TerminalLogType> Blocked => blockedLogTypes;
    }
}
