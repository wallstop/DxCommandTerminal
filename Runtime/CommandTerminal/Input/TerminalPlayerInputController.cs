namespace WallstopStudios.DxCommandTerminal.Input
{
#if ENABLE_INPUT_SYSTEM
    using UI;
    using UnityEngine;
    using UnityEngine.InputSystem;

    [DisallowMultipleComponent]
    public class TerminalPlayerInputController : MonoBehaviour
    {
        /// <summary>
        ///     Whether either console surface's text field currently holds
        ///     panel focus. The live read is the shipped behavior; a game
        ///     that wants a non-toggle character binding to keep running
        ///     while the field has focus overrides this to <c>false</c>,
        ///     which restores the pre-rule behavior for the six messages the
        ///     rule covers. The two toggle messages do not consult this
        ///     property - their key is reserved (#218).
        /// </summary>
        protected virtual bool TextInputOwnsFocus => TerminalUI.AnyConsoleFieldOwnsFocus();

        [Header("System")]
        public bool enableWarnings = true;

        public TerminalUI terminal;

        protected bool _enabled;

        [SerializeField]
        protected PlayerInput _serializedPlayerInput;

        protected PlayerInput _playerInput;

        public virtual void OnHandlePrevious(InputValue inputValue)
        {
            if (!ShouldHandleMessage("HandlePrevious"))
            {
                return;
            }
            terminal.HandlePrevious();
        }

        public virtual void OnHandleNext(InputValue inputValue)
        {
            if (!ShouldHandleMessage("HandleNext"))
            {
                return;
            }
            terminal.HandleNext();
        }

        public virtual void OnClose(InputValue inputValue)
        {
            if (!ShouldHandleMessage("Close"))
            {
                return;
            }
            terminal.Close();
        }

        public virtual void OnToggleSmall(InputValue inputValue)
        {
            /*
                The toggle key is reserved: it opens and closes the surface
                (#218), so the typing gate that holds a character binding for
                the focused field does not apply to toggles - only the live
                gate does.
             */
            if (!IsLive())
            {
                return;
            }
            terminal.ToggleSmall();
        }

        public virtual void OnToggleFull(InputValue inputValue)
        {
            if (!IsLive())
            {
                return;
            }
            terminal.ToggleFull();
        }

        public virtual void OnCompleteCommand(InputValue input)
        {
            if (!ShouldHandleMessage("CompleteCommand"))
            {
                return;
            }
            terminal.CompleteCommand(searchForward: true);
        }

        public virtual void OnReverseCompleteCommand(InputValue input)
        {
            if (!ShouldHandleMessage("ReverseCompleteCommand"))
            {
                return;
            }
            terminal.CompleteCommand(searchForward: false);
        }

        public virtual void OnEnterCommand(InputValue inputValue)
        {
            if (!ShouldHandleMessage("EnterCommand"))
            {
                return;
            }
            terminal.EnterCommand();
        }

        protected virtual void Awake()
        {
            _playerInput = _serializedPlayerInput;
            if (_playerInput == null)
            {
                if (!TryGetComponent(out _playerInput) && enableWarnings)
                {
                    Debug.LogWarning(
                        "No PlayerInput attached, events may not work (which is the point of this component).",
                        this
                    );
                }
            }

            if (terminal != null)
            {
                return;
            }

            if (!TryGetComponent(out terminal))
            {
                Debug.LogError("Failed to find TerminalUI, Input will not work.", this);
            }
        }

        protected virtual void OnEnable()
        {
            _enabled = true;
        }

        protected virtual void OnDisable()
        {
            _enabled = false;
        }

        /*
            The shared gate for every message. A disabled component and a
            missing terminal drop the message. A key that types text is left
            to the console field holding focus, exactly as a polled hotkey is
            (see InputHelpers.ProducesTypedText): the character is not
            consumed, so the field keeps it, and the action runs the next time
            it fires with no field focused.

            The action name is the string each handler passes: PlayerInput
            turns the action "ToggleSmall" into the message "OnToggleSmall",
            so the name is the only link from a message back to the control
            that performed it. A message that PlayerInput does not send a
            control with - a hand-sent message, a release that has already
            cleared the control, a name that resolves to no action - runs as
            it always has, as does every control that is not a typing key.

            The two toggle messages do not pass through here: their handlers
            answer only <see cref="IsLive"/>, because the stroke that opens a
            surface must be the stroke that closes it (#218).
         */
        private bool ShouldHandleMessage(string actionName)
        {
            if (!IsLive())
            {
                return false;
            }

            if (!TextInputOwnsFocus)
            {
                return true;
            }

            if (
                !TryGetDrivingControl(actionName, out InputControl drivingControl)
                || !InputHelpers.ControlProducesTypedText(drivingControl)
            )
            {
                return true;
            }

            return false;
        }

        private bool IsLive()
        {
            return _enabled && terminal != null;
        }

        /*
            The control that performed the action behind a message. The
            current action map is asked first: the same action name can exist
            in two maps, only one of which is enabled, and an action that
            never ran has no active control - so the wrong map would answer
            "no control" and the rule would not apply. The asset is the
            fallback for an action the map does not carry, and a name that
            resolves to nothing leaves the message to run as it always has.
         */
        private bool TryGetDrivingControl(string actionName, out InputControl control)
        {
            InputAction action = FindAction(actionName);
            control = action == null ? null : action.activeControl;
            return action != null;
        }

        private InputAction FindAction(string actionName)
        {
            if (_playerInput == null)
            {
                return null;
            }

            InputActionMap currentMap = _playerInput.currentActionMap;
            InputAction action = currentMap == null ? null : currentMap.FindAction(actionName);
            if (action != null)
            {
                return action;
            }

            InputActionAsset actions = _playerInput.actions;
            return actions == null ? null : actions.FindAction(actionName);
        }
    }
#endif
}
