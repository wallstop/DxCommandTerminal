namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using DataStructures;

    public readonly struct CommandHistoryEntry
    {
        public CommandHistoryEntry(string text, bool? success, bool? errorFree)
        {
            Text = text ?? string.Empty;
            Success = success;
            ErrorFree = errorFree;
        }

        public string Text { get; }
        public bool? Success { get; }
        public bool? ErrorFree { get; }
    }

    public sealed class CommandHistory
    {
        public int Capacity => _history.Capacity;
        public int Count => _history.Count;
        public long Version => _version;

        private readonly CyclicBuffer<(string text, bool? success, bool? errorFree)> _history;

        private int _position;
        private long _version;

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

        public void CopyEntriesTo(
            List<CommandHistoryEntry> buffer,
            bool onlySuccess,
            bool onlyErrorFree
        )
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

                buffer.Add(new CommandHistoryEntry(entry.text, entry.success, entry.errorFree));
            }
        }

        public void CopyEntriesTo(List<CommandHistoryEntry> buffer)
        {
            CopyEntriesTo(buffer, onlySuccess: false, onlyErrorFree: false);
        }

        public void Resize(int newCapacity)
        {
            int previousCount = _history.Count;
            _history.Resize(newCapacity);
            if (_history.Count != previousCount)
            {
                _version++;
                _position = Math.Min(_position, _history.Count);
            }
        }

        public bool Push(string commandString, bool? success, bool? errorFree)
        {
            if (string.IsNullOrWhiteSpace(commandString))
            {
                return false;
            }

            if (_history.Capacity <= 0)
            {
                return false;
            }

            _history.Add((commandString, success, errorFree));
            _position = _history.Count;
            _version++;
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
            if (0 < count)
            {
                _version++;
            }
            return count;
        }
    }
}
