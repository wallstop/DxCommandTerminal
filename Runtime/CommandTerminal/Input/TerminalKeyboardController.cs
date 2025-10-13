namespace WallstopStudios.DxCommandTerminal.Input
{
    using System;
    using System.Collections.Generic;
    using UI;
    using UnityEngine;

    [DisallowMultipleComponent]
    public class TerminalKeyboardController : MonoBehaviour, IInputHandler
    {
        protected static readonly TerminalControlTypes[] ControlTypes = BuildControlTypes();

        private static TerminalControlTypes[] BuildControlTypes()
        {
            Array values = Enum.GetValues(typeof(TerminalControlTypes));
            List<TerminalControlTypes> list = new();
            for (int i = 0; i < values.Length; ++i)
            {
                object v = values.GetValue(i);
                if (v is TerminalControlTypes t)
                {
#pragma warning disable CS0612 // Type or member is obsolete
                    if (t == TerminalControlTypes.None)
                    {
                        continue;
                    }
#pragma warning restore CS0612 // Type or member is obsolete
                    list.Add(t);
                }
            }
            return list.ToArray();
        }

        public bool ShouldHandleInputThisFrame
        {
            get
            {
                foreach (TerminalControlTypes controlType in _controlOrder)
                {
                    if (IsControlPressed(controlType))
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

        public TerminalKeyboardController() { }

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
            // Verify set equality without LINQ
            HashSet<TerminalControlTypes> set = new HashSet<TerminalControlTypes>(_controlOrder);
            bool equal = set.Count == ControlTypes.Length;
            if (equal)
            {
                for (int i = 0; i < ControlTypes.Length; ++i)
                {
                    if (!set.Contains(ControlTypes[i]))
                    {
                        equal = false;
                        break;
                    }
                }
            }

            if (!equal)
            {
                // Build missing list for message
                List<string> missing = new List<string>();
                for (int i = 0; i < ControlTypes.Length; ++i)
                {
                    TerminalControlTypes t = ControlTypes[i];
                    if (!set.Contains(t))
                    {
                        missing.Add(t.ToString());
                    }
                }

                Debug.LogWarning(
                    $"Control Order is missing the following controls: [{string.Join(", ", missing)}]. "
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
                if (!IsControlPressed(controlType))
                {
                    continue;
                }

                ExecuteControl(controlType);
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

        private bool IsControlPressed(TerminalControlTypes controlType)
        {
            switch (controlType)
            {
                case TerminalControlTypes.Close:
                    return IsClosePressed();
                case TerminalControlTypes.EnterCommand:
                    return IsEnterCommandPressed();
                case TerminalControlTypes.Previous:
                    return IsPreviousPressed();
                case TerminalControlTypes.Next:
                    return IsNextPressed();
                case TerminalControlTypes.ToggleFull:
                    return IsToggleFullPressed();
                case TerminalControlTypes.ToggleSmall:
                    return IsToggleSmallPressed();
                case TerminalControlTypes.CompleteBackward:
                    return IsCompleteBackwardPressed();
                case TerminalControlTypes.CompleteForward:
                    return IsCompletePressed();
                default:
                    return false;
            }
        }

        private void ExecuteControl(TerminalControlTypes controlType)
        {
            switch (controlType)
            {
                case TerminalControlTypes.Close:
                    Close();
                    break;
                case TerminalControlTypes.EnterCommand:
                    EnterCommand();
                    break;
                case TerminalControlTypes.Previous:
                    Previous();
                    break;
                case TerminalControlTypes.Next:
                    Next();
                    break;
                case TerminalControlTypes.ToggleFull:
                    ToggleFull();
                    break;
                case TerminalControlTypes.ToggleSmall:
                    ToggleSmall();
                    break;
                case TerminalControlTypes.CompleteBackward:
                    CompleteBackward();
                    break;
                case TerminalControlTypes.CompleteForward:
                    Complete();
                    break;
            }
        }
    }
}
