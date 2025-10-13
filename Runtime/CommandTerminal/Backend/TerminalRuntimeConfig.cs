namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using UnityEngine;

    [Flags]
    public enum TerminalRuntimeModeFlags : int
    {
        [Obsolete("None disables all runtime features. Choose explicit modes.")]
        None = 0,
        Editor = 1,
        Development = 2,
        Production = 4,
        All = Editor | Development | Production,
    }

    public static class TerminalRuntimeConfig
    {
#pragma warning disable CS0618 // Type or member is obsolete
        public static TerminalRuntimeModeFlags Mode { get; private set; } =
            TerminalRuntimeModeFlags.None;
#pragma warning restore CS0618 // Type or member is obsolete
        public static bool EditorAutoDiscover { get; set; }

        public static void SetMode(TerminalRuntimeModeFlags mode)
        {
            Mode = mode;
        }

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
            return HasFlagNoAlloc(Mode, TerminalRuntimeModeFlags.Editor);
#else
            return false;
#endif
        }

        public static bool ShouldEnableDevelopmentFeatures()
        {
            return HasFlagNoAlloc(Mode, TerminalRuntimeModeFlags.Development) && Debug.isDebugBuild;
        }

        public static bool ShouldEnableProductionFeatures()
        {
            return HasFlagNoAlloc(Mode, TerminalRuntimeModeFlags.Production) && !Debug.isDebugBuild;
        }

        public static int TryAutoDiscoverParsers()
        {
            if (ShouldEnableEditorFeatures() && EditorAutoDiscover)
            {
                return CommandArg.DiscoverAndRegisterParsers(replaceExisting: false);
            }
            return 0;
        }
    }
}
