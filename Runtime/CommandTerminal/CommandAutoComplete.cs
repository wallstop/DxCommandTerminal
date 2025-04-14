namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public sealed class CommandAutoComplete
    {
        private readonly HashSet<string> _knownWords = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _duplicateBuffer = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _buffer = new();

        private readonly CommandHistory _history;
        private readonly CommandShell _shell;

        public CommandAutoComplete(
            CommandHistory history,
            CommandShell shell,
            IEnumerable<string> commands = null
        )
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            _knownWords.UnionWith(commands ?? Enumerable.Empty<string>());
        }

        public void Register(string word)
        {
            _knownWords.Add(word);
        }

        public void Clear()
        {
            _knownWords.Clear();
        }

        public string[] Complete(string text)
        {
            _duplicateBuffer.Clear();
            _buffer.Clear();
            WalkHistory(text.Trim(), onlySuccess: true, onlyErrorFree: false);
            return 0 == _buffer.Count ? Array.Empty<string>() : _buffer.ToArray();
        }

        private void WalkHistory(string input, bool onlySuccess, bool onlyErrorFree)
        {
            foreach (
                string known in _shell
                    .Commands.Keys.Select(command => command.ToLowerInvariant())
                    .Concat(_knownWords)
                    .Concat(
                        _history.GetHistory(onlySuccess: onlySuccess, onlyErrorFree: onlyErrorFree)
                    )
                    .Where(known => known.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            )
            {
                if (_duplicateBuffer.Add(known))
                {
                    _buffer.Add(known);
                }
            }
        }
    }
}
