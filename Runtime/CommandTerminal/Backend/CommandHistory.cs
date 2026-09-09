namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DataStructures;

    public sealed class CommandHistory
    {
        public int Capacity => _history.Capacity;

        public int Count => _history.Count;

        private readonly CyclicBuffer<(string text, bool? success, bool? errorFree)> _history;

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

        public IEnumerable<string> GetHistory(bool onlySuccess, bool onlyErrorFree)
        {
            return _history
                .Where(value => !onlySuccess || value.success == true)
                .Where(value => !onlyErrorFree || value.errorFree == true)
                .Select(value => value.text);
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

            while (
                skipSameCommands
                && 0 <= _position
                && _position < _history.Count
                && _seenInDirection.Contains(_history[_position].text)
            )
            {
                ++_position;
            }

            if (0 <= _position && _position < _history.Count)
            {
                string text = _history[_position].text;
                _seenInDirection.Add(text);
                return text;
            }

            _position = _history.Count;
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

            while (
                skipSameCommands
                && 0 <= _position
                && _position < _history.Count
                && _seenInDirection.Contains(_history[_position].text)
            )
            {
                --_position;
            }

            if (0 <= _position && _position < _history.Count)
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
    }
}
