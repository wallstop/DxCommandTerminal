namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Extensions;

    public sealed class CommandAutoComplete
    {
        private readonly SortedSet<string> _knownWords = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _duplicateBuffer = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _buffer = new();
        private readonly List<string> _historyScratch = new();
        private readonly StringBuilder _sb = new();

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
            if (commands != null)
            {
                _knownWords.UnionWith(commands);
            }
        }

        public string[] Complete(string text)
        {
            return Complete(text: text, buffer: _buffer).ToArray();
        }

        public List<string> Complete(string text, List<string> buffer)
        {
            int caret = text?.Length ?? 0;
            Complete(text, caret, buffer);
            return buffer;
        }

        public List<string> Complete(string text, int caretIndex, List<string> buffer)
        {
            string input = text ?? string.Empty;
            buffer.Clear();
            _duplicateBuffer.Clear();

            if (string.IsNullOrWhiteSpace(input))
            {
                WalkHistory(input, onlySuccess: true, onlyErrorFree: false, buffer: buffer);
                return buffer;
            }

            int safeCaret = Math.Max(0, Math.Min(caretIndex, input.Length));
            string uptoCaret =
                safeCaret <= 0
                    ? string.Empty
                    : (safeCaret < input.Length ? input.Substring(0, safeCaret) : input);

            // Parse command + args up to caret
            string working = uptoCaret;
            if (!CommandShell.TryEatArgument(ref working, out CommandArg cmdArg))
            {
                WalkHistory(input, onlySuccess: true, onlyErrorFree: false, buffer: buffer);
                return buffer;
            }

            string commandName = cmdArg.contents;
            if (!_shell.Commands.TryGetValue(commandName, out CommandInfo cmdInfo))
            {
                // Fall back to default behavior if not a known command yet
                WalkHistory(input, onlySuccess: true, onlyErrorFree: false, buffer: buffer);
                return buffer;
            }

            // Collect args typed before cursor
            List<CommandArg> args = new();
            string lastToken = string.Empty;
            bool trailingWhitespace =
                uptoCaret.Length > 0 && char.IsWhiteSpace(uptoCaret[uptoCaret.Length - 1]);
            while (CommandShell.TryEatArgument(ref working, out CommandArg arg))
            {
                lastToken = arg.contents;
                args.Add(arg);
            }

            string partialArg = trailingWhitespace ? string.Empty : lastToken;
            int argIndex = trailingWhitespace ? args.Count : (args.Count - 1);
            if (!trailingWhitespace && 0 <= argIndex)
            {
                // Exclude the partial token from finalized args
                args.RemoveAt(args.Count - 1);
            }

            // Special case: caret is immediately after the command name with no space.
            // Treat this as requesting suggestions for the first argument, but preserve any partial text.
            if (!trailingWhitespace && args.Count == 0)
            {
                argIndex = 0;
            }

            // If the command provides a completer, ask it first
            bool inArgContext = argIndex >= 0;
            if (cmdInfo.completer != null)
            {
                CommandCompletionContext ctx = new(
                    input,
                    commandName,
                    args,
                    partialArg,
                    argIndex,
                    _shell
                );

                IEnumerable<string> suggestions =
                    cmdInfo.completer.Complete(ctx) ?? Array.Empty<string>();

                _sb.Clear();
                _sb.Append(commandName);
                if (0 < args.Count)
                {
                    _sb.Append(' ');
                    for (int i = 0; i < args.Count; ++i)
                    {
                        if (i > 0)
                        {
                            _sb.Append(' ');
                        }
                        _sb.Append(args[i].contents);
                    }
                }
                if (argIndex >= 0)
                {
                    _sb.Append(' ');
                }
                string prefixBase = _sb.ToString();

                foreach (string suggestion in suggestions)
                {
                    if (string.IsNullOrWhiteSpace(suggestion))
                    {
                        continue;
                    }

                    string insertion = suggestion;
                    bool needsQuoting = false;
                    if (!string.IsNullOrEmpty(insertion))
                    {
                        for (int i = 0; i < insertion.Length; ++i)
                        {
                            if (char.IsWhiteSpace(insertion[i]))
                            {
                                needsQuoting = true;
                                break;
                            }
                        }
                    }
                    if (needsQuoting)
                    {
                        // Basic quoting to keep single argument with whitespace
                        // Escape embedded quotes minimally
                        insertion = "\"" + insertion.Replace("\"", "\\\"") + "\"";
                    }

                    _sb.Clear();
                    _sb.Append(prefixBase);
                    _sb.Append(insertion);
                    string full = _sb.ToString();
                    string key = full.NeedsLowerInvariantConversion()
                        ? full.ToLowerInvariant()
                        : full;
                    if (_duplicateBuffer.Add(key))
                    {
                        buffer.Add(full);
                    }
                }

                // If we got any results from the completer, return them.
                if (0 < buffer.Count)
                {
                    return buffer;
                }

                // If we are in argument context for a command that supports completion,
                // prefer context (even if empty) and do not fall back to history/known words.
                if (inArgContext)
                {
                    return buffer;
                }
            }

            // Fallback to built-in completion sources
            WalkHistory(input, onlySuccess: true, onlyErrorFree: false, buffer: buffer);
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
            string normalizedInput = CommandShell.NormalizeCommandKey(input);
            bool useNormalizedMatch = !string.IsNullOrEmpty(normalizedInput);
            _duplicateBuffer.Clear();
            buffer.Clear();

            // Commands
            _historyScratch.Clear();
            _shell.CopyCommandNamesTo(_historyScratch);
            for (int ci = 0; ci < _historyScratch.Count; ++ci)
            {
                string command = _historyScratch[ci];
                string normalizedCommand = CommandShell.NormalizeCommandKey(command);
                bool matches = useNormalizedMatch
                    ? normalizedCommand.StartsWith(normalizedInput, StringComparison.Ordinal)
                    : command.StartsWith(input, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    continue;
                }
                string duplicateKey = !string.IsNullOrEmpty(normalizedCommand)
                    ? normalizedCommand
                    : command.NeedsLowerInvariantConversion()
                        ? command.ToLowerInvariant()
                        : command;
                string display = command.NeedsLowerInvariantConversion()
                    ? command.ToLowerInvariant()
                    : command;
                if (_duplicateBuffer.Add(duplicateKey))
                {
                    buffer.Add(display);
                }
            }

            // Known words
            foreach (string known in _knownWords)
            {
                string normalizedKnown = CommandShell.NormalizeCommandKey(known);
                bool matches = useNormalizedMatch
                    ? normalizedKnown.StartsWith(normalizedInput, StringComparison.Ordinal)
                    : known.StartsWith(input, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    continue;
                }
                string duplicateKey = !string.IsNullOrEmpty(normalizedKnown)
                    ? normalizedKnown
                    : known;
                if (_duplicateBuffer.Add(duplicateKey))
                {
                    buffer.Add(known);
                }
            }

            // History
            _history.CopyHistoryTo(_historyScratch, onlySuccess, onlyErrorFree);
            for (int hi = 0; hi < _historyScratch.Count; ++hi)
            {
                string known = _historyScratch[hi];
                string normalizedKnown = CommandShell.NormalizeCommandKey(known);
                bool matches = useNormalizedMatch
                    ? normalizedKnown.StartsWith(normalizedInput, StringComparison.Ordinal)
                    : known.StartsWith(input, StringComparison.OrdinalIgnoreCase);
                if (!matches)
                {
                    continue;
                }
                string duplicateKey = !string.IsNullOrEmpty(normalizedKnown)
                    ? normalizedKnown
                    : known;
                if (_duplicateBuffer.Add(duplicateKey))
                {
                    buffer.Add(known);
                }
            }
        }
    }
}
