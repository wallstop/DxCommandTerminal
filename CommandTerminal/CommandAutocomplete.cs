namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;

    public sealed class CommandAutocomplete
    {
        private readonly List<string> _knownWords = new();
        private readonly List<string> _buffer = new();

        public void Register(string word)
        {
            _knownWords.Add(word);
        }

        public string[] Complete(ref string text, ref int formatWidth)
        {
            _buffer.Clear();
            string partialWord = EatLastWord(ref text);

            foreach (string known in _knownWords)
            {
                if (!known.StartsWith(partialWord, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _buffer.Add(known);

                if (known.Length > formatWidth)
                {
                    formatWidth = known.Length;
                }
            }

            string[] completions = _buffer.ToArray();
            text += PartialWord(completions);
            return completions;
        }

        private static string EatLastWord(ref string text)
        {
            int lastSpace = text.LastIndexOf(' ');
            string result = text.Substring(lastSpace + 1);

            text = text.Substring(0, lastSpace + 1); // Remaining (keep space)
            return result;
        }

        private static string PartialWord(string[] words)
        {
            if (words.Length == 0)
            {
                return string.Empty;
            }

            string firstMatch = words[0];
            int partialLength = firstMatch.Length;

            if (words.Length == 1)
            {
                return firstMatch;
            }

            foreach (string word in words)
            {
                if (partialLength > word.Length)
                {
                    partialLength = word.Length;
                }

                for (int i = 0; i < partialLength; i++)
                {
                    if (word[i] != firstMatch[i])
                    {
                        partialLength = i;
                    }
                }
            }
            return firstMatch.Substring(0, partialLength);
        }
    }
}
