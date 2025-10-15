namespace WallstopStudios.DxCommandTerminal.UI
{
    using Backend;
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "TerminalAppearanceProfile",
        menuName = "DXCommandTerminal/Terminal Appearance Profile",
        order = 470
    )]
    public sealed class TerminalAppearanceProfile : ScriptableObject
    {
        [Header("Buttons")]
        public bool showGUIButtons = true;
        public string runButtonText = "run";
        public string closeButtonText = "close";
        public string smallButtonText = "small";
        public string fullButtonText = "full";
        public string launcherButtonText = "launcher";

        [Header("Hints")]
        public HintDisplayMode hintDisplayMode = HintDisplayMode.AutoCompleteOnly;
        public bool makeHintsClickable = true;

        [Header("History Fade")]
        public TerminalHistoryFadeTargets historyFadeTargets =
            TerminalHistoryFadeTargets.SmallTerminal
            | TerminalHistoryFadeTargets.FullTerminal
            | TerminalHistoryFadeTargets.Launcher;

        [Header("Cursor")]
        public int cursorBlinkRateMilliseconds = 666;

        [Header("System")]
        public bool logUnityMessages;
    }
}
