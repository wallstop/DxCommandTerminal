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

        /// <summary>
        ///     Whether either console surface's text field currently holds
        ///     panel focus. Overridable for test coverage; the live read is
        ///     the shipped behavior.
        /// </summary>
        protected virtual bool TextInputOwnsFocus => TerminalUI.AnyConsoleFieldOwnsFocus();

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

        private static TerminalControlTypes[] BuildControlTypes()
        {
            /*
                Written out explicitly rather than via Enum.GetValues: no
                runtime reflection, IL2CPP/WebGL safe. The
                ControlTypesContainsAllNonNoneEnumValues test fails when a new
                enum member is not added here.
             */
#pragma warning disable CS0612 // Type or member is obsolete
            return new[]
            {
                TerminalControlTypes.Close,
                TerminalControlTypes.EnterCommand,
                TerminalControlTypes.Previous,
                TerminalControlTypes.Next,
                TerminalControlTypes.ToggleFull,
                TerminalControlTypes.ToggleSmall,
                TerminalControlTypes.CompleteForward,
                TerminalControlTypes.CompleteBackward,
            };
#pragma warning restore CS0612 // Type or member is obsolete
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

        /// <summary>
        ///     Whether the key behind <paramref name="hotkey"/> is down this
        ///     frame. Overridable for test coverage, so a test can simulate a
        ///     press without the real Input API.
        /// </summary>
        protected virtual bool IsHotkeyDown(string hotkey)
        {
            return InputHelpers.IsKeyPressed(hotkey, inputMode);
        }

        protected virtual bool IsClosePressed()
        {
            return IsHotkeyActive(closeHotkey);
        }

        protected virtual bool IsPreviousPressed()
        {
            return IsHotkeyActive(previousHotkey);
        }

        protected virtual bool IsNextPressed()
        {
            return IsHotkeyActive(nextHotkey);
        }

        protected virtual bool IsToggleFullPressed()
        {
            return IsHotkeyActive(toggleFullHotkey);
        }

        protected virtual bool IsToggleSmallPressed()
        {
            return IsHotkeyActive(toggleHotkey);
        }

        protected virtual bool IsCompleteBackwardPressed()
        {
            return IsHotkeyActive(reverseCompleteHotkey);
        }

        protected virtual bool IsCompletePressed()
        {
            return IsHotkeyActive(completeHotkey);
        }

        protected virtual bool IsEnterCommandPressed()
        {
            if (_completeCommandHotkeys is not { Count: > 0 })
            {
                return false;
            }

            foreach (string command in _completeCommandHotkeys)
            {
                if (IsHotkeyActive(command))
                {
                    return true;
                }
            }

            return false;
        }

        /*
            A key that types text belongs to the console field holding focus,
            not to this poll: the character reaches the command line or the
            palette query, and the action waits for a frame with no focused
            field. A Ctrl chord or a non-typing named key keeps firing while
            the user types (see InputHelpers.ProducesTypedText).

            Overriding a per-control check replaces this call; call it from an
            override to keep the rule.
         */
        protected bool IsHotkeyActive(string hotkey)
        {
            if (InputHelpers.ProducesTypedText(hotkey) && TextInputOwnsFocus)
            {
                return false;
            }

            return IsHotkeyDown(hotkey);
        }

        private void VerifyControlOrderIntegrity()
        {
            List<TerminalControlTypes> missingControls = null;
            foreach (TerminalControlTypes controlType in ControlTypes)
            {
                if (!_controlOrder.Contains(controlType))
                {
                    missingControls ??= new List<TerminalControlTypes>();
                    missingControls.Add(controlType);
                }
            }

            if (missingControls != null)
            {
                Debug.LogWarning(
                    $"Control Order is missing the following controls: [{string.Join(", ", missingControls)}]. "
                        + "Input for these will not be handled. Is this intentional?",
                    this
                );
            }
        }
        #endregion
    }
}
