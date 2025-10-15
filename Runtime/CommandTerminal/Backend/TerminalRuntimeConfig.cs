namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using Internal;
    using UnityEngine;

    [Flags]
    public enum TerminalRuntimeModeFlags
    {
        [Obsolete("None disables all runtime features. Choose explicit modes.")]
        None = 0,
        Editor = 1 << 0,
        Development = 1 << 1,
        Production = 1 << 2,
        All = Editor | Development | Production,
    }

    // Auto-create under Assets/Resources/Wallstop Studios/DxCommandTerminal/TerminalRuntimeConfig.asset
    [ScriptableSingletonPath("Wallstop Studios/DxCommandTerminal")]
    public sealed class TerminalRuntimeConfig : ScriptableObjectSingleton<TerminalRuntimeConfig>
    {
        // Fallbacks ensure static API remains usable even before an asset exists
#pragma warning disable CS0618 // Type or member is obsolete
        private static TerminalRuntimeModeFlags _fallbackMode = TerminalRuntimeModeFlags.None;
#pragma warning restore CS0618 // Type or member is obsolete
        private static bool _fallbackEditorAutoDiscover;

#pragma warning disable CS0618 // Type or member is obsolete
        [SerializeField]
        private TerminalRuntimeModeFlags _mode = TerminalRuntimeModeFlags.None;
#pragma warning restore CS0618 // Type or member is obsolete

        [SerializeField]
        private bool _editorAutoDiscover;

        // Instance-level accessors (for inspector/serialization)
#pragma warning disable CS0618 // Type or member is obsolete
        public TerminalRuntimeModeFlags Mode
        {
            get => _mode;
            set => _mode = value;
        }
#pragma warning restore CS0618 // Type or member is obsolete

        public bool EditorAutoDiscoverInstance
        {
            get => _editorAutoDiscover;
            set => _editorAutoDiscover = value;
        }

        // Backwards-compatible static API
#pragma warning disable CS0618 // Type or member is obsolete
        public static void SetMode(TerminalRuntimeModeFlags mode)
        {
            if (Instance != null)
            {
                Instance._mode = mode;
            }
            _fallbackMode = mode;
        }
#pragma warning restore CS0618 // Type or member is obsolete

        public static bool EditorAutoDiscover
        {
            get
            {
                if (Instance != null)
                {
                    return Instance._editorAutoDiscover;
                }
                return _fallbackEditorAutoDiscover;
            }
            set
            {
                if (Instance != null)
                {
                    Instance._editorAutoDiscover = value;
                }
                _fallbackEditorAutoDiscover = value;
            }
        }

#pragma warning disable CS0618 // Type or member is obsolete
        private static TerminalRuntimeModeFlags CurrentMode =>
            Instance != null ? Instance._mode : _fallbackMode;
#pragma warning restore CS0618 // Type or member is obsolete

        public static bool HasFlagNoAlloc(
            TerminalRuntimeModeFlags value,
            TerminalRuntimeModeFlags flag
        )
        {
            return ((int)value & (int)flag) == (int)flag;
        }

        public static bool ShouldEnableEditorFeatures()
        {
#if UNITY_EDITOR
            return HasFlagNoAlloc(CurrentMode, TerminalRuntimeModeFlags.Editor);
#else
            return false;
#endif
        }

        public static bool ShouldEnableDevelopmentFeatures()
        {
            return HasFlagNoAlloc(CurrentMode, TerminalRuntimeModeFlags.Development)
                && Debug.isDebugBuild;
        }

        public static bool ShouldEnableProductionFeatures()
        {
            return HasFlagNoAlloc(CurrentMode, TerminalRuntimeModeFlags.Production)
                && !Debug.isDebugBuild;
        }

        public static int TryAutoDiscoverParsers()
        {
            if (ShouldEnableEditorFeatures() && EditorAutoDiscover)
            {
                return CommandArg.DiscoverAndRegisterParsers(replaceExisting: false);
            }
            return 0;
        }

        internal static TerminalRuntimeModeFlags GetModeForTests()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (Instance != null)
            {
                return Instance._mode;
            }
            return _fallbackMode;
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
