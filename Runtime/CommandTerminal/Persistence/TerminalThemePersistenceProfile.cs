namespace WallstopStudios.DxCommandTerminal.Persistence
{
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "TerminalThemePersistenceProfile",
        menuName = "DXCommandTerminal/Terminal Theme Persistence Profile",
        order = 490
    )]
    public sealed class TerminalThemePersistenceProfile : ScriptableObject
    {
        [Header("Persistence")]
        public bool enablePersistence = true;

        [Tooltip("Automatically hydrate terminal theme/font from storage when enabled.")]
        public bool loadOnStart = true;

        [Tooltip("Continuously save terminal state while running.")]
        public bool savePeriodically = true;

        [Min(0f)]
        public float savePeriod = 1f;

        [Tooltip("Optional file name override for the persisted theme data.")]
        public string fileName = "TerminalTheme.json";
    }
}
