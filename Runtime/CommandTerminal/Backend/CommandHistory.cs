namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
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
            foreach ((string text, bool? success, bool? errorFree) entry in _history)
            {
                if (onlySuccess && entry.success != true)
                {
                    continue;
                }
                if (onlyErrorFree && entry.errorFree != true)
                {
                    continue;
                }
                yield return entry.text;
            }
        }

        public void CopyHistoryTo(List<string> buffer, bool onlySuccess, bool onlyErrorFree)
        {
            if (buffer == null)
            {
                return;
            }
            buffer.Clear();
            int count = _history.Count;
            for (int i = 0; i < count; ++i)
            {
                (string text, bool? success, bool? errorFree) entry = _history[i];
                if (onlySuccess && entry.success != true)
                {
                    continue;
                }
                if (onlyErrorFree && entry.errorFree != true)
                {
                    continue;
                }
                buffer.Add(entry.text);
            }
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
            ++_position;

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
                    ++_position;
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
            --_position;

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
                    --_position;
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
