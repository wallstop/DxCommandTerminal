namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public sealed class CommandHistory
    {
        private readonly List<(string text, bool? success, bool? errorFree)> _history = new();
        private int _position;

        public IEnumerable<string> GetHistory(bool onlySuccess, bool onlyErrorFree)
        {
            return _history
                .Where(value => !onlySuccess || value.success == true)
                .Where(value => !onlyErrorFree || value.errorFree == true)
                .Select(value => value.text);
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

        public string Next()
        {
            _position++;
            if (0 <= _position && _position < _history.Count)
            {
                return _history[_position].text;
            }

            _position = _history.Count;
            return string.Empty;
        }

        public string Previous()
        {
            _position--;

            if (0 <= _position && _position < _history.Count)
            {
                return _history[_position].text;
            }

            _position = -1;
            return string.Empty;
        }

        public int Remove(Predicate<(string text, bool? success, bool? errorFree)> acceptance)
        {
            if (acceptance == null)
            {
                throw new ArgumentNullException(nameof(acceptance));
            }

            return _history.RemoveAll(acceptance);
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
