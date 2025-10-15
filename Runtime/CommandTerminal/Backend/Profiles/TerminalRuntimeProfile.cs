namespace WallstopStudios.DxCommandTerminal.Backend.Profiles
{
    using System.Collections.Generic;
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
        private bool _ignoreDefaultCommands;

        [SerializeField]
        private List<TerminalLogType> _ignoredLogTypes = new();

        [SerializeField]
        private List<string> _disabledCommands = new();

        public int LogBufferSize => Mathf.Max(0, _logBufferSize);

        public int HistoryBufferSize => Mathf.Max(0, _historyBufferSize);

        public bool IgnoreDefaultCommands => _ignoreDefaultCommands;

        public IReadOnlyList<TerminalLogType> IgnoredLogTypes => _ignoredLogTypes;

        public IReadOnlyList<string> DisabledCommands => _disabledCommands;

        internal TerminalRuntimeSettings BuildSettings()
        {
            return new TerminalRuntimeSettings(
                LogBufferSize,
                HistoryBufferSize,
                _ignoredLogTypes,
                _disabledCommands,
                _ignoreDefaultCommands
            );
        }

#if UNITY_EDITOR
        internal void ConfigureForTests(
            int logBufferSize,
            int historyBufferSize,
            bool ignoreDefaults,
            IReadOnlyList<TerminalLogType> ignoredLogTypes,
            IReadOnlyList<string> disabledCommands
        )
        {
            _logBufferSize = logBufferSize;
            _historyBufferSize = historyBufferSize;
            _ignoreDefaultCommands = ignoreDefaults;

            _ignoredLogTypes.Clear();
            if (ignoredLogTypes != null)
            {
                for (int i = 0; i < ignoredLogTypes.Count; ++i)
                {
                    _ignoredLogTypes.Add(ignoredLogTypes[i]);
                }
            }

            _disabledCommands.Clear();
            if (disabledCommands != null)
            {
                for (int i = 0; i < disabledCommands.Count; ++i)
                {
                    _disabledCommands.Add(disabledCommands[i]);
                }
            }
        }
#endif
    }
}
