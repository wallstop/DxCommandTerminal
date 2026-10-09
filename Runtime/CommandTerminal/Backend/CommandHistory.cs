namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
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
            One consistent snapshot of the visible window, filtered. The
            window is copied under one lock, so an Add, a Clear, or a Resize
            during the caller's iteration cannot repeat or skip entries the
            way a live enumeration can; the cost is one array per call,
            which a diagnostics surface pays gladly. The copy is sized from
            a Count read: history writes and resizes are main-thread only,
            so the count cannot move between the read and the copy.
         */
        public IEnumerable<string> GetHistory(bool onlySuccess, bool onlyErrorFree)
        {
            var window = new (string text, bool? success, bool? errorFree)[_history.Count];
            _history.CopyTo(window);
            for (int i = 0; i < window.Length; ++i)
            {
                if (onlySuccess && window[i].success != true)
                {
                    continue;
                }

                if (onlyErrorFree && window[i].errorFree != true)
                {
                    continue;
                }

                yield return window[i].text;
            }
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
            window is copied under one lock into a member array that only
            grows when the history does, so a steady-state sweep is one lock
            acquisition and zero allocation, and a Push mid-sweep cannot
            repeat or skip entries.
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
    }
}
