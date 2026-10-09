namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using DataStructures;

    public sealed class CommandHistory
    {
        public int Capacity => _history.Capacity;

        public int Count => _history.Count;

        private readonly CyclicBuffer<(string text, bool? success, bool? errorFree)> _history;

        /*
            Reused window for the completion hot path's CopyTo, grown only
            when the history grows. Main-thread only, like the history.
         */
        private (string text, bool? success, bool? errorFree)[] _window;

        /*
            One cached view per filter combination, indexed by the two
            filter bits. Created on first use, so steady-state GetHistory
            calls allocate nothing.
         */
        private readonly HistoryView[] _views = new HistoryView[4];

        /*
            Commands already shown during the current traversal direction. Next
            and Previous skip entries listed here, so neither adjacent runs nor
            non-adjacent repeats get re-displayed in one sweep. The set resets
            on Push, Clear, Resize, and whenever the traversal direction
            flips, replaying the passed entries on the way back.
         */
        private readonly HashSet<string> _seenInDirection = new(StringComparer.OrdinalIgnoreCase);

        private int _direction;

        private int _position;

        public CommandHistory(int capacity)
        {
            _history = new CyclicBuffer<(string text, bool? success, bool? errorFree)>(capacity);
        }

        /*
            A live, filtered view of the history: MoveNext reads one entry
            per step under the ring's lock, so no step can tear an entry or
            throw, and a Push, a Clear, or a Resize during the caller's
            iteration is visible from that step on - on a full ring, that
            push overwrites the oldest entry, so a walk in progress can
            skip past it. One view per filter combination is cached on the
            owner and the enumerator is a struct, so a steady-state
            foreach allocates nothing; a caller that needs the window
            frozen at one instant copies it instead - CopyHistory is that
            read. The concrete return is what keeps the foreach
            allocation-free: declared as IEnumerable<string>, the struct
            enumerator would box on every GetEnumerator.
         */
        public HistoryView GetHistory(bool onlySuccess, bool onlyErrorFree)
        {
            int index = (onlySuccess ? 1 : 0) | (onlyErrorFree ? 2 : 0);
            return _views[index] ??= new HistoryView(this, onlySuccess, onlyErrorFree);
        }

        public void Resize(int newCapacity)
        {
            _history.Resize(newCapacity);
            _seenInDirection.Clear();
            _direction = 0;
        }

        public bool Push(string commandString, bool? success, bool? errorFree)
        {
            if (string.IsNullOrWhiteSpace(commandString))
            {
                return false;
            }

            _history.Add((commandString, success, errorFree));
            _position = _history.Count;
            _seenInDirection.Clear();
            _direction = 0;
            return true;
        }

        public string Next(bool skipSameCommands)
        {
            ++_position;
            if (_direction != 1)
            {
                _seenInDirection.Clear();
            }
            _direction = 1;

            int count = _history.Count;
            while (
                skipSameCommands
                && 0 <= _position
                && _position < count
                && _seenInDirection.Contains(_history[_position].text)
            )
            {
                ++_position;
            }

            if (0 <= _position && _position < count)
            {
                string text = _history[_position].text;
                _seenInDirection.Add(text);
                return text;
            }

            _position = count;
            return string.Empty;
        }

        public string Previous(bool skipSameCommands)
        {
            --_position;
            if (_direction != -1)
            {
                _seenInDirection.Clear();
            }
            _direction = -1;

            int count = _history.Count;
            while (
                skipSameCommands
                && 0 <= _position
                && _position < count
                && _seenInDirection.Contains(_history[_position].text)
            )
            {
                --_position;
            }

            if (0 <= _position && _position < count)
            {
                string text = _history[_position].text;
                _seenInDirection.Add(text);
                return text;
            }

            _position = -1;
            return string.Empty;
        }

        public int Clear()
        {
            int count = _history.Count;
            _history.Clear();
            _position = 0;
            _seenInDirection.Clear();
            _direction = 0;
            return count;
        }

        /*
            Fills a caller-owned buffer without allocating: the completion
            hot path walks the history on every keystroke-driven query, so it
            must not pay for enumerators or iterator state machines. The
            window is copied under the ring's lock into a member array that
            only grows when the history does, so a steady-state sweep adds
            no allocation and two locked reads where the enumerator took
            one per entry, and a Push mid-sweep cannot repeat or skip
            entries.
         */
        internal void CopyHistory(bool onlySuccess, bool onlyErrorFree, List<string> results)
        {
            results.Clear();
            int count = _history.Count;
            if (_window == null || _window.Length < count)
            {
                _window = new (string text, bool? success, bool? errorFree)[count];
            }

            int copied = _history.CopyTo(_window);
            for (int i = 0; i < copied; ++i)
            {
                if (onlySuccess && _window[i].success != true)
                {
                    continue;
                }

                if (onlyErrorFree && _window[i].errorFree != true)
                {
                    continue;
                }

                results.Add(_window[i].text);
            }
        }

        /*
            The cached enumerable behind GetHistory. Public because it is
            GetHistory's return type; constructing one is internal, so the
            only instances are the owner's per-filter cache. Stateless
            beyond its filters: every GetEnumerator hands back a fresh
            struct enumerator, so concurrent or nested enumerations of one
            view never share a position.
         */
        public sealed class HistoryView : IEnumerable<string>
        {
            private readonly CommandHistory _history;
            private readonly bool _onlySuccess;
            private readonly bool _onlyErrorFree;

            internal HistoryView(CommandHistory history, bool onlySuccess, bool onlyErrorFree)
            {
                _history = history;
                _onlySuccess = onlySuccess;
                _onlyErrorFree = onlyErrorFree;
            }

            public HistoryEnumerator GetEnumerator()
            {
                return new HistoryEnumerator(_history._history, _onlySuccess, _onlyErrorFree);
            }

            IEnumerator<string> IEnumerable<string>.GetEnumerator()
            {
                return GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public struct HistoryEnumerator : IEnumerator<string>
        {
            public string Current => _current;

            object IEnumerator.Current => Current;

            private readonly CyclicBuffer<(string text, bool? success, bool? errorFree)> _history;
            private readonly bool _onlySuccess;
            private readonly bool _onlyErrorFree;

            private int _index;
            private string _current;

            internal HistoryEnumerator(
                CyclicBuffer<(string text, bool? success, bool? errorFree)> history,
                bool onlySuccess,
                bool onlyErrorFree
            )
            {
                _history = history;
                _onlySuccess = onlySuccess;
                _onlyErrorFree = onlyErrorFree;
                _index = -1;
                _current = null;
            }

            public bool MoveNext()
            {
                lock (_history.SyncRoot)
                {
                    while (++_index < _history.Count)
                    {
                        (string text, bool? success, bool? errorFree) entry = _history[_index];
                        if (_onlySuccess && entry.success != true)
                        {
                            continue;
                        }

                        if (_onlyErrorFree && entry.errorFree != true)
                        {
                            continue;
                        }

                        _current = entry.text;
                        return true;
                    }

                    _current = null;
                    return false;
                }
            }

            public void Reset()
            {
                _index = -1;
                _current = null;
            }

            public void Dispose() { }
        }
    }
}
