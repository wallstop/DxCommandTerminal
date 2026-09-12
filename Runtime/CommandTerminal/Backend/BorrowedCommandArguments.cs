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
                if (0 <= _length)
                {
                    return _length;
                }

                if (_array != null)
                {
                    return _array.Length - _offset;
                }

                if (_list != null)
                {
                    return _list.Count - _offset;
                }

                return (_fallback?.Count ?? 0) - _offset;
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
                    if ((uint)Count <= (uint)index)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return _array[_offset + index];
                }

                if (_list != null)
                {
                    if ((uint)Count <= (uint)index)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return _list[_offset + index];
                }

                if (_fallback != null)
                {
                    if ((uint)Count <= (uint)index)
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }

                    return _fallback[_offset + index];
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        /*
                    Specialized storage: Unity does not de-virtualize IReadOnlyList
                    indexers, so array and list keep direct element access. The
                    fallback covers exotic callers only. _offset/_length carve out a
                    subcommand's share of the backing buffer without copying:
                    _offset is the first visible index and _length >= 0 pins the
                    visible count (-1 means through the end of the backing storage).
                */
        private readonly CommandArg[] _array;
        private readonly List<CommandArg> _list;
        private readonly IReadOnlyList<CommandArg> _fallback;
        private readonly int _offset;
        private readonly int _length;

        internal BorrowedCommandArguments(CommandArg[] array)
            : this(array, 0) { }

        internal BorrowedCommandArguments(List<CommandArg> list)
            : this(list, 0) { }

        internal BorrowedCommandArguments(IReadOnlyList<CommandArg> arguments)
            : this(arguments, 0) { }

        private BorrowedCommandArguments(
            CommandArg[] array,
            List<CommandArg> list,
            IReadOnlyList<CommandArg> fallback,
            int offset,
            int length
        )
        {
            _array = array;
            _list = list;
            _fallback = fallback;
            _offset = offset;
            _length = length;
        }

        private BorrowedCommandArguments(CommandArg[] array, int offset)
            : this(array, null, null, offset, -1) { }

        private BorrowedCommandArguments(List<CommandArg> list, int offset)
            : this(null, list, null, offset, -1) { }

        private BorrowedCommandArguments(IReadOnlyList<CommandArg> arguments, int offset)
        {
            if (arguments is CommandArg[] array)
            {
                _array = array;
                _list = null;
                _fallback = null;
                _offset = offset;
                _length = -1;
                return;
            }

            if (arguments is List<CommandArg> list)
            {
                _array = null;
                _list = list;
                _fallback = null;
                _offset = offset;
                _length = -1;
                return;
            }

            _array = null;
            _list = null;
            _fallback = arguments;
            _offset = offset;
            _length = -1;
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(_array, _list, _fallback, _offset, Count);
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
                int count = Count;
                CommandArg[] arrayCopy = new CommandArg[count];
                Array.Copy(_array, _offset, arrayCopy, 0, count);
                return arrayCopy;
            }

            if (_list != null)
            {
                int count = Count;
                CommandArg[] listCopy = new CommandArg[count];
                _list.CopyTo(_offset, listCopy, 0, count);
                return listCopy;
            }

            if (_fallback == null)
            {
                return EmptyArguments;
            }

            int fallbackCount = Count;
            CommandArg[] copy = new CommandArg[fallbackCount];
            for (int i = 0; i < fallbackCount; ++i)
            {
                copy[i] = _fallback[_offset + i];
            }

            return copy;
        }

        /// <summary>
        ///     A zero-copy view over the arguments from
        ///     <paramref name="offset"/> to the end, for dispatching a
        ///     subcommand's share of one invocation. The view aliases the same
        ///     backing storage and is valid exactly as long as this view is.
        /// </summary>
        internal BorrowedCommandArguments Slice(int offset)
        {
            if (offset < 0 || Count < offset)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (offset == 0)
            {
                return this;
            }

            int absolute = _offset + offset;
            if (_array != null)
            {
                return new BorrowedCommandArguments(_array, absolute);
            }

            if (_list != null)
            {
                return new BorrowedCommandArguments(_list, absolute);
            }

            return new BorrowedCommandArguments(_fallback, absolute);
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
            private readonly int _offset;
            private readonly int _end;
            private int _index;
            private CommandArg _current;

            internal Enumerator(
                CommandArg[] array,
                List<CommandArg> list,
                IReadOnlyList<CommandArg> fallback,
                int offset,
                int count
            )
            {
                _array = array;
                _list = list;
                _fallback = fallback;
                _offset = offset;
                _end = offset + count;
                _index = offset - 1;
                _current = default;
            }

            public bool MoveNext()
            {
                int nextIndex = _index + 1;
                if (_end <= nextIndex)
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
                _index = _offset - 1;
                _current = default;
            }

            public void Dispose() { }
        }
    }
}
