namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Extensions;

    public sealed class CommandAutoComplete
    {
        private readonly SortedSet<string> _knownWords = new(StringComparer.OrdinalIgnoreCase);
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

        public string[] Complete(string text)
        {
            return Complete(text: text, buffer: _buffer).ToArray();
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
            foreach (
                string known in _shell
                    .Commands.Keys.Select(command =>
                        command.NeedsLowerInvariantConversion()
                            ? command.ToLowerInvariant()
                            : command
                    )
                    .Concat(_knownWords)
                    .Concat(
                        _history.GetHistory(onlySuccess: onlySuccess, onlyErrorFree: onlyErrorFree)
                    )
            )
            {
                if (!known.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_duplicateBuffer.Add(known))
                {
                    buffer.Add(known);
                }
            }
        }
    }
}
