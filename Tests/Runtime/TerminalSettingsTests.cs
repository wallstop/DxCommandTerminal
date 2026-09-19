namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    /*
        Pins the shared TerminalSettings asset contract (issue #72 option B):
        an assigned asset's values win over the component's serialized values
        when the component wakes, an empty slot leaves behavior exactly as
        before, and the palette follows the asset's hotkey.
     */
    public sealed class TerminalSettingsTests
    {
        private readonly List<GameObject> _spawned = new();
        private readonly List<ScriptableObject> _createdAssets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject spawned in _spawned)
            {
                if (spawned != null)
                {
                    Object.Destroy(spawned);
                }
            }

            _spawned.Clear();

            foreach (ScriptableObject created in _createdAssets)
            {
                if (created != null)
                {
                    Object.Destroy(created);
                }
            }

            _createdAssets.Clear();
        }

        [Test]
        public void AssignedSettingsWinOverComponentValuesOnWake()
        {
            TerminalSettings settings = CreateSettings();
            settings.showGUIButtons = true;
            settings.logUnityMessages = true;

            /*
                The asset turns the on-screen buttons on, so the enable pass
                builds the visual tree; this rig has no UIDocument, which logs
                the documented setup error. Every other field below must still
                come from the asset.
             */
            LogAssert.Expect(LogType.Error, "No UIDocument assigned, cannot setup UI.");
            TerminalUI terminal = SpawnTerminal(settings);

            Assert.AreEqual(77, terminal._logBufferSize, "Log buffer follows the asset");
            Assert.AreEqual(88, terminal._historyBufferSize, "History buffer follows the asset");
            Assert.AreEqual("$", terminal._inputCaret, "Input caret follows the asset");
            Assert.AreEqual(
                123,
                terminal._cursorBlinkRateMilliseconds,
                "Caret blink rate follows the asset"
            );
            Assert.IsTrue(terminal.showGUIButtons, "showGUIButtons follows the asset");
            Assert.AreEqual("go", terminal.runButtonText, "Run button text follows the asset");
            Assert.AreEqual("bye", terminal.closeButtonText, "Close button text follows the asset");
            Assert.AreEqual(
                "shrink",
                terminal.smallButtonText,
                "Small button text follows the asset"
            );
            Assert.AreEqual("grow", terminal.fullButtonText, "Full button text follows the asset");
            Assert.AreEqual(
                HintDisplayMode.Never,
                terminal.hintDisplayMode,
                "Hint display mode follows the asset"
            );
            Assert.IsFalse(terminal.makeHintsClickable, "makeHintsClickable follows the asset");
            Assert.IsFalse(
                terminal.skipSameCommandsInHistory,
                "skipSameCommandsInHistory follows the asset"
            );
            Assert.IsTrue(
                terminal.ignoreDefaultCommands,
                "ignoreDefaultCommands follows the asset"
            );
            Assert.AreEqual(
                new List<TerminalLogType> { TerminalLogType.Warning, TerminalLogType.Error },
                terminal._ignoredLogTypes,
                "Ignored log types follow the asset"
            );
            Assert.AreEqual(
                new List<string> { "help", "clear" },
                terminal._disabledCommands,
                "Disabled commands follow the asset"
            );
            Assert.IsFalse(
                ReferenceEquals(settings.ignoredLogTypes, terminal._ignoredLogTypes),
                "Ignored log types must be copied, not aliased from the asset"
            );
            Assert.IsFalse(
                ReferenceEquals(settings.disabledCommands, terminal._disabledCommands),
                "Disabled commands must be copied, not aliased from the asset"
            );
            Assert.IsTrue(terminal._logUnityMessages, "logUnityMessages follows the asset");
            Assert.AreEqual(
                77,
                Terminal.Buffer.Capacity,
                "The asset's log buffer size reaches the shared session buffer"
            );
        }

        [Test]
        public void EmptySettingsSlotLeavesComponentValuesUntouched()
        {
            TerminalUI terminal = SpawnTerminal(null);

            Assert.AreEqual(256, terminal._logBufferSize, "Default log buffer preserved");
            Assert.AreEqual(512, terminal._historyBufferSize, "Default history buffer preserved");
            Assert.AreEqual(">", terminal._inputCaret, "Default input caret preserved");
            Assert.AreEqual(
                666,
                terminal._cursorBlinkRateMilliseconds,
                "Default blink rate preserved"
            );
            Assert.IsFalse(terminal.showGUIButtons, "Default button visibility preserved");
            Assert.AreEqual("run", terminal.runButtonText, "Default run button text preserved");
            Assert.AreEqual(
                HintDisplayMode.AutoCompleteOnly,
                terminal.hintDisplayMode,
                "Default hint mode preserved"
            );
            Assert.IsTrue(terminal.skipSameCommandsInHistory, "Default history dedup preserved");
            Assert.IsFalse(
                terminal.ignoreDefaultCommands,
                "Default built-in command registration preserved"
            );
            Assert.IsEmpty(terminal._ignoredLogTypes, "Default ignored log types preserved");
            Assert.IsEmpty(terminal._disabledCommands, "Default disabled commands preserved");
        }

        [Test]
        public void PaletteFollowsTheAssetHotkey()
        {
            GameObject gameObject = new("settings-palette");
            gameObject.SetActive(false);
            _spawned.Add(gameObject);
            CommandPaletteUI palette = gameObject.AddComponent<CommandPaletteUI>();
            palette._settings = CreateSettings();

            Assert.AreEqual(
                "ctrl+space",
                palette.toggleHotkey,
                "Sanity: the serialized component hotkey applies before enable"
            );

            gameObject.SetActive(true);

            Assert.AreEqual(
                "ctrl+p",
                palette.toggleHotkey,
                "Palette hotkey follows the asset on wake"
            );
        }

        private TerminalSettings CreateSettings()
        {
            TerminalSettings settings = ScriptableObject.CreateInstance<TerminalSettings>();
            _createdAssets.Add(settings);
            settings.logBufferSize = 77;
            settings.historyBufferSize = 88;
            settings.inputCaret = "$";
            settings.cursorBlinkRateMilliseconds = 123;
            settings.runButtonText = "go";
            settings.closeButtonText = "bye";
            settings.smallButtonText = "shrink";
            settings.fullButtonText = "grow";
            settings.hintDisplayMode = HintDisplayMode.Never;
            settings.makeHintsClickable = false;
            settings.skipSameCommandsInHistory = false;
            settings.ignoreDefaultCommands = true;
            settings.ignoredLogTypes = new List<TerminalLogType>
            {
                TerminalLogType.Warning,
                TerminalLogType.Error,
            };
            settings.disabledCommands = new List<string> { "help", "clear" };
            settings.paletteToggleHotkey = "ctrl+p";
            return settings;
        }

        private TerminalUI SpawnTerminal(TerminalSettings settings)
        {
            GameObject gameObject = new("settings-terminal");
            gameObject.SetActive(false);
            _spawned.Add(gameObject);
            TerminalUI terminal = gameObject.AddComponent<TerminalUI>();
            terminal._settings = settings;
            gameObject.SetActive(true);
            return terminal;
        }
    }
}
