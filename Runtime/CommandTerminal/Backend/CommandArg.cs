namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Numerics;
    using System.Reflection;
    using UnityEngine;
    using Quaternion = UnityEngine.Quaternion;
    using Vector2 = UnityEngine.Vector2;
    using Vector3 = UnityEngine.Vector3;
    using Vector4 = UnityEngine.Vector4;

    public delegate bool CommandArgParser<T>(string input, out T parsed);

    public readonly struct CommandArg
    {
        private static readonly Lazy<MethodInfo> TryGetMethod = new(
            () =>
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

        public readonly string contents;
        public readonly char? startQuote;
        public readonly char? endQuote;

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
                parsed = (T)Convert.ChangeType(stringValue, type);
                return true;
            }
            if (TryGetTypeDefined(stringValue, out parsed))
            {
                return true;
            }

            // TODO: Slap into a dictionary of built-in type -> parser mapping
            if (type == typeof(bool))
            {
                return InnerParse<bool>(stringValue, bool.TryParse, out parsed);
            }
            if (type == typeof(float))
            {
                return InnerParse<float>(stringValue, float.TryParse, out parsed);
            }
            if (type == typeof(int))
            {
                return InnerParse<int>(stringValue, int.TryParse, out parsed);
            }
            if (type == typeof(uint))
            {
                return InnerParse<uint>(stringValue, uint.TryParse, out parsed);
            }
            if (type == typeof(long))
            {
                return InnerParse<long>(stringValue, long.TryParse, out parsed);
            }
            if (type == typeof(ulong))
            {
                return InnerParse<ulong>(stringValue, ulong.TryParse, out parsed);
            }
            if (type == typeof(double))
            {
                return InnerParse<double>(stringValue, double.TryParse, out parsed);
            }
            if (type == typeof(short))
            {
                return InnerParse<short>(stringValue, short.TryParse, out parsed);
            }
            if (type == typeof(ushort))
            {
                return InnerParse<ushort>(stringValue, ushort.TryParse, out parsed);
            }
            if (type == typeof(byte))
            {
                return InnerParse<byte>(stringValue, byte.TryParse, out parsed);
            }
            if (type == typeof(sbyte))
            {
                return InnerParse<sbyte>(stringValue, sbyte.TryParse, out parsed);
            }
            if (type == typeof(Guid))
            {
                return InnerParse<Guid>(stringValue, Guid.TryParse, out parsed);
            }
            if (type == typeof(DateTime))
            {
                return InnerParse<DateTime>(stringValue, DateTime.TryParse, out parsed);
            }
            if (type == typeof(DateTimeOffset))
            {
                return InnerParse<DateTimeOffset>(stringValue, DateTimeOffset.TryParse, out parsed);
            }
            if (type == typeof(char))
            {
                return InnerParse<char>(stringValue, char.TryParse, out parsed);
            }
            if (type == typeof(decimal))
            {
                return InnerParse<decimal>(stringValue, decimal.TryParse, out parsed);
            }
            if (type == typeof(BigInteger))
            {
                return InnerParse<BigInteger>(stringValue, BigInteger.TryParse, out parsed);
            }
            if (type == typeof(TimeSpan))
            {
                return InnerParse<TimeSpan>(stringValue, TimeSpan.TryParse, out parsed);
            }
            if (type == typeof(Version))
            {
                return InnerParse<Version>(stringValue, Version.TryParse, out parsed);
            }
            if (type == typeof(IPAddress))
            {
                return InnerParse<IPAddress>(stringValue, IPAddress.TryParse, out parsed);
            }
            if (type.IsEnum)
            {
                if (Enum.IsDefined(type, stringValue))
                {
                    bool parseOk = Enum.TryParse(type, stringValue, out object parsedObject);
                    if (parseOk)
                    {
                        parsed = (T)Convert.ChangeType(parsedObject, type);
                        return true;
                    }
                }

                if (int.TryParse(stringValue, out int enumIntValue))
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
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y):
                        parsed = (T)Convert.ChangeType(new Vector2(x, y), type);
                        return true;
                    case 3
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z):
                        parsed = (T)Convert.ChangeType((Vector2)new Vector3(x, y, z), type);
                        return true;
                }
            }
            else if (type == typeof(Vector3))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y):
                        parsed = (T)Convert.ChangeType(new Vector3(x, y), type);
                        return true;
                    case 3
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z):
                        parsed = (T)Convert.ChangeType(new Vector3(x, y, z), type);
                        return true;
                }
            }
            else if (type == typeof(Vector4))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y):
                        parsed = (T)Convert.ChangeType(new Vector4(x, y), type);
                        return true;
                    case 3
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z):
                        parsed = (T)Convert.ChangeType(new Vector4(x, y, z), type);
                        return true;
                    case 4
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z)
                            && float.TryParse(split[3], out float w):
                        parsed = (T)Convert.ChangeType(new Vector4(x, y, z, w), type);
                        return true;
                }
            }
            else if (type == typeof(Vector2Int))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when int.TryParse(split[0], out int x) && int.TryParse(split[1], out int y):
                        parsed = (T)Convert.ChangeType(new Vector2Int(x, y), type);
                        return true;
                    case 3
                        when int.TryParse(split[0], out int x)
                            && int.TryParse(split[1], out int y)
                            && int.TryParse(split[2], out int z):
                        parsed = (T)Convert.ChangeType((Vector2Int)new Vector3Int(x, y, z), type);
                        return true;
                }
            }
            else if (type == typeof(Vector3Int))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 2
                        when int.TryParse(split[0], out int x) && int.TryParse(split[1], out int y):
                        parsed = (T)Convert.ChangeType(new Vector3Int(x, y), type);
                        return true;
                    case 3
                        when int.TryParse(split[0], out int x)
                            && int.TryParse(split[1], out int y)
                            && int.TryParse(split[2], out int z):
                        parsed = (T)Convert.ChangeType(new Vector3Int(x, y, z), type);
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
                        when float.TryParse(split[0], out float r)
                            && float.TryParse(split[1], out float g)
                            && float.TryParse(split[2], out float b):
                        parsed = (T)Convert.ChangeType(new Color(r, g, b), type);
                        return true;
                    case 4
                        when float.TryParse(split[0], out float r)
                            && float.TryParse(split[1], out float g)
                            && float.TryParse(split[2], out float b)
                            && float.TryParse(split[3], out float a):
                        parsed = (T)Convert.ChangeType(new Color(r, g, b, a), type);
                        return true;
                }
            }
            else if (type == typeof(Quaternion))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when float.TryParse(split[0], out float x)
                            && float.TryParse(split[1], out float y)
                            && float.TryParse(split[2], out float z)
                            && float.TryParse(split[3], out float w):
                        parsed = (T)Convert.ChangeType(new Quaternion(x, y, z, w), type);
                        return true;
                }
            }
            else if (type == typeof(Rect))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when float.TryParse(
                            split[0]
                                .Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                            out float x
                        )
                            && float.TryParse(
                                split[1]
                                    .Replace(
                                        "y:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float y
                            )
                            && float.TryParse(
                                split[2]
                                    .Replace(
                                        "width:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float width
                            )
                            && float.TryParse(
                                split[3]
                                    .Replace(
                                        "height:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out float height
                            ):
                        parsed = (T)Convert.ChangeType(new Rect(x, y, width, height), type);
                        return true;
                }
            }
            else if (type == typeof(RectInt))
            {
                string[] split = StripAndSplit(stringValue);
                switch (split.Length)
                {
                    case 4
                        when int.TryParse(
                            split[0]
                                .Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                            out int x
                        )
                            && int.TryParse(
                                split[1]
                                    .Replace(
                                        "y:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int y
                            )
                            && int.TryParse(
                                split[2]
                                    .Replace(
                                        "width:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int width
                            )
                            && int.TryParse(
                                split[3]
                                    .Replace(
                                        "height:",
                                        string.Empty,
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                out int height
                            ):
                        parsed = (T)Convert.ChangeType(new RectInt(x, y, width, height), type);
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
                    parsed = (T)Convert.ChangeType(value, typeof(T));
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
                    if (strippedInput.Contains(delimiter))
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
                    value = (T)Convert.ChangeType(resolved, type);
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
                    value = (T)Convert.ChangeType(resolved, type);
                    return true;
                }

                value = default;
                return false;
            }
        }

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

        public override string ToString()
        {
            return contents;
        }
    }
}
