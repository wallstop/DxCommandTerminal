namespace WallstopStudios.DxCommandTerminal.Backend.Completers
{
    using System;
    using System.Collections.Generic;
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

            HashSet<string> set = new(StringComparer.OrdinalIgnoreCase);
            List<string> friendlyList = new();
            List<string> result = new();

            foreach (string raw in terminal._themePack._themeNames)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }
                string friendly = ThemeNameHelper.GetFriendlyThemeName(raw);
                if (string.IsNullOrWhiteSpace(friendly))
                {
                    continue;
                }
                if (set.Add(friendly))
                {
                    friendlyList.Add(friendly);
                }
            }

            friendlyList.Sort(StringComparer.OrdinalIgnoreCase);

            string partial = context.PartialArg ?? string.Empty;
            if (string.IsNullOrWhiteSpace(partial))
            {
                return friendlyList;
            }

            for (int i = 0; i < friendlyList.Count; ++i)
            {
                string n = friendlyList[i];
                if (n.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(n);
                }
            }

            return result;
        }
    }
}
