namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using WallstopStudios.DxCommandTerminal.Backend.Parsers;

    public delegate bool CommandArgParser<T>(string input, out T parsed);

    public readonly struct CommandArg
    {
        static CommandArg()
        {
            // Register built-in object parsers once for quick lookup
            RegisterDefaultObjectParsers();
        }

        private static readonly Lazy<MethodInfo> TryGetMethod = new(() =>
            typeof(CommandArg)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == nameof(TryGet))
                .FirstOrDefault(method => method.GetParameters().Length == 1)
        );
        private static readonly Dictionary<Type, object> RegisteredParsers = new();
        private static readonly Dictionary<Type, IArgParser> RegisteredObjectParsers = new();

        // Removed caches for static members and enum values; moved to dedicated parsers

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
            if (StaticMemberParser<T>.TryParse(stringValue, out parsed))
            {
                return true;
            }

            if (TryGetObjectParser(type, out IArgParser objectParser))
            {
                if (objectParser.TryParse(stringValue, out object objectValue))
                {
                    parsed = (T)objectValue;
                    return true;
                }
            }

            // Enums (hot path via cached values)
            if (type.IsEnum)
            {
                if (EnumArgParser.TryParse(type, stringValue, out object enumObject))
                {
                    parsed = (T)enumObject;
                    return true;
                }
            }

            parsed = default;
            return false;
            // static member resolution moved to StaticMemberParser<T>
        }

        // Consolidated parsing helpers moved to Backend.Parsers.CommandArgParserCommon

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

        // Object parser registration (IArgParser)
        public static bool RegisterObjectParser(IArgParser parser, bool force = false)
        {
            if (parser == null || parser.TargetType == null)
            {
                return false;
            }

            Type type = parser.TargetType;
            if (force)
            {
                RegisteredObjectParsers[type] = parser;
                return true;
            }

            return RegisteredObjectParsers.TryAdd(type, parser);
        }

        public static bool TryGetObjectParser(Type type, out IArgParser parser)
        {
            return RegisteredObjectParsers.TryGetValue(type, out parser);
        }

        public static bool UnregisterObjectParser(Type type)
        {
            return RegisteredObjectParsers.Remove(type);
        }

        public static int UnregisterAllObjectParsers()
        {
            int count = RegisteredObjectParsers.Count;
            RegisteredObjectParsers.Clear();
            return count;
        }

        public static IReadOnlyCollection<Type> GetRegisteredObjectParserTypes()
        {
            // Snapshot for thread-safety and immutability to callers
            return RegisteredObjectParsers.Keys.ToArray();
        }

        public static int DiscoverAndRegisterParsers(bool replaceExisting = false)
        {
            int added = 0;
            foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types.Where(t => t != null).ToArray();
                }

                foreach (Type t in types)
                {
                    if (t == null || t.IsAbstract || t.IsGenericTypeDefinition)
                    {
                        continue;
                    }
                    if (!typeof(IArgParser).IsAssignableFrom(t))
                    {
                        continue;
                    }

                    IArgParser instance = null;
                    // Prefer public static Instance singleton if available
                    var instProp = t.GetProperty(
                        "Instance",
                        BindingFlags.Public | BindingFlags.Static
                    );
                    if (
                        instProp != null
                        && typeof(IArgParser).IsAssignableFrom(instProp.PropertyType)
                    )
                    {
                        instance = (IArgParser)instProp.GetValue(null);
                    }
                    else
                    {
                        var instField = t.GetField(
                            "Instance",
                            BindingFlags.Public | BindingFlags.Static
                        );
                        if (
                            instField != null
                            && typeof(IArgParser).IsAssignableFrom(instField.FieldType)
                        )
                        {
                            instance = (IArgParser)instField.GetValue(null);
                        }
                    }

                    if (instance == null)
                    {
                        // Fall back to parameterless constructor
                        var ctor = t.GetConstructor(Type.EmptyTypes);
                        if (ctor != null)
                        {
                            instance = (IArgParser)Activator.CreateInstance(t);
                        }
                    }

                    if (instance == null || instance.TargetType == null)
                    {
                        continue;
                    }

                    if (RegisterObjectParser(instance, replaceExisting))
                    {
                        added++;
                    }
                }
            }
            return added;
        }

        private static void RegisterDefaultObjectParsers()
        {
            // Numerics
            RegisterObjectParser(BoolArgParser.Instance, true);
            RegisterObjectParser(FloatArgParser.Instance, true);
            RegisterObjectParser(IntArgParser.Instance, true);
            RegisterObjectParser(UIntArgParser.Instance, true);
            RegisterObjectParser(LongArgParser.Instance, true);
            RegisterObjectParser(ULongArgParser.Instance, true);
            RegisterObjectParser(DoubleArgParser.Instance, true);
            RegisterObjectParser(ShortArgParser.Instance, true);
            RegisterObjectParser(UShortArgParser.Instance, true);
            RegisterObjectParser(ByteArgParser.Instance, true);
            RegisterObjectParser(SByteArgParser.Instance, true);
            RegisterObjectParser(DecimalArgParser.Instance, true);
            RegisterObjectParser(BigIntegerArgParser.Instance, true);

            // Misc
            RegisterObjectParser(GuidArgParser.Instance, true);
            RegisterObjectParser(DateTimeArgParser.Instance, true);
            RegisterObjectParser(DateTimeOffsetArgParser.Instance, true);
            RegisterObjectParser(CharArgParser.Instance, true);
            RegisterObjectParser(TimeSpanArgParser.Instance, true);
            RegisterObjectParser(VersionArgParser.Instance, true);
            RegisterObjectParser(IPAddressArgParser.Instance, true);

            // Unity types
            RegisterObjectParser(Vector2ArgParser.Instance, true);
            RegisterObjectParser(Vector3ArgParser.Instance, true);
            RegisterObjectParser(Vector4ArgParser.Instance, true);
            RegisterObjectParser(Vector2IntArgParser.Instance, true);
            RegisterObjectParser(Vector3IntArgParser.Instance, true);
            RegisterObjectParser(ColorArgParser.Instance, true);
            RegisterObjectParser(QuaternionArgParser.Instance, true);
            RegisterObjectParser(RectArgParser.Instance, true);
            RegisterObjectParser(RectIntArgParser.Instance, true);
        }

        // Static member reflection helpers moved to Parsers.StaticMemberParser<T>

        public override string ToString()
        {
            return contents;
        }
    }
}
