namespace WallstopStudios.DxCommandTerminal.Input
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UI;
    using UnityEngine;

    [DisallowMultipleComponent]
    public class TerminalKeyboardController : MonoBehaviour, IInputHandler
    {
        protected static readonly TerminalControlTypes[] ControlTypes = Enum.GetValues(
                typeof(TerminalControlTypes)
            )
            .OfType<TerminalControlTypes>()
#pragma warning disable CS0612 // Type or member is obsolete
            .Except(new[] { TerminalControlTypes.None })
#pragma warning restore CS0612 // Type or member is obsolete
            .ToArray();

        public bool ShouldHandleInputThisFrame
        {
            get
            {
                foreach (TerminalControlTypes controlType in _controlOrder)
                {
                    if (!_inputChecks.TryGetValue(controlType, out Func<bool> inputCheck))
                    {
                        continue;
                    }
                    if (inputCheck())
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        [Header("System")]
        public InputMode inputMode =
#if ENABLE_INPUT_SYSTEM
        InputMode.NewInputSystem;
#else
        InputMode.LegacyInputSystem;
#endif
        public TerminalUI terminal;

        [Header("Hotkeys")]
        [SerializeField]
        public string toggleHotkey = "`";

        [SerializeField]
        public string toggleFullHotkey = "#`";

        [SerializeField]
        public string completeHotkey = "tab";

        [SerializeField]
        public string reverseCompleteHotkey = "#tab";

        [SerializeField]
        public string previousHotkey = "up";

        [SerializeField]
        public List<string> _completeCommandHotkeys = new() { "enter", "return" };

        [SerializeField]
        public string closeHotkey = "escape";

        [SerializeField]
        public string nextHotkey = "down";

        [SerializeField]
        [Tooltip("Re-order these to choose what priority you want input to be checked in")]
        protected List<TerminalControlTypes> _controlOrder = new()
        {
            TerminalControlTypes.Close,
            TerminalControlTypes.EnterCommand,
            TerminalControlTypes.Previous,
            TerminalControlTypes.Next,
            TerminalControlTypes.ToggleFull,
            TerminalControlTypes.ToggleSmall,
            TerminalControlTypes.CompleteBackward,
            TerminalControlTypes.CompleteForward,
        };

        protected readonly Dictionary<TerminalControlTypes, Func<bool>> _inputChecks = new();
        protected readonly Dictionary<TerminalControlTypes, Action> _controlHandlerActions = new();

        public TerminalKeyboardController()
        {
            _inputChecks.Clear();
            _inputChecks[TerminalControlTypes.Close] = IsClosePressed;
            _inputChecks[TerminalControlTypes.EnterCommand] = IsEnterCommandPressed;
            _inputChecks[TerminalControlTypes.Previous] = IsPreviousPressed;
            _inputChecks[TerminalControlTypes.Next] = IsNextPressed;
            _inputChecks[TerminalControlTypes.ToggleFull] = IsToggleFullPressed;
            _inputChecks[TerminalControlTypes.ToggleSmall] = IsToggleSmallPressed;
            _inputChecks[TerminalControlTypes.CompleteBackward] = IsCompleteBackwardPressed;
            _inputChecks[TerminalControlTypes.CompleteForward] = IsCompletePressed;

            _controlHandlerActions.Clear();
            _controlHandlerActions[TerminalControlTypes.Close] = Close;
            _controlHandlerActions[TerminalControlTypes.EnterCommand] = EnterCommand;
            _controlHandlerActions[TerminalControlTypes.Previous] = Previous;
            _controlHandlerActions[TerminalControlTypes.Next] = Next;
            _controlHandlerActions[TerminalControlTypes.ToggleFull] = ToggleFull;
            _controlHandlerActions[TerminalControlTypes.ToggleSmall] = ToggleSmall;
            _controlHandlerActions[TerminalControlTypes.CompleteBackward] = CompleteBackward;
            _controlHandlerActions[TerminalControlTypes.CompleteForward] = Complete;
        }

        protected virtual void Awake()
        {
            if (terminal != null)
            {
                return;
            }

            if (!TryGetComponent(out terminal))
            {
                Debug.LogError("Failed to find TerminalUI, Input will not work.", this);
            }

            if (_controlOrder is not { Count: > 0 })
            {
                Debug.LogError("No controls specified, Input will not work.", this);
            }
            else
            {
                VerifyControlOrderIntegrity();
            }
        }

        protected virtual void OnValidate()
        {
            if (!Application.isPlaying)
            {
                VerifyControlOrderIntegrity();
            }
        }

        private void VerifyControlOrderIntegrity()
        {
            if (!_controlOrder.ToHashSet().SetEquals(ControlTypes))
            {
                Debug.LogWarning(
                    $"Control Order is missing the following controls: [{string.Join(", ", ControlTypes.Except(_controlOrder))}]. "
                        + "Input for these will not be handled. Is this intentional?",
                    this
                );
            }
        }

        protected virtual void Update()
        {
            if (_controlOrder is not { Count: > 0 })
            {
                return;
            }

            foreach (TerminalControlTypes controlType in _controlOrder)
            {
                if (!_inputChecks.TryGetValue(controlType, out Func<bool> inputCheck))
                {
                    continue;
                }

                if (!inputCheck())
                {
                    continue;
                }

                if (!_controlHandlerActions.TryGetValue(controlType, out Action action))
                {
                    continue;
                }

                action();
                break;
            }
        }

        #region Commands

        protected virtual void Close()
        {
            if (terminal == null)
            {
                return;
            }

            terminal.Close();
        }

        protected virtual void EnterCommand()
        {
            if (terminal == null)
            {
                return;
            }
            terminal.EnterCommand();
        }

        protected virtual void Previous()
        {
            if (terminal == null)
            {
                return;
            }
            terminal.HandlePrevious();
        }

        protected virtual void Next()
        {
            if (terminal == null)
            {
                return;
            }
            terminal.HandleNext();
        }

        protected virtual void ToggleFull()
        {
            if (terminal == null)
            {
                return;
            }
            terminal.ToggleFull();
        }

        protected virtual void ToggleSmall()
        {
            if (terminal == null)
            {
                return;
            }
            terminal.ToggleSmall();
        }

        protected virtual void Complete()
        {
            if (terminal == null)
            {
                return;
            }

            terminal.CompleteCommand(searchForward: true);
        }

        protected virtual void CompleteBackward()
        {
            if (terminal == null)
            {
                return;
            }
            terminal.CompleteCommand(searchForward: false);
        }

        #endregion


        #region Control Checks

        protected virtual bool IsClosePressed()
        {
            return InputHelpers.IsKeyPressed(closeHotkey, inputMode);
        }

        protected virtual bool IsPreviousPressed()
        {
            return InputHelpers.IsKeyPressed(previousHotkey, inputMode);
        }

        protected virtual bool IsNextPressed()
        {
            return InputHelpers.IsKeyPressed(nextHotkey, inputMode);
        }

        protected virtual bool IsToggleFullPressed()
        {
            return InputHelpers.IsKeyPressed(toggleFullHotkey, inputMode);
        }

        protected virtual bool IsToggleSmallPressed()
        {
            return InputHelpers.IsKeyPressed(toggleHotkey, inputMode);
        }

        protected virtual bool IsCompleteBackwardPressed()
        {
            return InputHelpers.IsKeyPressed(reverseCompleteHotkey, inputMode);
        }

        protected virtual bool IsCompletePressed()
        {
            return InputHelpers.IsKeyPressed(completeHotkey, inputMode);
        }

        protected virtual bool IsEnterCommandPressed()
        {
            if (_completeCommandHotkeys is not { Count: > 0 })
            {
                return false;
            }

            foreach (string command in _completeCommandHotkeys)
            {
                if (InputHelpers.IsKeyPressed(command, inputMode))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
