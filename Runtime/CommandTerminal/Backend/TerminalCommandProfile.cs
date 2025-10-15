namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;
    using UI;
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "TerminalCommandProfile",
        menuName = "DXCommandTerminal/Terminal Command Profile",
        order = 480
    )]
    public sealed class TerminalCommandProfile : ScriptableObject
    {
        [Header("Commands")]
        public bool ignoreDefaultCommands;

        [Tooltip("Commands that should be disabled for this terminal instance.")]
        public List<string> disabledCommands = new();

        [Tooltip("Log types to ignore when routing into the terminal buffer.")]
        public List<TerminalLogType> ignoredLogTypes = new();

        public void ApplyTo(TerminalUI terminal)
        {
            if (terminal == null)
            {
                return;
            }

            terminal.ignoreDefaultCommands = ignoreDefaultCommands;
            terminal.SetDisabledCommandsForTests(disabledCommands);
            terminal.SetIgnoredLogTypesForTests(ignoredLogTypes);
        }
    }
}
