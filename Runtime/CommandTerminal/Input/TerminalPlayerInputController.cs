namespace WallstopStudios.DxCommandTerminal.Input
{
#if ENABLE_INPUT_SYSTEM
    using UI;
    using UnityEngine;
    using UnityEngine.InputSystem;

    [DisallowMultipleComponent]
    public class TerminalPlayerInputController : MonoBehaviour
    {
        [Header("System")]
        public bool enableWarnings = true;

        public TerminalUI terminal;

        protected bool _enabled;

        [SerializeField]
        protected PlayerInput _serializedPlayerInput;

        protected PlayerInput _playerInput;

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

        public virtual void OnHandlePrevious(InputValue inputValue)
        {
            if (!_enabled)
            {
                return;
            }
            if (terminal == null)
            {
                return;
            }
            terminal.HandlePrevious();
        }

        public virtual void OnHandleNext(InputValue inputValue)
        {
            if (!_enabled)
            {
                return;
            }
            if (terminal == null)
            {
                return;
            }
            terminal.HandleNext();
        }

        public virtual void OnClose(InputValue inputValue)
        {
            if (!_enabled)
            {
                return;
            }
            if (terminal == null)
            {
                return;
            }
            terminal.Close();
        }

        public virtual void OnToggleSmall(InputValue inputValue)
        {
            if (!_enabled)
            {
                return;
            }
            if (terminal == null)
            {
                return;
            }
            terminal.ToggleSmall();
        }

        public virtual void OnToggleFull(InputValue inputValue)
        {
            if (!_enabled)
            {
                return;
            }
            if (terminal == null)
            {
                return;
            }
            terminal.ToggleFull();
        }

        public virtual void OnCompleteCommand(InputValue input)
        {
            if (!_enabled)
            {
                return;
            }
            if (terminal == null)
            {
                return;
            }
            terminal.CompleteCommand(searchForward: true);
        }

        public virtual void OnReverseCompleteCommand(InputValue input)
        {
            if (!_enabled)
            {
                return;
            }
            if (terminal == null)
            {
                return;
            }
            terminal.CompleteCommand(searchForward: false);
        }

        public virtual void OnEnterCommand(InputValue inputValue)
        {
            if (!_enabled)
            {
                return;
            }
            if (terminal == null)
            {
                return;
            }
            terminal.EnterCommand();
        }
    }
#endif
}
