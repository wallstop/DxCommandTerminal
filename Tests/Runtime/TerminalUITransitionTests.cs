namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DxCommandTerminal.Input;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.LowLevel;
#endif
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Pins the transition contracts between terminal states and surfaces:
        the toggle state machine, the closed-state input gates, no duplicate
        execution, hotkey conflict priority, dead-terminal input safety, and
        palette behavior on a destroyed focus target. Real key events are
        never synthesized; the keyboard controller is driven through a
        scripted subclass.

        Two subclasses appear below and they exercise different rule paths.
        ScriptedInputController overrides every per-control check, so it also
        bypasses the text-focus gate those checks apply (that is the point:
        it drives control priority). HotkeyController overrides the key read
        instead, so the gate runs unchanged.
     */
    public sealed class TerminalUITransitionTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private const int FrameBudget = 600;

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private CommandPaletteUI _palette;
        private GameObject _paletteObject;
        private HotkeyController _hotkeyController;
        private PanelSettings _panelSettings;
        private readonly List<GameObject> _spawnedObjects = new();

        [TearDown]
        public void TearDown()
        {
            /*
                The input abstraction is a process-wide singleton outside the
                per-test session reset; clear it so a mid-test failure cannot
                leak staged text into the next test's first command.
             */
            DefaultTerminalInput.Instance.CommandText = string.Empty;

            for (int index = _spawnedObjects.Count - 1; 0 <= index; --index)
            {
                if (_spawnedObjects[index] != null)
                {
                    UnityEngine.Object.Destroy(_spawnedObjects[index]);
                }
            }

            _spawnedObjects.Clear();

            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_paletteObject != null)
            {
                UnityEngine.Object.Destroy(_paletteObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator ToggleCyclesClosedSmallFullClosed()
        {
            yield return SpawnTerminal(open: false);

            Assert.IsTrue(_terminal.IsClosed, "Sanity: the rig starts closed");

            _terminal.ToggleSmall();
            yield return WaitForInputVisible("ToggleSmall must open the terminal");

            _terminal.ToggleSmall();
            yield return WaitForClosed("A second ToggleSmall must close the open terminal");

            _terminal.ToggleFull();
            yield return WaitForInputVisible("ToggleFull must open the terminal");

            _terminal.ToggleFull();
            yield return WaitForClosed("A second ToggleFull must close the open terminal");
        }

        [UnityTest]
        public IEnumerator ClosedTerminalIgnoresInputActions()
        {
            yield return SpawnTerminal(open: false);

            int runs = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "closedgate",
                    _ => ++runs,
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the probe command registers"
            );

            /*
                The gates must neither run nor touch staged input: stage a
                sentinel first, then drive every input action while closed.
             */
            DefaultTerminalInput.Instance.CommandText = "staged";
            _terminal.EnterCommand();
            _terminal.CompleteCommand();
            _terminal.HandlePrevious();
            _terminal.HandleNext();

            Assert.AreEqual(0, runs, "Input actions on a closed terminal must not run commands");
            Assert.IsTrue(
                _terminal.IsClosed,
                "Input actions on a closed terminal must not open it"
            );
            Assert.AreEqual(
                "staged",
                DefaultTerminalInput.Instance.CommandText,
                "Input actions on a closed terminal must not consume staged input text"
            );
        }

        [UnityTest]
        public IEnumerator RepeatedEnterRunsCommandOnce()
        {
            yield return SpawnTerminal(open: true);

            int runs = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "dupgate",
                    _ => ++runs,
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the probe command registers"
            );

            /*
                Execution clears the input, so a second Enter on the same
                frame - a held or repeated key with no retyping - finds an
                empty line and must not re-run the command.
             */
            DefaultTerminalInput.Instance.CommandText = "dupgate";
            _terminal.EnterCommand();
            _terminal.EnterCommand();

            Assert.AreEqual(
                1,
                runs,
                "A held or repeated Enter must not re-run the command after execution"
            );
        }

        [UnityTest]
        public IEnumerator ConflictingHotkeysResolveByControlOrder()
        {
            yield return SpawnTerminal(open: true);

            int runs = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "conflictrun",
                    _ => ++runs,
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the probe command registers"
            );

            ScriptedInputController controller = SpawnController();

            /*
                Close sits before EnterCommand in the default control order, so
                a frame where both hotkeys read pressed must run exactly one
                action: the close.
             */
            controller.PressClose = true;
            controller.PressEnter = true;
            controller.DriveUpdate();
            yield return WaitForClosed(
                "The higher-priority control must win a same-frame hotkey conflict"
            );
            Assert.AreEqual(
                0,
                runs,
                "The lower-priority control must not also run on the conflict frame"
            );

            controller.PressClose = false;
            controller.PressEnter = true;
            _terminal.SetState(TerminalState.OpenFull);
            DefaultTerminalInput.Instance.CommandText = "conflictrun";
            controller.DriveUpdate();
            Assert.AreEqual(
                1,
                runs,
                "EnterCommand must run on a frame where it is the only pressed control"
            );
            Assert.IsFalse(_terminal.IsClosed, "EnterCommand must not close the terminal");
        }

        /*
            The console key is reserved by the surface it opens (#218): the
            stroke that opens must be the stroke that closes, so the poll
            answers it while the command field owns focus. From full, the
            small toggle first shrinks and the second press closes - both
            with the field still focused.
         */
        [UnityTest]
        public IEnumerator ConsoleKeyTogglesTheTerminalClosedWhileTheInputOwnsFocus()
        {
            yield return SpawnTerminal(open: true, withHotkeyController: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            /*
                The shipped default is a character key, which is what makes
                this the reservation contract rather than a free hotkey.
             */
            Assert.IsTrue(
                InputHelpers.ProducesTypedText(_hotkeyController.toggleHotkey),
                $"Sanity: the default toggle binding \"{_hotkeyController.toggleHotkey}\" types text"
            );

            _hotkeyController.PressedHotkey = _hotkeyController.toggleHotkey;
            _hotkeyController.DriveUpdate();
            Assert.AreEqual(
                TerminalState.OpenSmall,
                _terminal.State,
                "The console key must act while the field has focus, not type its character"
            );
            yield return WaitForFocusedInput("Sanity: the shrunken surface's field holds focus");

            _hotkeyController.PressedHotkey = _hotkeyController.toggleHotkey;
            _hotkeyController.DriveUpdate();
            yield return WaitForClosed(
                "The same stroke must close the surface it opened, whatever holds focus"
            );
        }

        /*
            The full toggle answers the same contract in one stroke: while the
            command field owns focus, shift+console key closes the full
            surface directly.
         */
        [UnityTest]
        public IEnumerator FullConsoleKeyClosesTheSurfaceWhileTheInputOwnsFocus()
        {
            yield return SpawnTerminal(open: true, withHotkeyController: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            _hotkeyController.PressedHotkey = _hotkeyController.toggleFullHotkey;
            _hotkeyController.DriveUpdate();
            yield return WaitForClosed(
                "The full-console key must close the surface while the field has focus"
            );
        }

        [UnityTest]
        public IEnumerator TextHotkeyOpensTheTerminalWithNoConsoleFieldFocused()
        {
            yield return SpawnTerminal(open: false, withHotkeyController: true);

            _hotkeyController.FocusOverride = false;
            _hotkeyController.PressedHotkey = _hotkeyController.toggleHotkey;
            _hotkeyController.DriveUpdate();

            yield return WaitForInputVisible(
                "With no console field focused the console key must still open the terminal"
            );
        }

        [UnityTest]
        public IEnumerator NonTextHotkeyFiresWhileTheInputOwnsFocus()
        {
            yield return SpawnTerminal(open: true, withHotkeyController: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            _hotkeyController.PressedHotkey = _hotkeyController.closeHotkey;
            _hotkeyController.DriveUpdate();

            yield return WaitForClosed(
                "A binding that types no character must keep working while the field has focus"
            );
        }

        /*
            The reservation costs the character: the press reaches the focused
            field and the poll in the same frame, and the close that answers
            it clears the line. Nothing typed survives the console key - that
            is the reserved-key contract (#218), not a dropped edit.
         */
        [UnityTest]
        public IEnumerator ConsoleKeyPressClosesAndClearsTheLineItHeld()
        {
            yield return SpawnTerminal(open: true, withHotkeyController: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            /*
                The field write stands in for the character the engine hands
                the field from the same key press the poll sees. The full
                toggle is the one-stroke close from full; the small toggle
                would shrink first.
             */
            _terminal._commandInput.value = "echo ready";
            _hotkeyController.PressedHotkey = _hotkeyController.toggleFullHotkey;
            _hotkeyController.DriveUpdate();

            yield return WaitForClosed(
                "The console key must close the terminal while its line holds text"
            );
            Assert.AreEqual(
                string.Empty,
                DefaultTerminalInput.Instance.CommandText,
                "The close answers the console key, and the close clears the line"
            );
        }

        [UnityTest]
        public IEnumerator TextHotkeyReopensATerminalClosedWhileItsFieldHeldFocus()
        {
            yield return SpawnTerminal(open: true, withHotkeyController: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            _terminal.Close();
            yield return WaitForClosed("Sanity: the terminal closes");

            /*
                Closing hides the field rather than detaching it, so a panel
                that keeps reporting a hidden field would let a stale focus
                report hold the console key. Whatever this editor does, the key
                has to open the terminal again.
             */
            _hotkeyController.PressedHotkey = _hotkeyController.toggleHotkey;
            _hotkeyController.DriveUpdate();

            yield return WaitForInputVisible(
                "The console key must open a terminal that was closed while its field held focus"
            );
        }

        /*
            The focus controller reports either the field or an element inside
            it, and the property accepts both. A suite that only reads the
            property cannot tell a right answer from a wrong one, so the focus
            read happens here directly, and the closed case is pinned with it.
         */
        [UnityTest]
        public IEnumerator InputOwnsFocusTracksTheFieldAndIgnoresAClosedTerminal()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the field reports focus");

            VisualElement focused =
                _terminal._commandInput.focusController?.focusedElement as VisualElement;
            Assert.IsTrue(
                focused == _terminal._commandInput || _terminal._commandInput.Contains(focused),
                "Sanity: the panel reports focus on the field or inside it"
            );
            Assert.IsTrue(
                _terminal.InputOwnsFocus,
                "A focused command field must report owning focus"
            );

            _terminal.Close();
            yield return WaitForClosed("Sanity: the terminal closes");
            Assert.IsFalse(
                _terminal.InputOwnsFocus,
                "A closed terminal must not report owning focus, whatever still holds it"
            );
        }

        /*
            A control can carry several bindings. The gate applies per binding,
            so a character added to the enter list types while Enter still runs
            the command. The character is not a toggle binding, so the
            reservation contract (#218) leaves it to the field and this pins
            the typed-text rule the enter list still answers.
         */
        [UnityTest]
        public IEnumerator TextBindingInTheEnterListTypesWhileEnterStillRuns()
        {
            yield return SpawnTerminal(open: true, withHotkeyController: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            int runs = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "mixedgate",
                    _ => ++runs,
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the probe command registers"
            );

            _hotkeyController.BindEnterTo("enter", "=");
            DefaultTerminalInput.Instance.CommandText = "mixedgate";

            _hotkeyController.PressedHotkey = "=";
            _hotkeyController.DriveUpdate();
            Assert.AreEqual(
                0,
                runs,
                "A character binding in the enter list must type while the field has focus"
            );
            Assert.AreEqual(
                TerminalState.OpenFull,
                _terminal.State,
                "Sanity: the character is no toggle binding, so the surface stays open"
            );

            _hotkeyController.PressedHotkey = "enter";
            _hotkeyController.DriveUpdate();
            Assert.AreEqual(1, runs, "A named binding in the same list must still run the command");
        }

        /*
            The palette runs its own poll on its own component, so this drives
            the real thing: a queued Input System key event, no seam. The second
            half is the control that proves the queue reached the poll -
            without it, "the palette stayed open" would be a green that means
            nothing. The key is released between the two, because a state event
            re-asserting a held bit is not a new press.
         */
#if ENABLE_INPUT_SYSTEM
        [UnityTest]
        public IEnumerator PaletteTextToggleClosesTheBarWhileItsQueryHasFocus()
        {
            yield return SpawnTerminal(open: false, withPalette: true);

            _palette.Open();
            yield return null;
            Assert.IsTrue(
                CommandPaletteUI.AnyInputOwnsFocus(),
                "Sanity: the palette query field holds panel focus"
            );

            _palette.toggleHotkey = "`";

            /*
                Release whatever a predecessor left held: a state event
                re-asserting a held bit is not a new press, and both presses
                below have to be new ones.
             */
            ReleaseKeys();
            yield return null;

            PressBackquote();
            yield return WaitForPaletteState(
                false,
                "The palette's toggle key must close it even while its query has focus"
            );

            /*
                The control half: with nothing focused, the same key must
                reopen the bar, which is what makes the first half mean
                something.
             */
            ReleaseKeys();
            yield return null;
            PressBackquote();
            yield return WaitForPaletteState(
                true,
                "Sanity: the queued key reaches the palette poll once nothing is focused"
            );

            /*
                The press is left held; release it so the next test's queued
                press is a new press, not a re-assertion of a held bit.
             */
            ReleaseKeys();
            yield return null;
        }

        /*
            The mirror: the palette's toggle key is the palette's, so it opens
            the bar even over a focused terminal command line, and the open
            closes the terminal surface.
         */
        [UnityTest]
        public IEnumerator PaletteTextToggleOpensTheBarOverAFocusedTerminal()
        {
            yield return SpawnTerminal(open: true, withPalette: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            /*
                Release whatever a predecessor test left held: a state event
                re-asserting a held bit is not a new press, and this press
                has to be one.
             */
            ReleaseKeys();
            yield return null;

            _palette.toggleHotkey = "`";
            PressBackquote();
            yield return WaitForPaletteState(
                true,
                "The palette's toggle key must open the bar even over a focused command line"
            );
            Assert.IsTrue(
                _terminal.IsClosed,
                "Opening the bar over the terminal must close the terminal surface"
            );
        }
#endif

        /*
            The console key is the terminal's: with the palette open and its
            query focused, the console key still opens the terminal, and the
            terminal's open closes the palette (#218).
         */
        [UnityTest]
        public IEnumerator ConsoleKeyOpensTheTerminalOverAFocusedPalette()
        {
            yield return SpawnTerminal(open: false, withPalette: true);

            _palette.Open();
            yield return null;

            Assert.IsTrue(_palette.IsOpen, "Sanity: the palette opens over the closed terminal");
            Assert.IsTrue(
                CommandPaletteUI.AnyInputOwnsFocus(),
                "Sanity: the palette query field holds panel focus"
            );

            HotkeyController controller = SpawnHotkeyController();
            controller.PressedHotkey = controller.toggleHotkey;
            controller.DriveUpdate();

            Assert.IsFalse(
                _palette.IsOpen,
                "The terminal taking the surface must close the palette"
            );
            yield return WaitForInputVisible(
                "The console key must open the terminal over the palette"
            );
        }

        [UnityTest]
        public IEnumerator ControllerSkipsDestroyedTerminal()
        {
            yield return SpawnTerminal(open: true);

            ScriptedInputController controller = SpawnController();

            UnityEngine.Object.DestroyImmediate(_terminalObject);
            _terminalObject = null;
            _terminal = null;

            Assert.That(
                controller.terminal == null,
                "Sanity: destroying the terminal leaves the controller's reference fake-null"
            );

            Assert.DoesNotThrow(
                controller.DriveUpdate,
                "A controller update with no pressed keys must skip a destroyed terminal"
            );
            controller.PressClose = true;
            Assert.DoesNotThrow(
                controller.DriveUpdate,
                "A controller update with a pressed key must skip a destroyed terminal"
            );
        }

        [UnityTest]
        public IEnumerator ClosedPaletteRejectsSelectionAndSubmission()
        {
            yield return SpawnPalette();

            Assert.IsFalse(_palette.IsOpen, "Sanity: the palette starts closed");

            Assert.IsFalse(_palette.Submit(), "Submit on a closed palette must be a no-op");
            Assert.IsFalse(
                _palette.ApplySelected(),
                "ApplySelected on a closed palette must be a no-op"
            );
            Assert.IsFalse(
                _palette.MoveSelection(1),
                "MoveSelection on a closed palette must be a no-op"
            );
            Assert.IsFalse(
                _palette.MoveSelection(-1),
                "Reverse MoveSelection on a closed palette must be a no-op"
            );
            Assert.IsFalse(
                _palette.TryGetSelected(out _),
                "With nothing staged, TryGetSelected must report no selection"
            );
        }

        /*
            The palette captures the previously focused element on open and
            refocuses it on close. When that element's terminal is destroyed
            while the palette is open, the captured element is detached from
            the panel; the restore must skip it instead of focusing a dead
            element, and the palette tick must survive the detached state.
         */
        [UnityTest]
        public IEnumerator PaletteCloseSurvivesDestroyedFocusTarget()
        {
            yield return SpawnTerminal(open: false, withPalette: true);

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Sanity: the terminal opens on the shared document");
            yield return WaitForFocusedInput("Sanity: the terminal input holds panel focus");

            _palette.Open();
            yield return null;

            Assert.IsTrue(_palette.IsOpen, "Sanity: the palette opens");
            yield return WaitForClosed("Opening the palette closes the terminal surface");
            Assert.That(
                _palette._input != null
                    && _palette._input.focusController != null
                    && _palette._input.focusController.focusedElement == _palette._input,
                "Sanity: the palette input takes over panel focus"
            );

            UnityEngine.Object.DestroyImmediate(_terminalObject);
            _terminalObject = null;
            _terminal = null;
            yield return null;

            Assert.IsTrue(
                _palette.IsOpen,
                "The palette must survive its focus target's destruction"
            );
            Assert.DoesNotThrow(
                _palette.Close,
                "Closing a palette whose captured focus target was destroyed must not throw"
            );
            Assert.IsFalse(_palette.IsOpen, "The palette must close after the destroyed target");
        }

        private ScriptedInputController SpawnController()
        {
            GameObject controllerObject = new("TransitionController");
            controllerObject.SetActive(false);
            ScriptedInputController controller =
                controllerObject.AddComponent<ScriptedInputController>();
            controller.terminal = _terminal;
            controllerObject.SetActive(true);
            _spawnedObjects.Add(controllerObject);
            return controller;
        }

        private HotkeyController SpawnHotkeyController()
        {
            GameObject controllerObject = new("TransitionHotkeyController");
            controllerObject.SetActive(false);
            HotkeyController controller = controllerObject.AddComponent<HotkeyController>();
            controller.terminal = _terminal;
            controllerObject.SetActive(true);
            _spawnedObjects.Add(controllerObject);
            return controller;
        }

#if ENABLE_INPUT_SYSTEM
        /*
            The real Input System path, so the queue has to reach a component
            poll: no seam, and a state event on the current keyboard.
         */
        private static void PressBackquote()
        {
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Backquote));
        }

        private static void ReleaseKeys()
        {
            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
        }
#endif

        private IEnumerator WaitForPaletteState(bool open, string message)
        {
            /*
                The key is a one-shot state event, so waiting for the state to
                match would pass on the pre-existing value. Let frames pass
                instead; the control half of the test proves this many is
                enough for the press to land.
             */
            const int SettleFrames = 5;
            for (int frame = 0; frame < SettleFrames; ++frame)
            {
                yield return null;
            }

            Assert.AreEqual(open, _palette.IsOpen, message);
        }

        private IEnumerator WaitForInputVisible(string message)
        {
            int frameBudget = FrameBudget;
            while (
                0 < frameBudget--
                && (
                    _terminal._commandInput == null
                    || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex
                )
            )
            {
                yield return null;
            }

            Assert.AreEqual(
                DisplayStyle.Flex,
                _terminal._commandInput.resolvedStyle.display,
                message
            );
        }

        private IEnumerator WaitForClosed(string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !_terminal.IsClosed)
            {
                yield return null;
            }

            Assert.IsTrue(_terminal.IsClosed, message);
        }

        private IEnumerator SpawnTerminal(
            bool open,
            bool withHotkeyController = false,
            bool withPalette = false
        )
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUITransitions");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            if (withHotkeyController)
            {
                /*
                    Added before activation so the terminal's Awake sees it in
                    GetComponents<IInputHandler>, the shipped arrangement that
                    lets the field-change handler observe a handled frame.
                 */
                _hotkeyController = _terminalObject.AddComponent<HotkeyController>();
            }

            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
            if (withPalette)
            {
                /*
                    A palette on the terminal's own document is the shipped
                    arrangement, and the only one where the two surfaces can
                    contest the same key press.
                 */
                _paletteObject = new GameObject("TransitionPalette");
                _paletteObject.SetActive(false);
                _palette = _paletteObject.AddComponent<CommandPaletteUI>();
                _palette._uiDocument = document;
                _paletteObject.SetActive(true);
                yield return null;
            }
#else
            Assert.Ignore("Terminal UI transition coverage runs in the editor Play Mode suite.");
            yield break;
#endif

            if (open)
            {
                _terminal.SetState(TerminalState.OpenFull);
                yield return null;
                yield return WaitForInputVisible("Sanity: the rig's terminal opens");
            }
        }

        private IEnumerator SpawnPalette()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _paletteObject = new GameObject("TransitionPalette");
            _paletteObject.SetActive(false);
            UIDocument document = _paletteObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _palette = _paletteObject.AddComponent<CommandPaletteUI>();
            _palette._uiDocument = document;
            _paletteObject.SetActive(true);
            yield return null;
#else
            Assert.Ignore("Terminal UI transition coverage runs in the editor Play Mode suite.");
            yield break;
#endif
        }

        private IEnumerator WaitForFocusedInput(string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !InputOwnsFocus())
            {
                yield return null;
            }

            Assert.IsTrue(InputOwnsFocus(), message);
        }

        /*
            The focus controller may report either the TextField or its inner
            text-input element; the runtime property accepts both.
         */
        private bool InputOwnsFocus()
        {
            return _terminal != null && _terminal.InputOwnsFocus;
        }

