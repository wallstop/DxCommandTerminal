namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Net;
    using System.Numerics;
    using System.Reflection;
    using UnityEngine;
    using Quaternion = UnityEngine.Quaternion;
    using Vector2 = UnityEngine.Vector2;
    using Vector3 = UnityEngine.Vector3;
    using Vector4 = UnityEngine.Vector4;

    public readonly struct CommandArg
    {
        // Public to allow custom-mutation, if desired
        public static readonly HashSet<char> Delimiters = new() { ',', ';', ':', '_', '/', '\\' };
        public static readonly List<char> Quotes = new() { '"', '\'' };
        public static readonly HashSet<string> IgnoredValuesForCleanedTypes = new() { "\r", "\n" };
        public static readonly HashSet<Type> DoNotCleanTypes = new()
        {
            typeof(string),
            typeof(char),
            typeof(DateTime),
            typeof(DateTimeOffset),
        };
        public static readonly HashSet<string> IgnoredValuesForComplexTypes = new()
        {
            "(",
            ")",
            "[",
            "]",
            "'",
            "`",
            "|",
            "{",
            "}",
            "<",
            ">",
        };
        private static readonly Lazy<MethodInfo> TryGetMethod = new(() =>
            typeof(CommandArg)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == nameof(TryGet))
                .FirstOrDefault(method => method.GetParameters().Length == 1)
        );

        private static readonly Dictionary<Type, object> RegisteredParsers = new();
        private static readonly Dictionary<
            Type,
            Dictionary<string, PropertyInfo>
        > StaticProperties = new();
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> ConstFields = new();
        private static readonly Dictionary<Type, object> EnumValues = new();

        public string CleanedContents
        {
            get
            {
                string cleanedString = contents;
                cleanedString = IgnoredValuesForCleanedTypes.Aggregate(
                    cleanedString,
                    (current, ignoredValue) =>
                        current.Replace(
                            ignoredValue,
                            string.Empty,
                            StringComparison.OrdinalIgnoreCase
                        )
                );
                return cleanedString;
            }
        }

        public readonly string contents;
        public readonly char? startQuote;
        public readonly char? endQuote;

        public CommandArg(string contents, char? startQuote = null, char? endQuote = null)
        {
            this.contents = contents ?? string.Empty;
            this.startQuote = startQuote;
            this.endQuote = endQuote;
        }

        public static bool RegisterParser<T>(CommandArgParser<T> parser, bool force = false)
        {
            if (parser == null)
            {
                return false;
            }

            Type type = typeof(T);
            if (force)
            {
                RegisteredParsers[type] = parser;
                return true;
            }

            return RegisteredParsers.TryAdd(type, parser);
        }

        public static bool TryGetParser<T>(out CommandArgParser<T> parser)
        {
            if (RegisteredParsers.TryGetValue(typeof(T), out object untypedParser))
            {
                parser = (CommandArgParser<T>)untypedParser;
                return true;
            }

            parser = null;
            return false;
        }

        public static bool UnregisterParser<T>()
        {
            return UnregisterParser(typeof(T));
        }

        public static bool UnregisterParser(Type type)
        {
            return RegisteredParsers.Remove(type);
        }

        public static int UnregisterAllParsers()
        {
            int parserCount = RegisteredParsers.Count;
            RegisteredParsers.Clear();
            return parserCount;
        }

        private static Dictionary<string, PropertyInfo> LoadStaticPropertiesForType<T>()
        {
            Type type = typeof(T);
            return type.GetProperties(BindingFlags.Static | BindingFlags.Public)
                .Where(property => property.PropertyType == type)
                .ToDictionary(
                    property => property.Name,
                    property => property,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        private static Dictionary<string, FieldInfo> LoadStaticFieldsForType<T>()
        {
            Type type = typeof(T);
            return type.GetFields(BindingFlags.Static | BindingFlags.Public)
                .Where(field => field.FieldType == type)
                .ToDictionary(
                    field => field.Name,
                    field => field,
                    StringComparer.OrdinalIgnoreCase
                );
        }

        public bool TryGet(Type type, out object parsed)
        {
            // TODO: Convert into delegates and cache for performance
            MethodInfo genericMethod = TryGetMethod.Value;
            if (genericMethod == null)
            {
                parsed = default;
                return false;
            }

            MethodInfo constructed = genericMethod.MakeGenericMethod(type);
            object[] parameters = { null };
            bool success = (bool)constructed.Invoke(this, parameters);
            parsed = parameters[0];
            return success;
        }

        public bool TryGet<T>(out T parsed)
        {
            return TryGet(out parsed, parserOverride: null);
        }

        public bool TryGet<T>(out T parsed, CommandArgParser<T> parserOverride)
        {
            Type type = typeof(T);
            string stringValue = DoNotCleanTypes.Contains(type) ? contents : CleanedContents;

            if (parserOverride != null)
            {
                return parserOverride(stringValue, out parsed);
            }

            if (TryGetParser(out CommandArgParser<T> parser))
            {
                return parser(stringValue, out parsed);
            }

            if (type == typeof(string))
            {
                parsed = (T)(object)stringValue;
                return true;
            }
            if (TryGetTypeDefined(stringValue, out parsed))
            {
                return true;
            }

            // TODO: Slap into a dictionary of built-in type -> parser mapping
            if (type == typeof(bool))
            {
                return InnerParse<bool>(stringValue, CommandArgParsers.Bool, out parsed);
            }
            if (type == typeof(float))
            {
                return InnerParse<float>(stringValue, CommandArgParsers.Float, out parsed);
            }
            if (type == typeof(int))
            {
                return InnerParse<int>(stringValue, CommandArgParsers.Int, out parsed);
            }
            if (type == typeof(uint))
            {
                return InnerParse<uint>(stringValue, CommandArgParsers.Uint, out parsed);
            }
            if (type == typeof(long))
            {
                return InnerParse<long>(stringValue, CommandArgParsers.Long, out parsed);
            }
            if (type == typeof(ulong))
            {
                return InnerParse<ulong>(stringValue, CommandArgParsers.Ulong, out parsed);
            }
            if (type == typeof(double))
            {
                return InnerParse<double>(stringValue, CommandArgParsers.Double, out parsed);
            }
            if (type == typeof(short))
            {
                return InnerParse<short>(stringValue, CommandArgParsers.Short, out parsed);
            }
            if (type == typeof(ushort))
            {
                return InnerParse<ushort>(stringValue, CommandArgParsers.Ushort, out parsed);
            }
            if (type == typeof(byte))
            {
                return InnerParse<byte>(stringValue, CommandArgParsers.Byte, out parsed);
            }
            if (type == typeof(sbyte))
            {
                return InnerParse<sbyte>(stringValue, CommandArgParsers.Sbyte, out parsed);
            }
            if (type == typeof(Guid))
            {
                return InnerParse<System.Guid>(stringValue, CommandArgParsers.Guid, out parsed);
            }
            if (type == typeof(DateTime))
            {
                return InnerParse<System.DateTime>(
                    stringValue,
                    CommandArgParsers.DateTime,
                    out parsed
                );
            }
            if (type == typeof(DateTimeOffset))
            {
                return InnerParse<System.DateTimeOffset>(
                    stringValue,
                    CommandArgParsers.DateTimeOffset,
                    out parsed
                );
            }
            if (type == typeof(char))
            {
                return InnerParse<char>(stringValue, CommandArgParsers.Char, out parsed);
            }
            if (type == typeof(decimal))
            {
                return InnerParse<decimal>(stringValue, CommandArgParsers.Decimal, out parsed);
            }
            if (type == typeof(BigInteger))
            {
                return InnerParse<System.Numerics.BigInteger>(
                    stringValue,
                    CommandArgParsers.BigInteger,
                    out parsed
                );
            }
            if (type == typeof(TimeSpan))
            {
                return InnerParse<System.TimeSpan>(
                    stringValue,
                    CommandArgParsers.TimeSpan,
                    out parsed
                );
            }
            if (type == typeof(Version))
            {
                return InnerParse<System.Version>(
                    stringValue,
                    CommandArgParsers.Version,
                    out parsed
                );
            }
            if (type == typeof(IPAddress))
            {
                return InnerParse<System.Net.IPAddress>(
                    stringValue,
                    CommandArgParsers.IPAddress,
                    out parsed
                );
            }
            if (type.IsEnum)
            {
                if (Enum.IsDefined(type, stringValue))
                {
                    bool parseOk = Enum.TryParse(type, stringValue, out object parsedObject);
                    if (parseOk)
                    {
                        parsed = (T)parsedObject;
                        return true;
                    }
                }

                if (CommandArgParsers.Int(stringValue, out int enumIntValue))
                {
                    if (!EnumValues.TryGetValue(type, out object enumValues))
                    {
                        enumValues = Enum.GetValues(type).OfType<T>().ToArray();
                        EnumValues[type] = enumValues;
                    }

                    T[] values = (T[])enumValues;
                    if (0 <= enumIntValue && enumIntValue < values.Length)
                    {
                        parsed = values[enumIntValue];
                        return true;
                    }
                }
            }
            if (type == typeof(Vector2))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when CommandArgParsers.Float(split[0], out float x)
                            && CommandArgParsers.Float(split[1], out float y):
                        parsed = (T)(object)new Vector2(x, y);
                        return true;
                    case 3
                        when CommandArgParsers.Float(split[0], out float x)
                            && CommandArgParsers.Float(split[1], out float y)
                            && CommandArgParsers.Float(split[2], out float z):
                        parsed = (T)(object)(Vector2)new Vector3(x, y, z);
                        return true;
                }
            }
            else if (type == typeof(Vector3))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when CommandArgParsers.Float(split[0], out float x)
                            && CommandArgParsers.Float(split[1], out float y):
                        parsed = (T)(object)new Vector3(x, y);
                        return true;
                    case 3
                        when CommandArgParsers.Float(split[0], out float x)
                            && CommandArgParsers.Float(split[1], out float y)
                            && CommandArgParsers.Float(split[2], out float z):
                        parsed = (T)(object)new Vector3(x, y, z);
                        return true;
                }
            }
            else if (type == typeof(Vector4))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when CommandArgParsers.Float(split[0], out float x)
                            && CommandArgParsers.Float(split[1], out float y):
                        parsed = (T)(object)new Vector4(x, y);
                        return true;
                    case 3
                        when CommandArgParsers.Float(split[0], out float x)
                            && CommandArgParsers.Float(split[1], out float y)
                            && CommandArgParsers.Float(split[2], out float z):
                        parsed = (T)(object)new Vector4(x, y, z);
                        return true;
                    case 4
                        when CommandArgParsers.Float(split[0], out float x)
                            && CommandArgParsers.Float(split[1], out float y)
                            && CommandArgParsers.Float(split[2], out float z)
                            && CommandArgParsers.Float(split[3], out float w):
                        parsed = (T)(object)new Vector4(x, y, z, w);
                        return true;
                }
            }
            else if (type == typeof(Vector2Int))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when CommandArgParsers.Int(split[0], out int x)
                            && CommandArgParsers.Int(split[1], out int y):
                        parsed = (T)(object)new Vector2Int(x, y);
                        return true;
                    case 3
                        when CommandArgParsers.Int(split[0], out int x)
                            && CommandArgParsers.Int(split[1], out int y)
                            && CommandArgParsers.Int(split[2], out int z):
                        parsed = (T)(object)(Vector2Int)new Vector3Int(x, y, z);
                        return true;
                }
            }
            else if (type == typeof(Vector3Int))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when CommandArgParsers.Int(split[0], out int x)
                            && CommandArgParsers.Int(split[1], out int y):
                        parsed = (T)(object)new Vector3Int(x, y);
                        return true;
                    case 3
                        when CommandArgParsers.Int(split[0], out int x)
                            && CommandArgParsers.Int(split[1], out int y)
                            && CommandArgParsers.Int(split[2], out int z):
                        parsed = (T)(object)new Vector3Int(x, y, z);
                        return true;
                }
            }
            else if (type == typeof(Color))
            {
                string colorString = stringValue;
                if (colorString.StartsWith("RGBA", StringComparison.OrdinalIgnoreCase))
                {
                    colorString = colorString.Replace(
                        "RGBA",
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase
                    );
                }

                string[] split = StripAndSplit(colorString);
                switch (split.Length)
                {
                    case 3
                        when CommandArgParsers.Float(split[0], out float r)
                            && CommandArgParsers.Float(split[1], out float g)
                            && CommandArgParsers.Float(split[2], out float b):
                        parsed = (T)(object)new Color(r, g, b);
                        return true;
                    case 4
                        when CommandArgParsers.Float(split[0], out float r)
                            && CommandArgParsers.Float(split[1], out float g)
                            && CommandArgParsers.Float(split[2], out float b)
                            && CommandArgParsers.Float(split[3], out float a):
                        parsed = (T)(object)new Color(r, g, b, a);
                        return true;
                }
            }
            else if (type == typeof(Quaternion))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when CommandArgParsers.Float(split[0], out float x)
                            && CommandArgParsers.Float(split[1], out float y)
                            && CommandArgParsers.Float(split[2], out float z)
                            && CommandArgParsers.Float(split[3], out float w):
                        parsed = (T)(object)new Quaternion(x, y, z, w);
                        return true;
                }
            }
            else if (type == typeof(Rect))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when CommandArgParsers.Float(
                            split[0]
                                .Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                            out float x
                        )
                            && CommandArgParsers.Float(
                                split[1]
                                    .Replace(
                                        "y:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float y
                            )
                            && CommandArgParsers.Float(
                                split[2]
                                    .Replace(
                                        "width:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float width
                            )
                            && CommandArgParsers.Float(
                                split[3]
                                    .Replace(
                                        "height:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float height
                            ):
                        parsed = (T)(object)new Rect(x, y, width, height);
                        return true;
                }
            }
            else if (type == typeof(RectInt))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when CommandArgParsers.Int(
                            split[0]
                                .Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                            out int x
                        )
                            && CommandArgParsers.Int(
                                split[1]
                                    .Replace(
                                        "y:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int y
                            )
                            && CommandArgParsers.Int(
                                split[2]
                                    .Replace(
                                        "width:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int width
                            )
                            && CommandArgParsers.Int(
                                split[3]
                                    .Replace(
                                        "height:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int height
                            ):
                        parsed = (T)(object)new RectInt(x, y, width, height);
                        return true;
                }
            }

            parsed = default;
            return false;

            static bool InnerParse<TParsed>(
                string input,
                CommandArgParser<TParsed> typedParser,
                out T parsed
            )
            {
                bool parseOk = typedParser(input, out TParsed value);
                if (parseOk)
                {
                    parsed = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
                }
                else
                {
                    parsed = default;
                }

                return parseOk;
            }

            static string[] StripAndSplit(string input)
            {
                string strippedInput = IgnoredValuesForComplexTypes
                    .Where(ignored => !string.IsNullOrEmpty(ignored))
                    .Aggregate(
                        input,
                        (current, ignored) =>
                            current.Replace(
                                ignored,
                                string.Empty,
                                StringComparison.OrdinalIgnoreCase
                            )
                    );

                foreach (char delimiter in Delimiters)
                {
                    if (0 <= strippedInput.IndexOf(delimiter, StringComparison.Ordinal))
                    {
                        return strippedInput.Split(delimiter);
                    }
                }

                return new[] { strippedInput };
            }

            static bool TryGetTypeDefined(string input, out T value)
            {
                Type type = typeof(T);
                if (
                    !StaticProperties.TryGetValue(
                        type,
                        out Dictionary<string, PropertyInfo> properties
                    )
                )
                {
                    properties = LoadStaticPropertiesForType<T>();
                    StaticProperties[type] = properties;
                }

                if (properties.TryGetValue(input, out PropertyInfo property))
                {
                    object resolved = property.GetValue(null);
                    value = (T)resolved;
                    return true;
                }

                if (!ConstFields.TryGetValue(type, out Dictionary<string, FieldInfo> fields))
                {
                    fields = LoadStaticFieldsForType<T>();
                    ConstFields[type] = fields;
                }

                if (fields.TryGetValue(input, out FieldInfo field))
                {
                    object resolved = field.GetValue(null);
                    value = (T)resolved;
                    return true;
                }

                value = default;
                return false;
            }
        }

        public override string ToString()
        {
            return contents;
        }
    }
}
