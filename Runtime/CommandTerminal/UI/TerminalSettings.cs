namespace WallstopStudios.DxCommandTerminal.UI
{
    using System.Collections.Generic;
    using Attributes;
    using Backend;
    using UnityEngine;

    /*
        Shared session-config asset (issue #72, option B): one asset for the
        cross-terminal configuration that used to live only on component
        fields — buffer sizes, ignored types/commands, button text, logging,
        and the palette hotkey. Assign it on a `TerminalUI` or
        `CommandPaletteUI` and the asset's values win over the component's
        serialized values when the component wakes; leave the slot empty and
        behavior is exactly as before. Genuinely per-terminal fields
        (window geometry, animation curves, ids, persisted theme/font
        preferences) stay on the components.
     */
    [CreateAssetMenu(
        menuName = "Wallstop Studios/DxCommandTerminal/Terminal Settings",
        fileName = nameof(TerminalSettings),
        order = 1_111_125
    )]
    public sealed class TerminalSettings : ScriptableObject
    {
        [Header("Buffers")]
        [Min(1)]
        [Tooltip("Maximum number of log entries kept in the shared terminal buffer")]
        public int logBufferSize = 256;

        [Min(1)]
        [Tooltip("Maximum number of commands kept in the shared history buffer")]
        public int historyBufferSize = 512;

        [Header("Input")]
        [Tooltip("Caret character drawn before the input line")]
        public string inputCaret = ">";

        [Min(0)]
        [Tooltip("Milliseconds between caret visibility toggles while the input is focused")]
        public int cursorBlinkRateMilliseconds = 666;

        [Header("Buttons")]
        [Tooltip("Show the run/close/small/full on-screen buttons")]
        public bool showGUIButtons;

        [DxShowIf(nameof(showGUIButtons))]
        [Tooltip("Text of the button that runs the current command")]
        public string runButtonText = "run";

        [DxShowIf(nameof(showGUIButtons))]
        [Tooltip("Text of the button that closes the terminal")]
        public string closeButtonText = "close";

        [DxShowIf(nameof(showGUIButtons))]
        [Tooltip("Text of the button that shrinks the terminal")]
        public string smallButtonText = "small";

        [DxShowIf(nameof(showGUIButtons))]
        [Tooltip("Text of the button that maximizes the terminal")]
        public string fullButtonText = "full";

        [Header("Hints")]
        [Tooltip("Which completion hints the input surface shows")]
        public HintDisplayMode hintDisplayMode = HintDisplayMode.AutoCompleteOnly;

        [Tooltip("Allow clicking completion hints to apply them")]
        public bool makeHintsClickable = true;

        [Header("System")]
        [Tooltip("Reset static command state when a terminal wakes")]
        public bool resetStateOnInit;

        [Tooltip("Skip duplicate consecutive history entries")]
        public bool skipSameCommandsInHistory = true;

        [Tooltip("Register no built-in commands on this terminal")]
        public bool ignoreDefaultCommands;

        [Tooltip("Unity log categories this terminal ignores")]
        public List<TerminalLogType> ignoredLogTypes = new();

        [Tooltip("Built-in commands disabled on this terminal")]
        public List<string> disabledCommands = new();

        [Tooltip("Forward Unity's own log messages into the terminal")]
        public bool logUnityMessages;

        [Tooltip(
            "Which entries capture a caller stack trace. ErrorsAndWarnings and Disabled skip the "
                + "per-log extraction cost for routine messages."
        )]
        public TerminalStackTraceMode stackTraceMode = TerminalStackTraceMode.All;

        [Header("Command Palette")]
        [Tooltip(
            "Hotkey that opens/closes the command palette (supports ctrl+ and shift+ modifiers)"
        )]
        public string paletteToggleHotkey = "ctrl+space";
    }
}
