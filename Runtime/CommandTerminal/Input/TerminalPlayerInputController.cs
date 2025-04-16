namespace WallstopStudios.DxCommandTerminal.Input
{
#if ENABLE_INPUT_SYSTEM
    using UI;
    using UnityEngine;
    using UnityEngine.InputSystem;

    public class TerminalPlayerInputController : MonoBehaviour
    {
        [Header("System")]
        public bool enableWarnings = true;

        public TerminalUI terminal;

        private void Awake()
        {
            if (enableWarnings && !TryGetComponent(out PlayerInput _))
            {
                Debug.LogWarning(
                    "No PlayerInput attached, events may not work (which is the point of this component)."
                );
            }

            if (terminal != null)
            {
                return;
            }

            if (!TryGetComponent(out terminal))
            {
                Debug.LogError("Failed to find TerminalUI, Input will not work.");
            }
        }

        public virtual void OnHandlePrevious(InputValue inputValue)
        {
            if (terminal == null)
            {
                return;
            }
            terminal.HandlePrevious();
        }

        public virtual void OnHandleNext(InputValue inputValue)
        {
            if (terminal == null)
            {
                return;
            }
            terminal.HandleNext();
        }

        public virtual void OnClose(InputValue inputValue)
        {
            if (terminal == null)
            {
                return;
            }
            terminal.Close();
        }

        public virtual void OnToggleSmall(InputValue inputValue)
        {
            if (terminal == null)
            {
                return;
            }
            terminal.ToggleSmall();
        }

        public virtual void OnToggleFull(InputValue inputValue)
        {
            if (terminal == null)
            {
                return;
            }
            terminal.ToggleFull();
        }

        public virtual void OnCompleteCommand(InputValue input)
        {
            if (terminal == null)
            {
                return;
            }
            terminal.CompleteCommand(searchForward: true);
        }

        public virtual void OnReverseCompleteCommand(InputValue input)
        {
            if (terminal == null)
            {
                return;
            }
            terminal.CompleteCommand(searchForward: false);
        }

        public virtual void OnEnterCommand(InputValue inputValue)
        {
            if (terminal == null)
            {
                return;
            }
            terminal.EnterCommand();
        }
    }
#endif
}
