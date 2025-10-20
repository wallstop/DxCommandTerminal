namespace WallstopStudios.DxCommandTerminal.Backend
{
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
        [SerializeField]
        private Profiles.TerminalCommandFilterConfiguration _commandFilters =
            new Profiles.TerminalCommandFilterConfiguration();

        [Header("Logs")]
        [SerializeField]
        private Profiles.TerminalLogFilterConfiguration _logFilters =
            new Profiles.TerminalLogFilterConfiguration();

        public Profiles.TerminalCommandFilterConfiguration CommandFilters => _commandFilters;

        public Profiles.TerminalLogFilterConfiguration LogFilters => _logFilters;

        public void ApplyTo(TerminalUI terminal)
        {
            if (terminal == null)
            {
                return;
            }

            terminal.ignoreDefaultCommands = !_commandFilters.IncludeDefaults;
            terminal.SetAllowedCommandsForTests(_commandFilters.Allowed);
            terminal.SetBlockedCommandsForTests(_commandFilters.Blocked);
            terminal.SetAllowedLogTypesForTests(_logFilters.Allowed);
            terminal.SetBlockedLogTypesForTests(_logFilters.Blocked);
        }
    }
}
