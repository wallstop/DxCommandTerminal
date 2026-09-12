namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Threading;

    /// <summary>
    ///     Disposable registration handle for a builder command. Disposing it
    ///     removes exactly the command this handle registered — never a later
    ///     replacement with the same name — so instance-owned commands can
    ///     register on enable and dispose on disable without leaking.
    /// </summary>
    /// <remarks>
    ///     Disposal is idempotent. Once disposed (or after the shell itself
    ///     cleared or replaced the command), later disposal is a no-op.
    /// </remarks>
    public sealed class CommandRegistrationHandle : IDisposable
    {
        /// <summary>True once this handle has disposed its registration.</summary>
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        private readonly CommandShell _shell;
        private readonly string _name;
        private readonly CommandHandler _registration;

        /*
            One once a dispose has run. Interlocked keeps concurrent dispose
            calls to a single removal, like the shell's readiness handoff.
         */
        private int _disposed;

        internal CommandRegistrationHandle(
            CommandShell shell,
            string name,
            CommandHandler registration
        )
        {
            _shell = shell;
            _name = name;
            _registration = registration;
        }

        /// <summary>
        ///     Removes the command this handle registered. When the command was
        ///     already replaced or cleared through the shell, nothing is
        ///     removed; disposing twice is safe.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _shell.TryRemoveCommand(_name, _registration);
        }
    }
}
