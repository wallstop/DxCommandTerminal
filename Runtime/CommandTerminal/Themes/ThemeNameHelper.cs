namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System;
    using System.Collections.Generic;

    public static class ThemeNameHelper
    {
        public static IEnumerable<string> GetPossibleThemeNames(string theme)
        {
            theme = GetFriendlyThemeName(theme);
            if (string.IsNullOrWhiteSpace(theme))
            {
                yield break;
            }

            yield return theme + "-theme";
            yield return "theme-" + theme;
        }

        public static bool IsThemeName(string theme)
        {
            if (string.IsNullOrWhiteSpace(theme))
            {
                return false;
            }

            return theme.Contains("-theme", StringComparison.OrdinalIgnoreCase)
                || theme.Contains("theme-", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetFriendlyThemeName(string theme)
        {
            if (string.IsNullOrWhiteSpace(theme))
            {
                return theme;
            }
            return theme
                .Replace("theme-", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("-theme", string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
