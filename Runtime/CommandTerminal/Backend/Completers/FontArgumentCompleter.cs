namespace WallstopStudios.DxCommandTerminal.Backend.Completers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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

            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null || terminal._fontPack == null)
            {
                return Array.Empty<string>();
            }

            IEnumerable<string> names = terminal
                ._fontPack._fonts.Where(f => f != null && !string.IsNullOrWhiteSpace(f.name))
                .Select(f => f.name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            string partial = context.PartialArg ?? string.Empty;
            if (string.IsNullOrWhiteSpace(partial))
            {
                return names;
            }

            return names.Where(n => n.StartsWith(partial, StringComparison.OrdinalIgnoreCase));
        }
    }
}
