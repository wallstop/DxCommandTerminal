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
        ///     produce it for the handler method. May be blank when inference
        ///     stripped the method name to empty; the shell rejects it with the
        ///     standard "Invalid Command Name" diagnostic.
        /// </summary>
        public string Name { get; }

        /// <summary>The name of the handler method, used for diagnostics.</summary>
        public string MethodName { get; }

        /// <summary>Minimum number of arguments the command accepts.</summary>
        public int MinArgCount { get; }

        /// <summary>
        ///     Maximum number of arguments the command accepts, or a negative
        ///     value for unbounded. int, not int?, because generated catalogs
        ///     transport the attribute's value as written;
        ///     <see cref="CommandInfo"/> normalizes negatives to
        ///     <c>null</c> at registration.
        /// </summary>
        public int MaxArgCount { get; }

        /// <summary>Help text shown by the built-in <c>help</c> command.</summary>
        public string Help { get; }

        /// <summary>Usage hint appended to argument-count errors.</summary>
        public string Hint { get; }

        /// <summary>
        ///     Whether invocations are recorded in the command history. Mirrors
        ///     <see cref="Attributes.RegisterCommandAttribute.AddToHistory"/>.
        /// </summary>
        public bool AddToHistory { get; }

        /// <summary>
        ///     Whether the command is available only in the Unity Editor.
        ///     Mirrors <see cref="Attributes.RegisterCommandAttribute.EditorOnly"/>.
        /// </summary>
        public bool EditorOnly { get; }

        /// <summary>
        ///     Whether the command is available only in development builds and
        ///     the Editor. Mirrors
        ///     <see cref="Attributes.RegisterCommandAttribute.DevelopmentOnly"/>.
        /// </summary>
        public bool DevelopmentOnly { get; }

        /// <summary>
        ///     Whether this is an in-built default command. Mirrors the internal
        ///     <c>Default</c> flag of <see cref="Attributes.RegisterCommandAttribute"/>.
        /// </summary>
        public bool IsDefault { get; }

        /// <summary>
        ///     Environments the command may run in. Mirrors
        ///     <see cref="Attributes.RegisterCommandAttribute.Contexts"/>;
        ///     eligibility is enforced at dispatch.
        /// </summary>
        public CommandExecutionContexts Contexts { get; }

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
            Func<MethodInfo> methodAccessor,
            CommandExecutionContexts contexts = CommandExecutionContextSets.All
        )
        {
            /*
                A blank name is legal here: the legacy discovery path produces
                it for handlers whose inferred name strips to empty (for
                example a method named `Command`), and the shell rejects it at
                registration with the same "Invalid Command Name" diagnostic
                reflection discovery has always produced.
             */
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
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
            Contexts = contexts;
        }
    }
}
