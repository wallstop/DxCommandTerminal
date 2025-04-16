namespace WallstopStudios.DxCommandTerminal.Persistence
{
    using System;

    [Serializable]
    public struct TerminalThemeConfiguration : IEquatable<TerminalThemeConfiguration>
    {
        public string terminalId;
        public string font;
        public string theme;

        public bool Equals(TerminalThemeConfiguration other)
        {
            return string.Equals(terminalId, other.terminalId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(font, other.font, StringComparison.OrdinalIgnoreCase)
                && string.Equals(theme, other.theme, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            if (obj is TerminalThemeConfiguration config)
            {
                return Equals(config);
            }

            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (
                    terminalId != null ? terminalId.ToLowerInvariant().GetHashCode() : 0
                );
                hashCode =
                    (hashCode * 397) ^ (font != null ? font.ToLowerInvariant().GetHashCode() : 0);
                hashCode =
                    (hashCode * 397) ^ (theme != null ? theme.ToLowerInvariant().GetHashCode() : 0);
                return hashCode;
            }
        }
    }
}
