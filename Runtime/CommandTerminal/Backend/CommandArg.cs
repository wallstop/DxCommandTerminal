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
        /// <summary>
        ///     Untyped parser adapter: parses into a boxed value for the
        ///     non-generic <see cref="TryGet(Type, out object)" /> path.
        /// </summary>
        internal delegate bool UntypedParser(string input, out object parsed);

        /// <summary>
        ///     Types the built-in parser table covers. Internal for test
        ///     coverage; callers cannot mutate the returned collection.
        /// </summary>
        internal static IReadOnlyCollection<Type> BuiltInParserTypes => BuiltInParsers.Keys;

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
        private static readonly Dictionary<Type, object> RegisteredParsers = new();

        /*
            Untyped adapters for the built-in parser table, so the non-generic
            TryGet(Type, out object) path needs no reflection at runtime
            (IL2CPP/WebGL safe). Registered parsers mirror into the same
            delegate shape at registration time.
         */
        private static readonly Dictionary<Type, UntypedParser> BuiltInUntypedParsers = new()
        {
            [typeof(bool)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Bool(input, out bool value);
                parsed = value;
                return ok;
            },
            [typeof(float)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Float(input, out float value);
                parsed = value;
                return ok;
            },
            [typeof(int)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Int(input, out int value);
                parsed = value;
                return ok;
            },
            [typeof(uint)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Uint(input, out uint value);
                parsed = value;
                return ok;
            },
            [typeof(long)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Long(input, out long value);
                parsed = value;
                return ok;
            },
            [typeof(ulong)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Ulong(input, out ulong value);
                parsed = value;
                return ok;
            },
            [typeof(double)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Double(input, out double value);
                parsed = value;
                return ok;
            },
            [typeof(short)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Short(input, out short value);
                parsed = value;
                return ok;
            },
            [typeof(ushort)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Ushort(input, out ushort value);
                parsed = value;
                return ok;
            },
            [typeof(byte)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Byte(input, out byte value);
                parsed = value;
                return ok;
            },
            [typeof(sbyte)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Sbyte(input, out sbyte value);
                parsed = value;
                return ok;
            },
            [typeof(Guid)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Guid(input, out Guid value);
                parsed = value;
                return ok;
            },
            [typeof(DateTime)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.DateTime(input, out DateTime value);
                parsed = value;
                return ok;
            },
            [typeof(DateTimeOffset)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.DateTimeOffset(input, out DateTimeOffset value);
                parsed = value;
                return ok;
            },
            [typeof(char)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Char(input, out char value);
                parsed = value;
                return ok;
            },
            [typeof(decimal)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Decimal(input, out decimal value);
                parsed = value;
                return ok;
            },
            [typeof(BigInteger)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.BigInteger(input, out BigInteger value);
                parsed = value;
                return ok;
            },
            [typeof(TimeSpan)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.TimeSpan(input, out TimeSpan value);
                parsed = value;
                return ok;
            },
            [typeof(Version)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Version(input, out Version value);
                parsed = value;
                return ok;
            },
            [typeof(IPAddress)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.IPAddress(input, out IPAddress value);
                parsed = value;
                return ok;
            },
            [typeof(Vector2)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Vector2(input, out Vector2 value);
                parsed = value;
                return ok;
            },
            [typeof(Vector3)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Vector3(input, out Vector3 value);
                parsed = value;
                return ok;
            },
            [typeof(Vector4)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Vector4(input, out Vector4 value);
                parsed = value;
                return ok;
            },
            [typeof(Vector2Int)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Vector2Int(input, out Vector2Int value);
                parsed = value;
                return ok;
            },
            [typeof(Vector3Int)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Vector3Int(input, out Vector3Int value);
                parsed = value;
                return ok;
            },
            [typeof(Color)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Color(input, out Color value);
                parsed = value;
                return ok;
            },
            [typeof(Quaternion)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Quaternion(input, out Quaternion value);
                parsed = value;
                return ok;
            },
            [typeof(Rect)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Rect(input, out Rect value);
                parsed = value;
                return ok;
            },
            [typeof(RectInt)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.RectInt(input, out RectInt value);
                parsed = value;
                return ok;
            },
            [typeof(Bounds)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Bounds(input, out Bounds value);
                parsed = value;
                return ok;
            },
            [typeof(BoundsInt)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.BoundsInt(input, out BoundsInt value);
                parsed = value;
                return ok;
            },
            [typeof(RectOffset)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.RectOffset(input, out RectOffset value);
                parsed = value;
                return ok;
            },
            [typeof(Plane)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Plane(input, out Plane value);
                parsed = value;
                return ok;
            },
            [typeof(Ray)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Ray(input, out Ray value);
                parsed = value;
                return ok;
            },
            [typeof(Complex)] = (string input, out object parsed) =>
            {
                bool ok = CommandArgParsers.Complex(input, out Complex value);
                parsed = value;
                return ok;
            },
        };

        private static readonly Dictionary<Type, UntypedParser> RegisteredUntypedParsers = new();
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
            UntypedParser untypedParser = (string input, out object parsed) =>
            {
                bool ok = parser(input, out T value);
                parsed = value;
                return ok;
            };
            if (force)
            {
                RegisteredParsers[type] = parser;
                RegisteredUntypedParsers[type] = untypedParser;
                return true;
            }

            if (!RegisteredParsers.TryAdd(type, parser))
            {
                return false;
            }

            RegisteredUntypedParsers[type] = untypedParser;
            return true;
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
            RegisteredUntypedParsers.Remove(type);
            return RegisteredParsers.Remove(type);
        }

        public static int UnregisterAllParsers()
        {
            int parserCount = RegisteredParsers.Count;
            RegisteredParsers.Clear();
            RegisteredUntypedParsers.Clear();
            return parserCount;
        }

        /*
            Build-time parseability probe for the typed command builder: a
            definition is only accepted when its argument types can parse at
            execution. Mirrors the ordinary TryGet paths (registered parsers,
            built-in parsers, strings, enums); types that rely solely on the
            named-constant fallback need an explicit parser registration.
         */
        internal static bool CanParse(Type type)
        {
            return type == typeof(string)
                || type.IsEnum
                || BuiltInParsers.ContainsKey(type)
                || RegisteredParsers.ContainsKey(type);
        }

        private static Dictionary<string, PropertyInfo> LoadStaticPropertiesForType(Type type)
        {
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

        private static Dictionary<string, FieldInfo> LoadStaticFieldsForType(Type type)
        {
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

        private static bool TryGetNamedConstantUntyped(Type type, string input, out object value)
        {
            if (
                !StaticProperties.TryGetValue(type, out Dictionary<string, PropertyInfo> properties)
            )
            {
                properties = LoadStaticPropertiesForType(type);
                StaticProperties[type] = properties;
            }

            if (properties.TryGetValue(input, out PropertyInfo property))
            {
                value = property.GetValue(null);
                return true;
            }

            if (!ConstFields.TryGetValue(type, out Dictionary<string, FieldInfo> fields))
            {
                fields = LoadStaticFieldsForType(type);
                ConstFields[type] = fields;
            }

            if (fields.TryGetValue(input, out FieldInfo field))
            {
                value = field.GetValue(null);
                return true;
            }

            value = default;
            return false;
        }

        private static bool TryGetNamedConstant<T>(string input, out T value)
        {
            if (TryGetNamedConstantUntyped(typeof(T), input, out object resolved))
            {
                value = (T)resolved;
                return true;
            }

            value = default;
            return false;
        }

        public bool TryGet(Type type, out object parsed)
        {
            if (type == null)
            {
                parsed = default;
                return false;
            }

            string stringValue = DoNotCleanTypes.Contains(type) ? contents : CleanedContents;

            if (RegisteredUntypedParsers.TryGetValue(type, out UntypedParser registered))
            {
                return registered(stringValue, out parsed);
            }

            if (type == typeof(string))
            {
                parsed = stringValue;
                return true;
            }

            if (TryGetNamedConstantUntyped(type, stringValue, out parsed))
            {
                return true;
            }

            if (BuiltInUntypedParsers.TryGetValue(type, out UntypedParser builtIn))
            {
                return builtIn(stringValue, out parsed);
            }

            if (type.IsEnum)
            {
                if (Enum.IsDefined(type, stringValue))
                {
                    if (Enum.TryParse(type, stringValue, out object parsedObject))
                    {
                        parsed = parsedObject;
                        return true;
                    }
                }

                if (CommandArgParsers.Int(stringValue, out int enumIntValue))
                {
                    if (!EnumValues.TryGetValue(type, out object enumValues))
                    {
                        enumValues = Enum.GetValues(type);
                        EnumValues[type] = enumValues;
                    }

                    Array values = (Array)enumValues;
                    if (0 <= enumIntValue && enumIntValue < values.Length)
                    {
                        parsed = values.GetValue(enumIntValue);
                        return true;
                    }
                }
            }

            parsed = default;
            return false;
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
                        enumValues = Enum.GetValues(type);
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

        public bool TryGetRaw<T>(out T parsed, CommandArgParser<T> parser)
        {
            if (parser == null)
            {
                parsed = default;
                return false;
            }

            return parser(contents ?? string.Empty, out parsed);
        }

        public override string ToString()
        {
            return contents;
        }
    }
}
