namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    public static class StaticMemberParser<T>
    {
        private static bool initialized;
        private static Dictionary<string, PropertyInfo> Properties;
        private static Dictionary<string, FieldInfo> Fields;

        private static void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            Type type = typeof(T);
            Properties = type.GetProperties(BindingFlags.Static | BindingFlags.Public)
                .Where(p => p.PropertyType == type)
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);

            Fields = type.GetFields(BindingFlags.Static | BindingFlags.Public)
                .Where(f => f.FieldType == type)
                .ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

            initialized = true;
        }

        public static bool TryParse(string input, out T value)
        {
            EnsureInitialized();

            if (Properties.TryGetValue(input, out PropertyInfo property))
            {
                object resolved = property.GetValue(null);
                value = (T)resolved;
                return true;
            }

            if (Fields.TryGetValue(input, out FieldInfo field))
            {
                object resolved = field.GetValue(null);
                value = (T)resolved;
                return true;
            }

            value = default;
            return false;
        }
    }
}
