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
     */
    public sealed class TerminalUITransitionTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private const int FrameBudget = 600;

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private CommandPaletteUI _palette;
        private GameObject _paletteObject;
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
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            GameObject documentObject = new("TransitionSharedDocument");
            documentObject.SetActive(false);
            UIDocument document = documentObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            documentObject.SetActive(true);
            _spawnedObjects.Add(documentObject);

            _terminalObject = new GameObject("TransitionFocusTerminal");
            _terminalObject.SetActive(false);
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);

            _paletteObject = new GameObject("TransitionFocusPalette");
            _paletteObject.SetActive(false);
            _palette = _paletteObject.AddComponent<CommandPaletteUI>();
            _palette._uiDocument = document;
            _paletteObject.SetActive(true);
            yield return null;
#else
            Assert.Ignore("Terminal UI transition coverage runs in the editor Play Mode suite.");
            yield break;
#endif

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

        private IEnumerator SpawnTerminal(bool open)
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
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
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
            Depending on panel state the focus controller reports either the
            TextField or its inner text-input element; both mean the input
            owns focus (see the palette suite's equivalent helper).
         */
        private bool InputOwnsFocus()
        {
            if (_terminal._commandInput == null || _terminal._uiDocument == null)
            {
                return false;
            }

            FocusController focusController = _terminal
                ._uiDocument
                .rootVisualElement
                .focusController;
            VisualElement focused = focusController?.focusedElement as VisualElement;
            return focused == _terminal._commandInput || _terminal._commandInput.Contains(focused);
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
#endif
    }
}
