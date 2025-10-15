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

        protected readonly HashSet<string> _missing = new();
        protected readonly HashSet<TerminalControlTypes> _terminalControlTypes = new();
        protected ITerminalInputTarget _inputTarget;
        private ITerminalInputSource _inputSource;
        private bool _missingTargetLogged;

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

        [Header("Profiles")]
        [SerializeField]
        private TerminalInputProfile _inputProfile;

        [Header("Hotkeys")]
        public string toggleHotkey = "`";
        public string toggleFullHotkey = "#`";
        public string toggleLauncherHotkey = "#space";
        public string completeHotkey = "tab";
        public string reverseCompleteHotkey = "#tab";
        public string previousHotkey = "up";
        public List<string> _completeCommandHotkeys = new() { "enter", "return" };
        public string closeHotkey = "escape";
        public string nextHotkey = "down";

        [SerializeField]
        [Tooltip("Re-order these to choose what priority you want input to be checked in")]
        protected internal List<TerminalControlTypes> _controlOrder = new()
        {
            TerminalControlTypes.Close,
            TerminalControlTypes.EnterCommand,
            TerminalControlTypes.Previous,
            TerminalControlTypes.Next,
            TerminalControlTypes.ToggleLauncher,
            TerminalControlTypes.ToggleFull,
            TerminalControlTypes.ToggleSmall,
            TerminalControlTypes.CompleteBackward,
            TerminalControlTypes.CompleteForward,
        };

        public TerminalKeyboardController() { }

        protected virtual void Awake()
        {
            ResolveInputTarget();
            ResolveInputSource();

            ApplyProfileIfAvailable();

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
                ResolveInputTarget();
                ApplyProfileIfAvailable();
                VerifyControlOrderIntegrity();
                ResolveInputSource();
            }
        }

        private void VerifyControlOrderIntegrity()
        {
            // Verify set equality without LINQ
            _terminalControlTypes.Clear();
            foreach (TerminalControlTypes controlType in _controlOrder)
            {
                _terminalControlTypes.Add(controlType);
            }
            bool equal = _terminalControlTypes.Count == ControlTypes.Length;
            if (equal)
            {
                for (int i = 0; i < ControlTypes.Length; ++i)
                {
                    if (!_terminalControlTypes.Contains(ControlTypes[i]))
                    {
                        equal = false;
                        break;
                    }
                }
            }

            if (!equal)
            {
                // Build missing list for message
                _missing.Clear();
                for (int i = 0; i < ControlTypes.Length; ++i)
                {
                    TerminalControlTypes t = ControlTypes[i];
                    if (!_terminalControlTypes.Contains(t))
                    {
                        _missing.Add(t.ToString());
                    }
                }

                Debug.LogError(
                    $"Control Order is missing the following controls: [{string.Join(", ", _missing)}]. "
                        + "Input for these will not be handled. Is this intentional?"
                        + $"\nTerminal Control Types: [{string.Join(", ", ControlTypes)}]"
                        + $"\nExisting Control Types: [{string.Join(", ", _terminalControlTypes)}]",
                    this
                );
            }
        }

        protected virtual void Update()
        {
            if (_inputTarget == null)
            {
                ResolveInputTarget();
                if (_inputTarget == null)
                {
                    return;
                }
            }

            if (_inputSource == null)
            {
                ResolveInputSource();
                if (_inputSource == null)
                {
                    return;
                }
            }

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
            if (_inputTarget == null)
            {
                return;
            }

            _inputTarget.Close();
        }

        protected virtual void EnterCommand()
        {
            if (_inputTarget == null)
            {
                return;
            }
            _inputTarget.EnterCommand();
        }

        protected virtual void Previous()
        {
            if (_inputTarget == null)
            {
                return;
            }
            _inputTarget.HandlePrevious();
        }

        protected virtual void Next()
        {
            if (_inputTarget == null)
            {
                return;
            }
            _inputTarget.HandleNext();
        }

        protected virtual void ToggleFull()
        {
            if (_inputTarget == null)
            {
                return;
            }
            _inputTarget.ToggleFull();
        }

        protected virtual void ToggleLauncher()
        {
            if (_inputTarget == null)
            {
                return;
            }
            _inputTarget.ToggleLauncher();
        }

        protected virtual void ToggleSmall()
        {
            if (_inputTarget == null)
            {
                return;
            }
            _inputTarget.ToggleSmall();
        }

        protected virtual void Complete()
        {
            if (_inputTarget == null)
            {
                return;
            }

            _inputTarget.CompleteCommand(searchForward: true);
        }

        protected virtual void CompleteBackward()
        {
            if (_inputTarget == null)
            {
                return;
            }
            _inputTarget.CompleteCommand(searchForward: false);
        }

        #endregion


        #region Control Checks

        protected virtual bool IsClosePressed()
        {
            return _inputSource != null && _inputSource.IsKeyPressed(closeHotkey);
        }

        protected virtual bool IsPreviousPressed()
        {
            return _inputSource != null && _inputSource.IsKeyPressed(previousHotkey);
        }

        protected virtual bool IsNextPressed()
        {
            return _inputSource != null && _inputSource.IsKeyPressed(nextHotkey);
        }

        protected virtual bool IsToggleFullPressed()
        {
            return _inputSource != null && _inputSource.IsKeyPressed(toggleFullHotkey);
        }

        protected virtual bool IsToggleLauncherPressed()
        {
            return _inputSource != null && _inputSource.IsKeyPressed(toggleLauncherHotkey);
        }

        protected virtual bool IsToggleSmallPressed()
        {
            return _inputSource != null && _inputSource.IsKeyPressed(toggleHotkey);
        }

        protected virtual bool IsCompleteBackwardPressed()
        {
            return _inputSource != null && _inputSource.IsKeyPressed(reverseCompleteHotkey);
        }

        protected virtual bool IsCompletePressed()
        {
            return _inputSource != null && _inputSource.IsKeyPressed(completeHotkey);
        }

        protected virtual bool IsEnterCommandPressed()
        {
            if (_completeCommandHotkeys is not { Count: > 0 })
            {
                return false;
            }

            foreach (string command in _completeCommandHotkeys)
            {
                if (_inputSource != null && _inputSource.IsKeyPressed(command))
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
                case TerminalControlTypes.ToggleLauncher:
                    return IsToggleLauncherPressed();
                case TerminalControlTypes.CompleteBackward:
                    return IsCompleteBackwardPressed();
                case TerminalControlTypes.CompleteForward:
                    return IsCompletePressed();
                default:
                    return false;
            }
        }

        protected virtual void ExecuteControl(TerminalControlTypes controlType)
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
                case TerminalControlTypes.ToggleLauncher:
                    ToggleLauncher();
                    break;
                case TerminalControlTypes.CompleteBackward:
                    CompleteBackward();
                    break;
                case TerminalControlTypes.CompleteForward:
                    Complete();
                    break;
            }
        }

        private void ResolveInputTarget()
        {
            if (terminal != null)
            {
                _inputTarget = terminal;
                _missingTargetLogged = false;
                return;
            }

            if (!TryGetComponent<ITerminalInputTarget>(out ITerminalInputTarget resolvedTarget))
            {
                MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length && resolvedTarget == null; ++i)
                {
                    if (behaviours[i] is ITerminalInputTarget candidate)
                    {
                        resolvedTarget = candidate;
                    }
                }
            }

            if (resolvedTarget != null)
            {
                _inputTarget = resolvedTarget;
                terminal = resolvedTarget as TerminalUI;
                _missingTargetLogged = false;
            }
            else if (!_missingTargetLogged)
            {
                Debug.LogWarning(
                    "Failed to locate a terminal input target. Input will not work.",
                    this
                );
                _missingTargetLogged = true;
            }
        }

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        internal void ExecuteControlForTests(TerminalControlTypes controlType)
        {
            ExecuteControl(controlType);
        }

        internal void SetInputProfileForTests(TerminalInputProfile profile)
        {
            _inputProfile = profile;
            ApplyProfileIfAvailable();
        }
#endif

        private void ApplyProfileIfAvailable()
        {
            if (_inputProfile == null)
            {
                return;
            }

            _inputProfile.ApplyTo(this);
            ResolveInputSource();
        }

        private void ResolveInputSource()
        {
            _inputSource = TerminalInputSourceFactory.Create(inputMode);
        }
    }
}
