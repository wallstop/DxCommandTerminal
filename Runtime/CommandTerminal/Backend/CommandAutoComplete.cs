namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using Extensions;

    public sealed class CommandAutoComplete
    {
        private readonly SortedSet<string> _knownWords = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _duplicateBuffer = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _buffer = new();
        private readonly List<string> _historyBuffer = new();

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
            _knownWords.UnionWith(commands ?? Array.Empty<string>());
        }

        public string[] Complete(string text)
        {
            Complete(text: text, buffer: _buffer);
            string[] results = new string[_buffer.Count];
            _buffer.CopyTo(results);
            return results;
        }

        public List<string> Complete(string text, List<string> buffer)
        {
            WalkHistory(text, onlySuccess: true, onlyErrorFree: false, buffer: buffer);
            return buffer;
        }

        private void WalkHistory(
            string input,
            bool onlySuccess,
            bool onlyErrorFree,
            List<string> buffer
        )
        {
            if (input.NeedsTrim())
            {
                input = input.Trim();
            }
            _duplicateBuffer.Clear();
            buffer.Clear();

            foreach (string command in _shell.Commands.Keys)
            {
                TryAddCompletion(
                    command.NeedsLowerInvariantConversion() ? command.ToLowerInvariant() : command,
                    input,
                    buffer
                );
            }

            foreach (string known in _knownWords)
            {
                TryAddCompletion(known, input, buffer);
            }

            _history.CopyHistory(onlySuccess, onlyErrorFree, _historyBuffer);
            foreach (string entry in _historyBuffer)
            {
                TryAddCompletion(entry, input, buffer);
            }
        }

        private void TryAddCompletion(string candidate, string input, List<string> buffer)
        {
            if (!candidate.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_duplicateBuffer.Add(candidate))
            {
                buffer.Add(candidate);
            }
        }
    }
}
