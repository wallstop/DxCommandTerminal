namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Numerics;
    using System.Reflection;
    using UnityEngine;
    using Plane = UnityEngine.Plane;
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
        {
            foreach (
                MethodInfo method in typeof(CommandArg).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public
                )
            )
            {
                if (method.Name == nameof(TryGet) && method.GetParameters().Length == 1)
                {
                    return method;
                }
            }

            return null;
        });

        private static readonly Dictionary<Type, object> RegisteredParsers = new();
        private static readonly Dictionary<
            Type,
            Dictionary<string, PropertyInfo>
        > StaticProperties = new();
        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> ConstFields = new();
        private static readonly Dictionary<Type, object> EnumValues = new();

        private static readonly Dictionary<Type, Delegate> BuiltInParsers = new()
        {
            [typeof(bool)] = (CommandArgParser<bool>)CommandArgParsers.Bool,
            [typeof(float)] = (CommandArgParser<float>)CommandArgParsers.Float,
            [typeof(int)] = (CommandArgParser<int>)CommandArgParsers.Int,
            [typeof(uint)] = (CommandArgParser<uint>)CommandArgParsers.Uint,
            [typeof(long)] = (CommandArgParser<long>)CommandArgParsers.Long,
            [typeof(ulong)] = (CommandArgParser<ulong>)CommandArgParsers.Ulong,
            [typeof(double)] = (CommandArgParser<double>)CommandArgParsers.Double,
            [typeof(short)] = (CommandArgParser<short>)CommandArgParsers.Short,
            [typeof(ushort)] = (CommandArgParser<ushort>)CommandArgParsers.Ushort,
            [typeof(byte)] = (CommandArgParser<byte>)CommandArgParsers.Byte,
            [typeof(sbyte)] = (CommandArgParser<sbyte>)CommandArgParsers.Sbyte,
            [typeof(Guid)] = (CommandArgParser<Guid>)CommandArgParsers.Guid,
            [typeof(DateTime)] = (CommandArgParser<DateTime>)CommandArgParsers.DateTime,
            [typeof(DateTimeOffset)] =
                (CommandArgParser<DateTimeOffset>)CommandArgParsers.DateTimeOffset,
            [typeof(char)] = (CommandArgParser<char>)CommandArgParsers.Char,
            [typeof(decimal)] = (CommandArgParser<decimal>)CommandArgParsers.Decimal,
            [typeof(BigInteger)] = (CommandArgParser<BigInteger>)CommandArgParsers.BigInteger,
            [typeof(TimeSpan)] = (CommandArgParser<TimeSpan>)CommandArgParsers.TimeSpan,
            [typeof(Version)] = (CommandArgParser<Version>)CommandArgParsers.Version,
            [typeof(IPAddress)] = (CommandArgParser<IPAddress>)CommandArgParsers.IPAddress,
            [typeof(Vector2)] = (CommandArgParser<Vector2>)CommandArgParsers.Vector2,
            [typeof(Vector3)] = (CommandArgParser<Vector3>)CommandArgParsers.Vector3,
            [typeof(Vector4)] = (CommandArgParser<Vector4>)CommandArgParsers.Vector4,
            [typeof(Vector2Int)] = (CommandArgParser<Vector2Int>)CommandArgParsers.Vector2Int,
            [typeof(Vector3Int)] = (CommandArgParser<Vector3Int>)CommandArgParsers.Vector3Int,
            [typeof(Color)] = (CommandArgParser<Color>)CommandArgParsers.Color,
            [typeof(Quaternion)] = (CommandArgParser<Quaternion>)CommandArgParsers.Quaternion,
            [typeof(Rect)] = (CommandArgParser<Rect>)CommandArgParsers.Rect,
            [typeof(RectInt)] = (CommandArgParser<RectInt>)CommandArgParsers.RectInt,
            [typeof(Bounds)] = (CommandArgParser<Bounds>)CommandArgParsers.Bounds,
            [typeof(BoundsInt)] = (CommandArgParser<BoundsInt>)CommandArgParsers.BoundsInt,
            [typeof(RectOffset)] = (CommandArgParser<RectOffset>)CommandArgParsers.RectOffset,
            [typeof(Plane)] = (CommandArgParser<Plane>)CommandArgParsers.Plane,
            [typeof(Ray)] = (CommandArgParser<Ray>)CommandArgParsers.Ray,
            [typeof(Complex)] = (CommandArgParser<Complex>)CommandArgParsers.Complex,
        };

        public string CleanedContents
        {
            get
            {
                string cleanedString = contents;
                foreach (string ignoredValue in IgnoredValuesForCleanedTypes)
                {
                    cleanedString = cleanedString.Replace(
                        ignoredValue,
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase
                    );
                }

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
            Dictionary<string, PropertyInfo> properties = new(StringComparer.OrdinalIgnoreCase);
            foreach (
                PropertyInfo property in type.GetProperties(
                    BindingFlags.Static | BindingFlags.Public
                )
            )
            {
                if (property.PropertyType == type)
                {
                    properties.Add(property.Name, property);
                }
            }

            return properties;
        }

        private static Dictionary<string, FieldInfo> LoadStaticFieldsForType<T>()
        {
            Type type = typeof(T);
            Dictionary<string, FieldInfo> fields = new(StringComparer.OrdinalIgnoreCase);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                if (field.FieldType == type)
                {
                    fields.Add(field.Name, field);
                }
            }

            return fields;
        }

        private static bool TryGetNamedConstant<T>(string input, out T value)
        {
            Type type = typeof(T);
            if (
                !StaticProperties.TryGetValue(type, out Dictionary<string, PropertyInfo> properties)
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
            if (TryGetNamedConstant(stringValue, out parsed))
            {
                return true;
            }
            if (BuiltInParsers.TryGetValue(type, out Delegate builtInParser))
            {
                return ((CommandArgParser<T>)builtInParser)(stringValue, out parsed);
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
                        /*
                            Enum.GetValues returns an array whose runtime type is
                            exactly T[], so the cast is free; OfType/ToArray would
                            copy it for nothing.
                         */
                        enumValues = (T[])Enum.GetValues(type);
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

            parsed = default;
            return false;
        }

        public override string ToString()
        {
            return contents;
        }
    }
}
