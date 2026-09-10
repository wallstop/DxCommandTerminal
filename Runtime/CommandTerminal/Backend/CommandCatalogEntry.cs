namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Reflection;

    /// <summary>
    ///     A single source-generated command registration. The DxCommandTerminal
    ///     source generator emits one <c>CommandCatalog</c> per assembly that declares
    ///     <see cref="Attributes.RegisterCommandAttribute"/> methods; each catalog
    ///     produces instances of this type so the shell can register commands without
    ///     walking every type and method in the domain.
    /// </summary>
    /// <remarks>
    ///     Instances are produced exclusively by generated code and are treated as
    ///     immutable after construction.
    /// </remarks>
    public sealed class CommandCatalogEntry
    {
        /// <summary>
        ///     The normalized command name, exactly as
        ///     <see cref="Attributes.RegisterCommandAttribute"/> normalization would
        ///     produce it for the handler method.
        /// </summary>
        public string Name { get; }

        /// <summary>The name of the handler method, used for diagnostics.</summary>
        public string MethodName { get; }

        public int MinArgCount { get; }
        public int MaxArgCount { get; }
        public string Help { get; }
        public string Hint { get; }
        public bool AddToHistory { get; }
        public bool EditorOnly { get; }
        public bool DevelopmentOnly { get; }

        /// <summary>
        ///     Whether this is an in-built default command. Mirrors the internal
        ///     <c>Default</c> flag of <see cref="Attributes.RegisterCommandAttribute"/>.
        /// </summary>
        public bool IsDefault { get; }

        /// <summary>
        ///     Binds the handler to a runnable delegate. Non-null only for commands
        ///     with a valid <c>(CommandArg[])</c> signature; accessible handlers are
        ///     bound by direct delegate creation, inaccessible ones by a cached,
        ///     exact-identity reflection binding. May throw in builds where the
        ///     handler was stripped; callers must contain failures.
        /// </summary>
        public Func<Action<CommandArg[]>> Binder { get; }

        /// <summary>
        ///     Materializes the handler <see cref="MethodInfo"/> for rejected-command
        ///     diagnostics. Non-null only for commands whose signature was rejected at
        ///     generation time; null for valid commands and for shapes whose metadata
        ///     cannot be addressed.
        /// </summary>
        public Func<MethodInfo> MethodAccessor { get; }

        /// <summary>Whether the handler has a valid <c>(CommandArg[])</c> signature.</summary>
        public bool IsValid => Binder != null;

        public CommandCatalogEntry(
            string name,
            string methodName,
            int minArgCount,
            int maxArgCount,
            string help,
            string hint,
            bool addToHistory,
            bool editorOnly,
            bool developmentOnly,
            bool isDefault,
            Func<Action<CommandArg[]>> binder,
            Func<MethodInfo> methodAccessor
        )
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(
                    "Command name must not be null or blank.",
                    nameof(name)
                );
            }

            if (string.IsNullOrWhiteSpace(methodName))
            {
                throw new ArgumentException(
                    "Method name must not be null or blank.",
                    nameof(methodName)
                );
            }

            if (binder == null && methodAccessor == null)
            {
                throw new ArgumentException(
                    "Rejected commands must provide a method accessor for diagnostics.",
                    nameof(methodAccessor)
                );
            }

            Name = name;
            MethodName = methodName;
            MinArgCount = minArgCount;
            MaxArgCount = maxArgCount;
            Help = help;
            Hint = hint;
            AddToHistory = addToHistory;
            EditorOnly = editorOnly;
            DevelopmentOnly = developmentOnly;
            IsDefault = isDefault;
            Binder = binder;
            MethodAccessor = methodAccessor;
        }
    }
}
