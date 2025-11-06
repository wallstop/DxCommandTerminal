namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System;
    using System.Collections.Generic;

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
                nameMap = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                string[] names = Enum.GetNames(enumType);
                for (int i = 0; i < names.Length; ++i)
                {
                    string n = names[i];
                    nameMap[n] = Enum.Parse(enumType, n);
                }
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
