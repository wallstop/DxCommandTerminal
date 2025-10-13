namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System;
    using System.Collections.Generic;
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
            Properties = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);
            PropertyInfo[] props = type.GetProperties(BindingFlags.Static | BindingFlags.Public);
            for (int i = 0; i < props.Length; ++i)
            {
                PropertyInfo p = props[i];
                if (p != null && p.PropertyType == type)
                {
                    Properties[p.Name] = p;
                }
            }

            Fields = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);
            FieldInfo[] fields = type.GetFields(BindingFlags.Static | BindingFlags.Public);
            for (int i = 0; i < fields.Length; ++i)
            {
                FieldInfo f = fields[i];
                if (f != null && f.FieldType == type)
                {
                    Fields[f.Name] = f;
                }
            }

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
