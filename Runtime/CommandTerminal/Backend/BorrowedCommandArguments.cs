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

        /// <summary>Number of arguments in the current invocation.</summary>
        public int Count
        {
            get
            {
                if (_array != null)
                {
                    return _array.Length;
                }

                if (_list != null)
                {
                    return _list.Count;
                }

                return _fallback?.Count ?? 0;
            }
        }

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
                if (_array != null)
                {
                    if ((uint)_array.Length <= (uint)index)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return _array[index];
                }

                if (_list != null)
                {
                    if ((uint)_list.Count <= (uint)index)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return _list[index];
                }

                if (_fallback != null)
                {
                    if ((uint)_fallback.Count <= (uint)index)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return _fallback[index];
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        // Specialized storage: Unity does not de-virtualize IReadOnlyList
        // indexers, so array and list keep direct element access. The
        // fallback covers exotic callers only.
        private readonly CommandArg[] _array;
        private readonly List<CommandArg> _list;
        private readonly IReadOnlyList<CommandArg> _fallback;

        internal BorrowedCommandArguments(CommandArg[] array)
        {
            _array = array;
            _list = null;
            _fallback = null;
        }

        internal BorrowedCommandArguments(List<CommandArg> list)
        {
            _array = null;
            _list = list;
            _fallback = null;
        }

        internal BorrowedCommandArguments(IReadOnlyList<CommandArg> arguments)
        {
            if (arguments is CommandArg[] array)
            {
                _array = array;
                _list = null;
                _fallback = null;
                return;
            }

            if (arguments is List<CommandArg> list)
            {
                _array = null;
                _list = list;
                _fallback = null;
                return;
            }

            _array = null;
            _list = null;
            _fallback = arguments;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_array, _list, _fallback);
        }

        /// <summary>
        ///     Copies the arguments into an owned array with bulk copies. A
        ///     new-style handler that must retain arguments beyond the
        ///     invocation uses this; the copy is theirs to keep.
        /// </summary>
        public CommandArg[] ToArray()
        {
            if (_array != null)
            {
                CommandArg[] arrayCopy = new CommandArg[_array.Length];
                Array.Copy(_array, arrayCopy, arrayCopy.Length);
                return arrayCopy;
            }

            if (_list != null)
            {
                CommandArg[] listCopy = new CommandArg[_list.Count];
                _list.CopyTo(listCopy, 0);
                return listCopy;
            }

            if (_fallback == null)
            {
                return EmptyArguments;
            }

            CommandArg[] copy = new CommandArg[_fallback.Count];
            for (int i = 0; i < copy.Length; ++i)
            {
                copy[i] = _fallback[i];
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
            public CommandArg Current => _current;

            object IEnumerator.Current => _current;

            private readonly CommandArg[] _array;
            private readonly List<CommandArg> _list;
            private readonly IReadOnlyList<CommandArg> _fallback;
            private int _index;
            private CommandArg _current;

            internal Enumerator(
                CommandArg[] array,
                List<CommandArg> list,
                IReadOnlyList<CommandArg> fallback
            )
            {
                _array = array;
                _list = list;
                _fallback = fallback;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                int nextIndex = _index + 1;
                if (_array != null)
                {
                    if (_array.Length <= nextIndex)
                    {
                        _current = default;
                        return false;
                    }
                }
                else if (_list != null)
                {
                    if (_list.Count <= nextIndex)
                    {
                        _current = default;
                        return false;
                    }
                }
                else if (_fallback != null)
                {
                    if (_fallback.Count <= nextIndex)
                    {
                        _current = default;
                        return false;
                    }
                }
                else
                {
                    _current = default;
                    return false;
                }

                _index = nextIndex;
                if (_array != null)
                {
                    _current = _array[_index];
                }
                else if (_list != null)
                {
                    _current = _list[_index];
                }
                else
                {
                    _current = _fallback[_index];
                }

                return true;
            }

            public void Reset()
            {
                _index = -1;
                _current = default;
            }

            public void Dispose() { }
        }
    }
}
