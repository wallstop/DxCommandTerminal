namespace WallstopStudios.DxCommandTerminal.Samples
{
    using Backend;
    using UnityEngine;

    /*
        The raw registration lifetime, without the shared base: OnEnable
        registers and keeps the handle, OnDisable disposes it. Disposing
        removes exactly this registration - never a later replacement with
        the same name - so enable/disable cycles and re-registrations stay
        safe. Attach alongside a TerminalUI and watch the log while
        toggling the component.
     */
    public sealed class LifecycleCommands : MonoBehaviour
    {
        private CommandRegistrationHandle _pingHandle;

        private void OnEnable()
        {
            if (Terminal.Shell == null)
            {
                Debug.LogError(
                    "LifecycleCommands enabled before any TerminalUI; ping was not "
                        + "registered. Keep the component in a scene with a TerminalUI, or "
                        + "register from Start instead.",
                    this
                );
                return;
            }

            /*
                A failed add (duplicate name) leaves the handle null; the
                disable path null-guards it.
             */
            if (
                !Terminal.Shell.AddCommand(
                    CommandBuilder
                        .Create("ping", "Replies with pong")
                        .Handler((context, arguments) => Terminal.Log("pong")),
                    out _pingHandle
                )
            )
            {
                Terminal.Log("ping was not registered (duplicate name).");
                return;
            }

            Terminal.Log("ping registered (handle disposed: {0})", _pingHandle.IsDisposed);
        }

        private void OnDisable()
        {
            if (_pingHandle != null && !_pingHandle.IsDisposed)
            {
                _pingHandle.Dispose();
                Terminal.Log("ping unregistered");
            }

            _pingHandle = null;
        }
    }
}
