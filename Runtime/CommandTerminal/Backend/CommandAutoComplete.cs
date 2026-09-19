namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using Helper;

    public sealed class CommandAutoComplete
    {
        /*
            The caller-supplied known words in sorted order. Unity's Mono
            runtime allocates a fresh enumerator for every SortedSet pass,
            even when the set is empty, so sweeps iterate this list instead.
         */
        private readonly List<string> _knownWords = new();

        private readonly HashSet<string> _duplicateBuffer = new(StringComparer.OrdinalIgnoreCase);

        /*
            The shell's command names in the dictionary's sorted order, in
            their completion form (lowercased), rebuilt only when the shell
            reports a new command version. Unity's Mono runtime allocates a
            fresh enumerator for every SortedDictionary pass and a fresh
            lowercased string for every cased name conversion, so sweeping
            the live table on every keystroke would allocate per keystroke.
         */
        private readonly List<string> _commandNames = new();

        private readonly List<string> _buffer = new();
        private readonly List<string> _historyBuffer = new();

        private readonly CommandHistory _history;
        private readonly CommandShell _shell;

        private long _commandNamesVersion;
        private bool _commandNamesDirty = true;

        public CommandAutoComplete(
            CommandHistory history,
            CommandShell shell,
            IEnumerable<string> commands = null
        )
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));

            /*
                Bulk add, then one sort: insertion-time binary searches would
                make construction quadratic. The seen set keeps the first
                casing of case-variant duplicates, matching the ordered-set
                semantics this list replaces.
             */
            using CachedStringSets.StringSetScope seenScope = CachedStringSets.RentIgnoreCase();
            HashSet<string> seen = seenScope.Set;
            foreach (string known in commands ?? Array.Empty<string>())
            {
                if (known == null)
                {
                    throw new ArgumentNullException(nameof(commands));
                }

                if (seen.Add(known))
                {
                    _knownWords.Add(known);
                }
            }

            _knownWords.Sort(StringComparer.OrdinalIgnoreCase);
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

            _shell.EnsureAutoCommandsRegistered();
            if (_commandNamesDirty || _commandNamesVersion != _shell.CommandsVersion)
            {
                RebuildCommandNames();
                _commandNamesVersion = _shell.CommandsVersion;
                _commandNamesDirty = false;
            }

            foreach (string command in _commandNames)
            {
                TryAddCompletion(command, input, buffer);
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

        private void RebuildCommandNames()
        {
            _commandNames.Clear();
            foreach (KeyValuePair<string, CommandInfo> command in _shell.CommandsSorted)
            {
                string name = command.Key;
                _commandNames.Add(
                    name.NeedsLowerInvariantConversion() ? name.ToLowerInvariant() : name
                );
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
