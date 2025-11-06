namespace WallstopStudios.DxCommandTerminal.Backend.Profiles
{
    using System.Collections.Generic;
    using Backend;
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "TerminalRuntimeProfile",
        menuName = "DXCommandTerminal/Terminal Runtime Profile",
        order = 450
    )]
    public sealed class TerminalRuntimeProfile : ScriptableObject
    {
        [SerializeField]
        [Min(0)]
        private int _logBufferSize = 256;

        [SerializeField]
        [Min(0)]
        private int _historyBufferSize = 512;

        [SerializeField]
        private TerminalCommandFilterConfiguration _commandFilters = new();

        [SerializeField]
        private TerminalLogFilterConfiguration _logFilters = new();

        public int LogBufferSize => Mathf.Max(0, _logBufferSize);

        public int HistoryBufferSize => Mathf.Max(0, _historyBufferSize);

        public bool IncludeDefaultCommands => _commandFilters?.IncludeDefaults ?? true;

        public IReadOnlyList<string> AllowedCommands =>
            _commandFilters?.Allowed ?? System.Array.Empty<string>();

        public IReadOnlyList<string> BlockedCommands =>
            _commandFilters?.Blocked ?? System.Array.Empty<string>();

        public IReadOnlyList<TerminalLogType> AllowedLogTypes =>
            _logFilters?.Allowed ?? System.Array.Empty<TerminalLogType>();

        public IReadOnlyList<TerminalLogType> BlockedLogTypes =>
            _logFilters?.Blocked ?? System.Array.Empty<TerminalLogType>();

        internal TerminalRuntimeSettings BuildSettings()
        {
            return new TerminalRuntimeSettings(
                LogBufferSize,
                HistoryBufferSize,
                BlockedLogTypes,
                AllowedLogTypes,
                BlockedCommands,
                AllowedCommands,
                IncludeDefaultCommands
            );
        }

#if UNITY_EDITOR
        internal void ConfigureForTests(
            int logBufferSize,
            int historyBufferSize,
            bool includeDefaults,
            IReadOnlyList<TerminalLogType> allowedLogTypes,
            IReadOnlyList<TerminalLogType> blockedLogTypes,
            IReadOnlyList<string> allowedCommands,
            IReadOnlyList<string> blockedCommands
        )
        {
            _logBufferSize = logBufferSize;
            _historyBufferSize = historyBufferSize;
            if (_commandFilters == null)
            {
                _commandFilters = new TerminalCommandFilterConfiguration();
            }

            if (_logFilters == null)
            {
                _logFilters = new TerminalLogFilterConfiguration();
            }

            _commandFilters.includeDefaultCommands = includeDefaults;
            CopyList(allowedCommands, _commandFilters.allowedCommands);
            CopyList(blockedCommands, _commandFilters.blockedCommands);

            CopyList(allowedLogTypes, _logFilters.allowedLogTypes);
            CopyList(blockedLogTypes, _logFilters.blockedLogTypes);
        }

        private static void CopyList<T>(IReadOnlyList<T> source, List<T> destination)
        {
            destination.Clear();
            if (source == null)
            {
                return;
            }

            for (int i = 0; i < source.Count; ++i)
            {
                destination.Add(source[i]);
            }
        }
#endif
    }
}
