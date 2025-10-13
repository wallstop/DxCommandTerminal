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
            // Treat this as requesting suggestions for the first argument.
            if (!trailingWhitespace && args.Count == 0)
            {
                partialArg = string.Empty;
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

                foreach (
                    string suggestion in cmdInfo.completer.Complete(ctx) ?? Array.Empty<string>()
                )
                {
                    if (string.IsNullOrWhiteSpace(suggestion))
                    {
                        continue;
                    }

                    string prefix = commandName;
                    if (0 < args.Count)
                    {
                        prefix += " " + string.Join(" ", args.Select(a => a.contents));
                    }

                    if (argIndex >= 0)
                    {
                        prefix += " ";
                    }

                    string insertion = suggestion;
                    bool needsQuoting =
                        !string.IsNullOrEmpty(insertion) && insertion.Any(char.IsWhiteSpace);
                    if (needsQuoting)
                    {
                        // Basic quoting to keep single argument with whitespace
                        // Escape embedded quotes minimally
                        insertion = "\"" + insertion.Replace("\"", "\\\"") + "\"";
                    }

                    string full = prefix + insertion;
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
            _duplicateBuffer.Clear();
            buffer.Clear();

            // Commands
            foreach (string command in _shell.Commands.Keys)
            {
                string known = command.NeedsLowerInvariantConversion()
                    ? command.ToLowerInvariant()
                    : command;
                if (!known.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (_duplicateBuffer.Add(known))
                {
                    buffer.Add(known);
                }
            }

            // Known words
            foreach (string known in _knownWords)
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

            // History
            foreach (
                string known in _history.GetHistory(
                    onlySuccess: onlySuccess,
                    onlyErrorFree: onlyErrorFree
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
