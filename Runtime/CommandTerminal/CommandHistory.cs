namespace CommandTerminal
{
    using System.Collections.Generic;

    public sealed class CommandHistory
    {
        private readonly List<string> _history = new();
        private int _position;

        public void Push(string commandString)
        {
            if (string.IsNullOrWhiteSpace(commandString))
            {
                return;
            }

            _history.Add(commandString);
            _position = _history.Count;
        }

        public string Next()
        {
            _position++;

            if (_position < _history.Count)
            {
                return _history[_position];
            }

            _position = _history.Count;
            return string.Empty;
        }

        public string Previous()
        {
            if (_history.Count == 0)
            {
                return string.Empty;
            }

            _position--;

            if (_position < 0)
            {
                _position = 0;
            }

            return _history[_position];
        }

        public void Clear()
        {
            _history.Clear();
            _position = 0;
        }
    }
}
