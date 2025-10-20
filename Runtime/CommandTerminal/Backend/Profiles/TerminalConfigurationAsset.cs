namespace WallstopStudios.DxCommandTerminal.Backend.Profiles
{
    using Backend;
    using UnityEngine;

    /// <summary>
    /// Aggregates configuration profiles for terminals to feed the new settings provider/factory pipeline.
    /// Backwards-compatible: can wrap existing profile fields used by TerminalUI during migration.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TerminalConfigurationAsset",
        menuName = "DXCommandTerminal/Terminal Configuration Asset",
        order = 400
    )]
    public sealed class TerminalConfigurationAsset : ScriptableObject, ITerminalSettingsProvider
    {
        [Tooltip("Runtime profile providing buffer sizes and command/log filters")]
        public TerminalRuntimeProfile runtimeProfile;

        public TerminalRuntimeSettings BuildSettings()
        {
            return runtimeProfile != null
                ? runtimeProfile.BuildSettings()
                : new TerminalRuntimeSettings(
                    logCapacity: 0,
                    historyCapacity: 0,
                    blockedLogTypes: System.Array.Empty<TerminalLogType>(),
                    allowedLogTypes: System.Array.Empty<TerminalLogType>(),
                    blockedCommands: System.Array.Empty<string>(),
                    allowedCommands: System.Array.Empty<string>(),
                    includeDefaultCommands: true
                );
        }
    }
}
