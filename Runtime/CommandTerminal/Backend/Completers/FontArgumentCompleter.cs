namespace WallstopStudios.DxCommandTerminal.Backend.Completers
{
    using System;
    using System.Collections.Generic;
    using UI;

    public sealed class FontArgumentCompleter : IArgumentCompleter
    {
        public IEnumerable<string> Complete(CommandCompletionContext context)
        {
            // Only complete the first argument (font name)
            if (context.ArgIndex != 0)
            {
                return Array.Empty<string>();
            }

            TerminalUI terminal = TerminalUI.ActiveTerminal;
            if (terminal == null || terminal._fontPack == null)
            {
                return Array.Empty<string>();
            }

            HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
            List<string> namesList = new();
            List<string> result = new();

            // Collect unique names (case-insensitive)
            foreach (UnityEngine.Font font in terminal._fontPack._fonts)
            {
                if (font == null)
                {
                    continue;
                }

                string name = font.name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                if (set.Add(name))
                {
                    namesList.Add(name);
                }
            }

            // Sort deterministically
            namesList.Sort(StringComparer.OrdinalIgnoreCase);

            string partial = context.PartialArg ?? string.Empty;
            if (string.IsNullOrWhiteSpace(partial))
            {
                return namesList;
            }

            for (int i = 0; i < namesList.Count; ++i)
            {
                string n = namesList[i];
                if (n.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(n);
                }
            }

            return result;
        }
    }
}
