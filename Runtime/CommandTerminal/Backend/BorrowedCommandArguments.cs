namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    ///     Read-only, invocation-scoped view over the parsed arguments of one
    ///     command invocation. Given to new-style command handlers and
    ///     completion providers so dispatch can avoid materializing an owned
    ///     array per invocation.
    /// </summary>
    /// <remarks>
    ///     The view is valid only during the callback that received it. The
    ///     backing buffer is owned by whoever dispatched the invocation
    ///     (the shell's depth-scoped parse scopes, or the caller's own list)
    ///     and is reused for later invocations, so a handler must not retain
    ///     the view or its enumerator. Handlers that need to keep arguments
    ///     must copy them out through <see cref="ToArray"/>.
    /// </remarks>
    public readonly struct BorrowedCommandArguments : IEnumerable<CommandArg>
    {
        private static readonly CommandArg[] EmptyArguments = Array.Empty<CommandArg>();

        private readonly IReadOnlyList<CommandArg> _arguments;

        internal BorrowedCommandArguments(IReadOnlyList<CommandArg> arguments)
        {
            _arguments = arguments;
        }

        /// <summary>Number of arguments in the current invocation.</summary>
        public int Count => _arguments?.Count ?? 0;

        /// <summary>True when the invocation carries no arguments.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>
        ///     The argument at <paramref name="index"/>, in input order.
        ///     Throws <see cref="ArgumentOutOfRangeException"/> when the
        ///     index is outside <c>[0, Count)</c>.
        /// </summary>
        public CommandArg this[int index]
        {
            get
            {
                if (_arguments == null || (uint)index >= (uint)_arguments.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _arguments[index];
            }
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_arguments);
        }

        /// <summary>
        ///     Copies the arguments into an owned array. New-style handlers
        ///     that must retain arguments beyond the invocation use this; the
        ///     copy is theirs to keep.
        /// </summary>
        public CommandArg[] ToArray()
        {
            if (_arguments == null)
            {
                return EmptyArguments;
            }

            if (_arguments is CommandArg[] array)
            {
                return (CommandArg[])array.Clone();
            }

            CommandArg[] copy = new CommandArg[_arguments.Count];
            for (int i = 0; i < copy.Length; ++i)
            {
                copy[i] = _arguments[i];
            }

            return copy;
        }

        IEnumerator<CommandArg> IEnumerable<CommandArg>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        ///     Allocation-free enumerator over the borrowed view. Valid only
        ///     as long as the view's backing buffer is valid. Interface access
        ///     boxes the struct; value-typed <c>foreach</c> does not.
        /// </summary>
        public struct Enumerator : IEnumerator<CommandArg>
        {
            private readonly IReadOnlyList<CommandArg> _arguments;
            private CommandArg _current;

            internal Enumerator(IReadOnlyList<CommandArg> arguments)
            {
                _arguments = arguments;
                _index = -1;
                _current = default;
            }

            public CommandArg Current => _current;

            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                int nextIndex = _index + 1;
                if (_arguments == null || _arguments.Count <= nextIndex)
                {
                    _current = default;
                    return false;
                }

                _index = nextIndex;
                _current = _arguments[_index];
                return true;
            }

            public void Reset()
            {
                _index = -1;
                _current = default;
            }

            public void Dispose() { }

            private int _index;
        }
    }
}
