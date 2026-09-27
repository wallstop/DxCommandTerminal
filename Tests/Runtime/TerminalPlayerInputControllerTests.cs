namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using Backend;
    using Components;
    using Input;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;
    using UnityEngine.InputSystem.LowLevel;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Pins the PlayerInput path's share of the typing-wins rule: a message
        driven by a keyboard character key belongs to the console field
        holding focus, while a key that types no character, a control that is
        not a keyboard key, and the same character key with no focused field
        all run as they always have.

        The rig drives the real path: a PlayerInput with the default
        SendMessages behavior sends the real On* messages from real queued
        key events, and every action records the control that performed it.
        The sweep binds all eight messages to the character key at once, so a
        message that is held back is one that arrived and was deferred, not
        one that never fired. The staged text is a prefix of a registered
        command, so Enter would run it, completion would extend it, and
        history navigation would replace it: each deferred message would show
        up in the terminal state, the run count, or the staged text.
     */
    public sealed class TerminalPlayerInputControllerTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private const int FrameBudget = 600;

        private const int SettleFrames = 5;

        private const string CharacterKeyPath = "<Keyboard>/backquote";

        private const string NonTypingKeyPath = "<Keyboard>/escape";

        private const string ProbeCommand = "playerinputgate";

        /*
            A prefix of the probe command, so the three text-reading messages
            each have work to do: Enter would run it, completion would extend
            it, and history navigation would replace it.
         */
        private const string StagedText = "playerinputga";

        /*
            Button, except in the one test that needs a Value action's release
            message. The action type is fixed when the rig creates its actions,
            so a test that needs another one sets this before spawning.
         */
        private static InputActionType ActionType = InputActionType.Button;

        private readonly List<InputAction> _actions = new();

        private readonly List<string> _performedControls = new();

        private InputActionAsset _asset;

        private PlayerInput _playerInput;

        private TerminalUI _terminal;

        private GameObject _terminalObject;

        private PanelSettings _panelSettings;

        /*
            The names PlayerInput turns into the controller's messages. Read
            from the controller so a new handler without a matching action -
            or a handler that passes the wrong action name - fails here.
         */
        private static List<string> RoutedMessages()
        {
            List<string> messages = new();
            foreach (
                MethodInfo method in typeof(TerminalPlayerInputController).GetMethods(
                    BindingFlags.Public | BindingFlags.Instance
                )
            )
            {
                if (!method.Name.StartsWith("On", StringComparison.Ordinal))
                {
                    continue;
                }

                messages.Add(method.Name[2..]);
            }

            messages.Sort(StringComparer.Ordinal);
            return messages;
        }

        /*
            Resolves a control by name against the live keyboard, the only
            device these rows need. The non-keyboard case has its own Play Mode
            test, which adds a real gamepad and presses a real button.
         */
        private static InputControl ResolveControl(string controlName)
        {
            return Keyboard.current?[controlName];
        }

        private static void PressCharacterKey()
        {
            PressKey(Key.Backquote);
        }

        private static void ReleaseKeys()
        {
            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
        }

        /*
            A queued state replaces the whole keyboard, but a state equal to
            the current one is not a new press, so the keys are released
            first: a key left held by an earlier test would make this press
            invisible.
         */
        private static void PressKey(Key key)
        {
            ReleaseKeys();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
        }

        private static IEnumerator Settle()
        {
            for (int frame = 0; frame < SettleFrames; ++frame)
            {
                yield return null;
            }
        }

        [TearDown]
        public void TearDown()
        {
            /*
                The input abstraction is a process-wide singleton outside the
                per-test session reset; clear it so staged text cannot leak
                into the next test's first command.
             */
            DefaultTerminalInput.Instance.CommandText = string.Empty;

            // A key left held here is invisible to the next press, here or in another suite.
            ReleaseKeys();

            if (_playerInput != null)
            {
                _playerInput.DeactivateInput();
            }

            if (_asset != null)
            {
                _asset.Disable();
                UnityEngine.Object.Destroy(_asset);
                _asset = null;
            }

            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
                _terminalObject = null;
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
                _panelSettings = null;
            }

            _actions.Clear();
            _performedControls.Clear();
            ActionType = InputActionType.Button;
        }

        /*
            The rule, as data: a keyboard character key and anyKey are held
            back, a keyboard key that types nothing and a control that is not
            a keyboard key are not.
         */
        [TestCase("backquote", true)]
        [TestCase("a", true)]
        [TestCase("space", true)]
        [TestCase("slash", true)]
        [TestCase("numpad0", true)]
        [TestCase("numpadPlus", true)]
        [TestCase("anyKey", true)]
        [TestCase("escape", false)]
        [TestCase("enter", false)]
        [TestCase("backspace", false)]
        [TestCase("tab", false)]
        [TestCase("upArrow", false)]
        [TestCase("f1", false)]
        [TestCase("leftShift", false)]
        [TestCase("numpadEnter", false)]
        public void ControlProducesTypedTextFollowsTheTypingRule(string controlName, bool expected)
        {
            InputControl control = ResolveControl(controlName);
            Assert.That(
                control,
                Is.Not.Null,
                $"Sanity: the live keyboard has a {controlName} control"
            );

            Assert.That(
                InputHelpers.ControlProducesTypedText(control),
                Is.EqualTo(expected),
                $"ControlProducesTypedText({controlName}) must be {expected}"
            );
        }

        /*
            A Ctrl chord inserts no character, so it stays a live hotkey: the
            control names the key, and the modifier lives in the live keyboard
            state, which is what this row pins. A player that binds Ctrl+[ or
            Ctrl+` to a message keeps that binding working.
         */
        [UnityTest]
        public IEnumerator CtrlChordMessageStillActsWhileTyping()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("ToggleSmall", CharacterKeyPath);

            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
            InputSystem.QueueStateEvent(
                Keyboard.current,
                new KeyboardState(Key.LeftCtrl, Key.Backquote)
            );
            yield return Settle();

            Assert.That(
                _performedControls,
                Is.EqualTo(new[] { "backquote" }),
                "Sanity: the chord's key performed its action"
            );
            Assert.AreEqual(
                TerminalState.OpenSmall,
                _terminal.State,
                "A Ctrl chord inserts no character, so it must keep running its message while the field has focus"
            );
        }

        /*
            A control the rule cannot name types nothing, so the message runs:
            a game calling a handler directly, or an action the asset does not
            contain, must keep working exactly as it did before the rule.
         */
        [Test]
        public void ControlProducesTypedTextTreatsAnUnnamedControlAsTypingNothing()
        {
            Assert.That(
                InputHelpers.ControlProducesTypedText(null),
                Is.False,
                "A null control names no key, so a message it drove must run rather than defer"
            );
        }

        /*
            The shipped default toggle binding is a character, so pressing
            the console key from a PlayerInput action is a text edit: while
            the command field owns focus the key belongs to the field and no
            routed message runs.
         */
        [UnityTest]
        public IEnumerator CharacterKeyMessagesAreLeftToTheFocusedField()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            int runs = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    ProbeCommand,
                    _ => ++runs,
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the probe command registers, so completion and Enter have work to do"
            );

            List<string> routedMessages = RoutedMessages();
            foreach (string message in routedMessages)
            {
                Bind(message, CharacterKeyPath);
            }

            DefaultTerminalInput.Instance.CommandText = StagedText;
            PressCharacterKey();
            yield return Settle();

            Assert.That(
                _performedControls.Count,
                Is.EqualTo(routedMessages.Count),
                "Sanity: every routed message reached the controller before the rule was applied"
            );
            Assert.That(
                _performedControls,
                Has.All.EqualTo("backquote"),
                "Sanity: every action performed on the keyboard key that types its character"
            );
            Assert.AreEqual(
                TerminalState.OpenFull,
                _terminal.State,
                "A character key must not change the terminal state while the field has focus"
            );
            Assert.AreEqual(
                0,
                runs,
                "A character key must not run the staged command through EnterCommand"
            );
            Assert.AreEqual(
                StagedText,
                DefaultTerminalInput.Instance.CommandText,
                "A character key must not complete or navigate the staged text either"
            );
        }

        /*
            The control: the queue reaches the poll, the action fires, and a
            key that types no character still runs its message while the
            field has focus.
         */
        [UnityTest]
        public IEnumerator NonTypingKeyMessageStillActsWhileTyping()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("ToggleSmall", NonTypingKeyPath);

            PressKey(Key.Escape);
            yield return Settle();

            Assert.That(
                _performedControls,
                Is.EqualTo(new[] { "escape" }),
                "Sanity: the escape action performed on the key that types no character"
            );
            Assert.AreEqual(
                TerminalState.OpenSmall,
                _terminal.State,
                "A key that types no character must keep running its message while the field has focus"
            );
        }

        /*
            A composite binding is one action over several controls, and the
            control that performed it decides: a composite whose driving part
            is a keyboard character key defers, and the same composite driven
            by a key that types nothing runs. The rig is the only way to
            prove which control the console read, because a composite's
            active control is whichever part fired.
         */
        [UnityTest]
        public IEnumerator CompositeMessageDefersOnItsCharacterKeyAndRunsOnItsOtherKey()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            InputAction toggle = _asset.FindAction("ToggleSmall");
            toggle
                .AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/backquote")
                .With("Positive", "<Keyboard>/escape");
            toggle.performed += OnActionPerformed;
            toggle.Enable();
            _actions.Add(toggle);

            PressKey(Key.Escape);
            yield return Settle();
            Assert.That(
                _performedControls,
                Is.EqualTo(new[] { "escape" }),
                "Sanity: the composite performed on the key that types no character"
            );
            Assert.AreEqual(
                TerminalState.OpenSmall,
                _terminal.State,
                "A composite driven by the key that types no character must run its message"
            );

            _terminal.ToggleSmall();
            yield return null;
            _performedControls.Clear();
            PressCharacterKey();
            yield return Settle();
            Assert.That(
                _performedControls,
                Is.EqualTo(new[] { "backquote" }),
                "Sanity: the same composite performed on the character key"
            );
            Assert.AreEqual(
                TerminalState.OpenSmall,
                _terminal.State,
                "The same composite driven by a character key must defer its message"
            );
        }

        /*
            A message sent by hand runs as it always has: no action performed
            it, so there is no control to classify, and a game that calls the
            handler from its own input code must not lose it. A character key
            is bound in this test, so the state change can only come from the
            character path - there is no key press to defer.
         */
        [UnityTest]
        public IEnumerator HandSentMessageStillRunsWhileTyping()
        {
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("ToggleSmall", CharacterKeyPath);

            TerminalPlayerInputController controller =
                _terminalObject.GetComponent<TerminalPlayerInputController>();
            Assert.That(controller != null, "Sanity: the rig has the controller under test");
            Assert.IsTrue(
                _asset.FindAction("ToggleSmall").activeControl == null,
                "Sanity: no key has been pressed, so the action has no active control to classify"
            );

            controller.OnToggleSmall(null);
            yield return null;

            Assert.AreEqual(
                TerminalState.OpenSmall,
                _terminal.State,
                "A message with no action behind it must run as it always has"
            );
        }

        /*
            The console key still opens a closed console: the rule defers a
            character key only while a console field owns focus.
         */
        [UnityTest]
        public IEnumerator CharacterKeyMessageActsWithNoFieldFocused()
        {
            yield return SpawnTerminal(open: false);

            Assert.IsTrue(
                _terminal.IsClosed,
                "Sanity: nothing has focus while the terminal is closed"
            );

            Bind("ToggleSmall", CharacterKeyPath);

            PressCharacterKey();
            yield return Settle();

            Assert.AreEqual(
                TerminalState.OpenSmall,
                _terminal.State,
                "With no console field focused the console key must still open the terminal"
            );
        }

        /*
            A Value action sends a message on the press and again on the
            release, and the release still names the key, so the rule covers
            both halves of a character binding. On a key that types no
            character both run, and a toggle opens the console and closes it
            again - which is why the README says to bind a message to a Button
            action. Both halves are measured here rather than asserted in prose.
         */
        [UnityTest]
        public IEnumerator ValueActionDefersBothHalvesOfACharacterBindingAndRunsBothOfANonTypingOne()
        {
            ActionType = InputActionType.Value;
            yield return SpawnTerminal(open: true);
            yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

            Bind("ToggleSmall", NonTypingKeyPath);
            PressKey(Key.Escape);
            yield return Settle();
            Assert.AreEqual(
                TerminalState.OpenSmall,
                _terminal.State,
                "Sanity: the press runs, because Escape types no character"
            );

            ReleaseKeys();
            yield return Settle();
            Assert.IsTrue(
                _terminal.IsClosed,
                "A Value action's release runs its message too, so a toggle on such a binding undoes itself"
            );

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForFocusedInput("Sanity: the field takes focus again");

            Bind("ToggleFull", CharacterKeyPath);
            PressCharacterKey();
            yield return Settle();
            ReleaseKeys();
            yield return Settle();
            Assert.AreEqual(
                TerminalState.OpenFull,
                _terminal.State,
                "Both halves of a character binding must be deferred, so a Value action cannot close the console"
            );
        }

        /*
            A gamepad is not a keyboard, so a gamepad binding types nothing and
            must keep working while the user types a command. The device is
            added and removed here because a test host has no gamepad.
         */
        [UnityTest]
        public IEnumerator GamepadMessageStillActsWhileTyping()
        {
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            try
            {
                yield return SpawnTerminal(open: true);
                yield return WaitForFocusedInput("Sanity: the command field holds panel focus");

                Bind("ToggleSmall", "<Gamepad>/buttonSouth");

                InputSystem.QueueStateEvent(gamepad, new GamepadState(GamepadButton.South));
                yield return Settle();

                Assert.That(
                    _performedControls,
                    Is.EqualTo(new[] { "buttonSouth" }),
                    "Sanity: the gamepad action performed on the button that types no character"
                );
                Assert.AreEqual(
                    TerminalState.OpenSmall,
                    _terminal.State,
                    "A gamepad binding must keep running its message while the field has focus"
                );
            }
            finally
            {
                InputSystem.RemoveDevice(gamepad);
            }
        }

        private void Bind(string actionName, string keyPath)
        {
            InputAction action = _asset.FindAction(actionName);
            Assert.That(action != null, $"Sanity: the rig created the {actionName} action");
            action.performed += OnActionPerformed;
            action.AddBinding(keyPath);
            action.Enable();
            _actions.Add(action);
        }

        private void OnActionPerformed(InputAction.CallbackContext context)
        {
            _performedControls.Add(context.action.activeControl?.name ?? string.Empty);
        }

        private IEnumerator SpawnTerminal(bool open)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("PlayerInputTerminal");
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

            /*
                Both input components sit on the terminal's own object, which
                is the arrangement the controller resolves in Awake: it reads
                the PlayerInput for the action behind a message and the
                TerminalUI for the field that owns focus.

                The actions exist before PlayerInput enables, because
                PlayerInput caches the action-name to message-name map when
                it enables and a name added later is never sent.
             */
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = new("Console");
            foreach (string message in RoutedMessages())
            {
                map.AddAction(message, ActionType);
            }

            _asset.AddActionMap(map);
            _playerInput = _terminalObject.AddComponent<PlayerInput>();
            _playerInput.actions = _asset;
            _terminalObject.AddComponent<TerminalPlayerInputController>();

            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("PlayerInput coverage runs in the editor Play Mode suite.");
            yield break;
#endif

            if (!open)
            {
                yield break;
            }

            _terminal.SetState(TerminalState.OpenFull);
            yield return null;
            yield return WaitForInputVisible("Sanity: the rig's terminal opens");
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

        private IEnumerator WaitForFocusedInput(string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !_terminal.InputOwnsFocus)
            {
                yield return null;
            }

            Assert.IsTrue(_terminal.InputOwnsFocus, message);
        }

#if UNITY_EDITOR
        private static T LoadAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.That(asset != null, $"Expected the test asset at {PackageRoot}/{relativePath}");
            return asset;
        }
#endif
    }
}
