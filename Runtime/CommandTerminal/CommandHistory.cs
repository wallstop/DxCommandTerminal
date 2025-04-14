namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DataStructures;

    public sealed class CommandHistory
    {
        public int Capacity => _history.Capacity;

        private readonly CyclicBuffer<(string text, bool? success, bool? errorFree)> _history;

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
        }

        public bool Push(string commandString, bool? success, bool? errorFree)
        {
            if (string.IsNullOrWhiteSpace(commandString))
            {
                return false;
            }

            _history.Add((commandString, success, errorFree));
            _position = _history.Count;
            return true;
        }

        public string Next(bool skipSameCommands)
        {
            int initialPosition = _position;
            _position++;

            while (
                skipSameCommands
                && 0 <= initialPosition
                && initialPosition < _history.Count
                && 0 <= _position
                && _position < _history.Count
            )
            {
                if (
                    string.Equals(
                        _history[initialPosition].text,
                        _history[_position].text,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    _position++;
                }
                else
                {
                    break;
                }
            }

            if (0 <= _position && _position < _history.Count)
            {
                return _history[_position].text;
            }

            _position = _history.Count;
            return string.Empty;
        }

        public string Previous(bool skipSameCommands)
        {
            int initialPosition = _position;
            _position--;

            while (
                skipSameCommands
                && 0 <= initialPosition
                && initialPosition < _history.Count
                && 0 <= _position
                && _position < _history.Count
            )
            {
                if (
                    string.Equals(
                        _history[initialPosition].text,
                        _history[_position].text,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    _position--;
                }
                else
                {
                    break;
                }
            }

            if (0 <= _position && _position < _history.Count)
            {
                return _history[_position].text;
            }

            _position = -1;
            return string.Empty;
        }

        public int Clear()
        {
            int count = _history.Count;
            _history.Clear();
            _position = 0;
            return count;
        }
    }
}
