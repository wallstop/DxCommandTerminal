namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public sealed class CommandAutocomplete
    {
        private readonly HashSet<string> _knownWords = new();
        private readonly List<string> _buffer = new();
        private readonly CommandHistory _history;
        private readonly CommandShell _shell;

        public CommandAutocomplete(
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

        public string[] Complete(string text, ref int formatWidth)
        {
            _buffer.Clear();
            formatWidth = WalkHistory(text.Trim(), formatWidth, false);
            return _buffer.ToArray();

            int WalkHistory(string input, int currentFormatWidth, bool onlyErrorFree)
            {
                foreach (
                    string known in _shell
                        .Commands.Keys.Select(command => command.ToLowerInvariant())
                        .Concat(_knownWords)
                        .Concat(
                            _history.GetHistory(onlySuccess: true, onlyErrorFree: onlyErrorFree)
                        )
                        .Where(known => known.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                )
                {
                    _buffer.Add(known);
                    currentFormatWidth = Math.Max(currentFormatWidth, known.Length);
                }

                return currentFormatWidth;
            }
        }

        private static string EatLastWord(string text)
        {
            text = text.Trim();
            int lastSpace = text.LastIndexOf(' ');
            string result = text.Substring(lastSpace + 1);
            return result;
        }
    }
}
