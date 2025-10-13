namespace WallstopStudios.DxCommandTerminal.Backend.Completers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Themes;
    using UI;

    public sealed class ThemeArgumentCompleter : IArgumentCompleter
    {
        public IEnumerable<string> Complete(CommandCompletionContext context)
        {
            // Only complete the first argument (theme)
            if (context.ArgIndex != 0)
            {
                return Array.Empty<string>();
            }

            TerminalUI terminal = TerminalUI.Instance;
            if (terminal == null || terminal._themePack == null)
            {
                return Array.Empty<string>();
            }

            IEnumerable<string> friendly = terminal
                ._themePack._themeNames.Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(ThemeNameHelper.GetFriendlyThemeName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            string partial = context.PartialArg ?? string.Empty;
            if (string.IsNullOrWhiteSpace(partial))
            {
                return friendly;
            }

            return friendly.Where(n => n.StartsWith(partial, StringComparison.OrdinalIgnoreCase));
        }
    }
}
