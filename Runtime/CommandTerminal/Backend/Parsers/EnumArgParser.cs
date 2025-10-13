namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public static class EnumArgParser
    {
        private static readonly Dictionary<Type, Array> CachedValues = new();
        private static readonly Dictionary<Type, Dictionary<string, object>> CachedNames = new();

        public static bool TryParse(Type enumType, string input, out object value)
        {
            if (!enumType.IsEnum)
            {
                value = null;
                return false;
            }

            // Fast name map (case-insensitive)
            if (!CachedNames.TryGetValue(enumType, out Dictionary<string, object> nameMap))
            {
                nameMap = Enum.GetNames(enumType)
                    .ToDictionary(
                        n => n,
                        n => (object)Enum.Parse(enumType, n),
                        StringComparer.OrdinalIgnoreCase
                    );
                CachedNames[enumType] = nameMap;
            }

            if (nameMap.TryGetValue(input, out object named))
            {
                value = named;
                return true;
            }

            // Ordinal path
            if (int.TryParse(input, out int ordinal))
            {
                if (!CachedValues.TryGetValue(enumType, out Array values))
                {
                    values = Enum.GetValues(enumType);
                    CachedValues[enumType] = values;
                }

                if (0 <= ordinal && ordinal < values.Length)
                {
                    value = values.GetValue(ordinal);
                    return true;
                }
            }

            value = null;
            return false;
        }
    }
}