#if UNITY_EDITOR
        private static T LoadAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.That(asset != null, $"Expected the test asset at {PackageRoot}/{relativePath}");
            return asset;
        }

        /*
            Overrides every input check so no control reads the real Input
            API; the pressed flags decide what the frame sees. The base
            constructor binds these virtual methods into its check table, so
            the overrides steer the stock priority loop unchanged.
         */
        private sealed class ScriptedInputController : TerminalKeyboardController
        {
            public bool PressClose;
            public bool PressEnter;

            public void DriveUpdate()
            {
                Update();
            }

            protected override bool IsClosePressed()
            {
                return PressClose;
            }

            protected override bool IsEnterCommandPressed()
            {
                return PressEnter;
            }

            protected override bool IsPreviousPressed()
            {
                return false;
            }

            protected override bool IsNextPressed()
            {
                return false;
            }

            protected override bool IsToggleFullPressed()
            {
                return false;
            }

            protected override bool IsToggleSmallPressed()
            {
                return false;
            }

            protected override bool IsCompleteBackwardPressed()
            {
                return false;
            }

            protected override bool IsCompletePressed()
            {
                return false;
            }
        }

        /*
            Simulates a key press at the hotkey-string level instead of
            overriding the per-control checks, so the text-focus gate the
            real checks apply runs unchanged. The first poll that reads the
            press consumes it, because the real poll is edge-triggered. A null
            FocusOverride reads live panel focus, which is the shipped
            behavior.
         */
        private sealed class HotkeyController : TerminalKeyboardController
        {
            protected override bool TextInputOwnsFocus => FocusOverride ?? base.TextInputOwnsFocus;

            public string PressedHotkey;

            [System.NonSerialized]
            public bool? FocusOverride;

            public void DriveUpdate()
            {
                Update();
            }

            public void BindEnterTo(params string[] hotkeys)
            {
                _completeCommandHotkeys.Clear();
                _completeCommandHotkeys.AddRange(hotkeys);
            }

            protected override bool IsHotkeyDown(string hotkey)
            {
                if (hotkey != PressedHotkey)
                {
                    return false;
                }

                PressedHotkey = null;
                return true;
            }
        }
#endif
    }
}
