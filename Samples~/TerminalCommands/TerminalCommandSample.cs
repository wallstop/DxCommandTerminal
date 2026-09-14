namespace WallstopStudios.DxCommandTerminal.Samples
{
    using System.Collections.Generic;
    using Backend;
    using UnityEngine;

    /*
        Shared base for the sample components: registers builder commands on
        enable and disposes every handle on disable, so a disabled or
        destroyed component removes exactly its own commands. Two hazards the
        samples defend against: component enable order is undefined, so
        OnEnable can run before the TerminalUI built its shell (Register
        skips with an actionable error instead of throwing), and if the
        TerminalUI uses Reset State On Init it rebuilds the shell during its
        own startup - register from Start instead then (see the package
        README), or the registration is discarded.
     */
    public abstract class TerminalCommandSample : MonoBehaviour
    {
        private readonly List<CommandRegistrationHandle> _handles = new();

        protected virtual void OnEnable()
        {
            RegisterCommands();
        }

        protected virtual void OnDisable()
        {
            foreach (CommandRegistrationHandle handle in _handles)
            {
                handle.Dispose();
            }

            _handles.Clear();
        }

        protected void Register(CommandBuilder builder)
        {
            if (Terminal.Shell == null)
            {
                Debug.LogError(
                    $"{GetType().Name} enabled before any TerminalUI; commands were not "
                        + "registered. Keep the component in a scene with a TerminalUI, or "
                        + "register from Start instead.",
                    this
                );
                return;
            }

            /*
                AddCommand returns false for a duplicate name against the
                live shell (the shell queues that error) and leaves the
                handle null; only a successful registration owns a handle.
             */
            if (Terminal.Shell.AddCommand(builder, out CommandRegistrationHandle handle))
            {
                _handles.Add(handle);
            }
        }

        protected abstract void RegisterCommands();
    }
}
