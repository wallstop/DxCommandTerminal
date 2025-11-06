namespace WallstopStudios.DxCommandTerminal.Input
{
    using System.Collections.Generic;
    using UnityEngine;

    [CreateAssetMenu(
        fileName = "TerminalInputProfile",
        menuName = "DXCommandTerminal/Terminal Input Profile",
        order = 460
    )]
    public sealed class TerminalInputProfile : ScriptableObject
    {
        [Header("System")]
        public InputMode inputMode = InputMode.LegacyInputSystem;

        [Header("Hotkeys")]
        public string toggleHotkey = "`";
        public string toggleFullHotkey = "#`";
        public string toggleLauncherHotkey = "#space";
        public string completeHotkey = "tab";
        public string reverseCompleteHotkey = "#tab";
        public string previousHotkey = "up";
        public List<string> enterCommandHotkeys = new() { "enter", "return" };
        public string closeHotkey = "escape";
        public string nextHotkey = "down";

        [Header("Control Order")]
        public List<TerminalControlTypes> controlOrder = new()
        {
            TerminalControlTypes.Close,
            TerminalControlTypes.EnterCommand,
            TerminalControlTypes.Previous,
            TerminalControlTypes.Next,
            TerminalControlTypes.ToggleLauncher,
            TerminalControlTypes.ToggleFull,
            TerminalControlTypes.ToggleSmall,
            TerminalControlTypes.CompleteBackward,
            TerminalControlTypes.CompleteForward,
        };

        public void ApplyTo(TerminalKeyboardController controller)
        {
            if (controller == null)
            {
                return;
            }

            controller.inputMode = inputMode;
            controller.toggleHotkey = toggleHotkey;
            controller.toggleFullHotkey = toggleFullHotkey;
            controller.toggleLauncherHotkey = toggleLauncherHotkey;
            controller.completeHotkey = completeHotkey;
            controller.reverseCompleteHotkey = reverseCompleteHotkey;
            controller.previousHotkey = previousHotkey;
            controller._completeCommandHotkeys = new List<string>(enterCommandHotkeys);
            controller.closeHotkey = closeHotkey;
            controller.nextHotkey = nextHotkey;
            controller._controlOrder = new List<TerminalControlTypes>(controlOrder);
        }
    }
}
