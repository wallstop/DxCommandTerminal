namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using Attributes;
    using DataStructures;
    using Helper;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

    public sealed class CommandShell
    {
        /* Internal for the player compatibility bake, which probes candidate
           assemblies for the same generated catalog the discovery scan binds. */
        internal const string CatalogTypeName =
            "WallstopStudios.DxCommandTerminal.Generated.CommandCatalog";

        private const string CatalogCollectMethodName = "Collect";

        private const BindingFlags CatalogBindingFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const BindingFlags ReflectedMethodBindingFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        public static readonly Lazy<(
            MethodInfo method,
            RegisterCommandAttribute attribute
        )[]> RegisteredCommands = new(static () =>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Stopwatch stopwatch = Stopwatch.StartNew();
#endif
            List<(MethodInfo, RegisterCommandAttribute)> commands = new();
            Assembly ourAssembly = typeof(BuiltInCommands).Assembly;
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<Assembly> scanCandidates = CollectScanCandidates(loadedAssemblies, ourAssembly);

            foreach (Assembly assembly in scanCandidates)
            {
                CollectReflectedCommands(assembly, commands);
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                $"[DxCommandTerminal] Discovered {commands.Count} auto-registered commands in "
                    + $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms (scanned {scanCandidates.Count} of "
                    + $"{loadedAssemblies.Length} loaded assemblies)"
            );
#endif

            return commands.ToArray();
        });

        private static readonly ConditionalWeakTable<Assembly, DiscoveryCache> DiscoveryCaches =
            new ConditionalWeakTable<Assembly, DiscoveryCache>();

        /*
            Classification memo for the discovery scan. Assembly metadata
            (its name and its referenced-assembly list) is immutable for the
            lifetime of the Assembly instance, so the read is cached per
            assembly: a warm registration cycle over a large editor domain
            otherwise re-reads every assembly's metadata on every pass
            (measured ~10 ms per cycle over ~780 assemblies on Unity
            6000.4.6f1 - the dominant warm readiness cost at the 1,000-
            command scaling tier). Entries die with their assembly, so
            unloads cannot serve stale data.
         */
        private static readonly ConditionalWeakTable<
            Assembly,
            AssemblyClassification
        > AssemblyClassifications = new ConditionalWeakTable<Assembly, AssemblyClassification>();

        /*
            Ordered discovery providers consulted between the generated-catalog
            probe and the reflection compatibility walk while auto registration
            applies. Editor services register from Unity initialization hooks,
            which run before any shell exists in the domain, so registration
            precedes every session reset. Read on whatever thread reaches
            readiness (the readiness handoff is Interlocked-guarded, like the
            discovery caches); mutate only through RegisterDiscoveryProvider
            handles.
         */
        private static readonly List<ICommandDiscoveryProvider> DiscoveryProviders = new();

        /*
            Assemblies opted into discovery despite failing the assembly-
            reference filter: dynamic assemblies, precompiled DLLs without a
            metadata reference, or otherwise exceptional carriers of
            [RegisterCommand] methods. Never populated by the default path.
            Read on whatever thread reaches readiness (the readiness handoff
            is Interlocked-guarded, like the discovery caches); mutate only
            through IncludeDiscoveryAssembly handles.
         */
        private static readonly List<Assembly> IncludedScanAssemblies = new();

        /*
            Readiness boundary: the first observation of command state applies
            any deferred auto registration before returning, so a single-
            threaded caller never sees a half-initialized catalog. Concurrent
            callers are out of contract, like all shell command state.
         */
        public IReadOnlyDictionary<string, CommandInfo> Commands
        {
            get
            {
                EnsureAutoCommandsRegistered();
                return _commands;
            }
        }

        public IReadOnlyDictionary<string, CommandArg> Variables => _variables;

        /// <summary>
        ///     Auto commands registered by this shell. Empty until registration
        ///     is applied; a shell initialized with deferred registration stays
        ///     empty past enable, and the first command request, the first read
        ///     of <see cref="Commands"/>, or an explicit
        ///     <see cref="EnsureAutoCommandsRegistered"/> call fills it.
        /// </summary>
        public ReadOnlyHashSet<string> AutoRegisteredCommands { get; private set; } =
            ReadOnlyHashSet<string>.Empty;

        public ReadOnlyHashSet<string> IgnoredCommands { get; private set; } =
            ReadOnlyHashSet<string>.Empty;

        public bool IgnoringDefaultCommands { get; private set; }

        /// <summary>
        ///     True once auto command registration has been applied to this shell.
        ///     A shell initialized with deferred registration stays false until the
        ///     first command request or an explicit
        ///     <see cref="EnsureAutoCommandsRegistered"/> call applies it.
        /// </summary>
        public bool AutoCommandsRegistered { get; private set; }

        public bool HasErrors => 0 < _errorMessages.Count;

        /*
            Sorted iteration for in-assembly callers (auto-complete walks it
            when the version changes). Reading through the public Commands
            property's IReadOnlyDictionary allocates a Keys collection and a
            boxed enumerator per pass; the concrete type enumerates without
            the Keys copy. Invariant: every mutation of the returned table
            must bump _commandVersion, or name caches go stale.
         */
        internal SortedDictionary<string, CommandInfo> CommandsSorted => _commands;

        internal long CommandsVersion => _commandVersion;

        private readonly HashSet<string> _autoRegisteredCommands = new(
            StringComparer.OrdinalIgnoreCase
        );

        /*
            1 while a deferred registration is waiting to run. The readiness
            handoff uses Interlocked so two concurrent first uses cannot both
            run the registration; command state itself stays single-threaded
            like the rest of the shell.
         */
        private int _autoCommandsPending;

        private readonly StringBuilder _commandBuilder = new();

        private readonly SortedDictionary<string, CommandInfo> _commands = new(
            StringComparer.OrdinalIgnoreCase
        );

        /*
            Bumped on every command-table mutation. In-assembly consumers (the
            auto-complete's name cache) key off it, so per-keystroke sweeps
            never re-enumerate the sorted dictionary: Unity's Mono runtime
            allocates a fresh enumerator for every SortedDictionary pass.
            A long, like CommandLog.Version, so wraparound stays out of
            reach.
         */
        private long _commandVersion;

        private readonly Queue<string> _errorMessages = new();

        private readonly CommandHistory _history;
        private readonly HashSet<string> _ignoredCommands = new(StringComparer.OrdinalIgnoreCase);

        /*
            Rejected signatures store their formatted "Found: ..." text at
            rejection time, so the error pass never reflects over MethodInfo.
            Rejections only arise on the reflection compatibility path; the
            generator catalog never produces them.
         */
        private readonly SortedDictionary<string, string> _rejectedCommands = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly SortedDictionary<string, CommandArg> _variables = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly List<string> _variableClearBuffer = new();

        /*
            Depth-scoped parse buffers: one per active RunCommand/TryComplete
            level, so nested dispatches cannot corrupt outer arguments.
            Arrays materialized for legacy handlers are fresh per invocation,
            never pooled.
         */
        private readonly List<List<CommandArg>> _dispatchScopes = new();

        private readonly List<List<CommandToken>> _tokenScopes = new();

        private readonly HashSet<string> _completionDeduplication = new(StringComparer.Ordinal);

        private int _dispatchDepth;

        public CommandShell(CommandHistory history)
        {
            _history = history ?? throw new ArgumentNullException(nameof(history));
        }

        /// <summary>
        ///     Opts an assembly into auto command discovery even when the
        ///     default assembly-reference filter would skip it. The default
        ///     path scans only assemblies whose metadata references this
        ///     package and skips dynamic assemblies, so commands in a runtime-
        ///     emitted assembly or a precompiled DLL without that reference
        ///     would be silently dropped. Include the assembly explicitly to
        ///     scan it through the normal catalog, provider, and reflection
        ///     stages.
        /// </summary>
        /// <remarks>
        ///     Register before the first command request (readiness); a
        ///     registration made after registration was applied takes effect on
        ///     the next registration cycle, the same as discovery providers.
        ///     Duplicate registration of the same assembly is a no-op returning
        ///     a fresh handle. Disposing removes exactly that assembly; a
        ///     second dispose is a no-op.
        /// </remarks>
        /// <param name="assembly">The assembly to scan. Null is rejected.</param>
        /// <returns>A handle removing the assembly from discovery on dispose.</returns>
        public static IDisposable IncludeDiscoveryAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException(nameof(assembly));
            }

            bool alreadyIncluded = false;
            foreach (Assembly included in IncludedScanAssemblies)
            {
                if (ReferenceEquals(included, assembly))
                {
                    alreadyIncluded = true;
                    break;
                }
            }

            if (!alreadyIncluded)
            {
                IncludedScanAssemblies.Add(assembly);
            }

            return new DiscoveryAssemblyRegistration(assembly);
        }

        /*
           Internal for test coverage of the discovery filter (see
           WallstopStudios.DxCommandTerminal.Tests.Runtime).
        */
        public static bool TryEatArgument(ref string stringValue, out CommandArg arg)
        {
            stringValue = stringValue.TrimStart();
            if (stringValue.Length == 0)
            {
                arg = default;
                return false;
            }

            char firstChar = stringValue[0];
            if (CommandArg.Quotes.Contains(firstChar))
            {
                int closingQuoteIndex = -1;

                // Find the matching closing quote.
                for (int i = 1; i < stringValue.Length; ++i)
                {
                    if (stringValue[i] == firstChar)
                    {
                        closingQuoteIndex = i;
                        break;
                    }
                }

                if (closingQuoteIndex < 0)
                {
                    // No closing quote was found; consume the rest of the string (excluding the opening quote).
                    string input = stringValue.Substring(1);
                    arg = new CommandArg(input, firstChar);
                    stringValue = string.Empty;
                }
                else
                {
                    // Extract the argument inside the quotes.
                    string input = stringValue.Substring(1, closingQuoteIndex - 1);
                    arg = new CommandArg(input, firstChar, firstChar);
                    // Remove the parsed argument (including the quotes) from the input.
                    stringValue = stringValue.Substring(closingQuoteIndex + 1);
                }
            }
            else
            {
                // Unquoted argument: find the next space.
                int spaceIndex = stringValue.IndexOf(' ');
                if (spaceIndex < 0)
                {
                    arg = new CommandArg(stringValue);
                    stringValue = string.Empty;
                }
                else
                {
                    string input = stringValue.Substring(0, spaceIndex);
                    arg = new CommandArg(input);
                    stringValue = stringValue.Substring(spaceIndex + 1);
                }
            }

            return true;
        }

        /*
            Registers an ordered discovery provider consulted while auto
            registration applies (after generated catalogs, before the
            reflection walk). The returned handle removes exactly that
            provider; disposing twice is a no-op. Duplicate registration of
            the same provider instance is a no-op returning a fresh handle,
            so a hook rerun cannot double-serve assemblies.
         */
        internal static IDisposable RegisterDiscoveryProvider(ICommandDiscoveryProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            bool alreadyRegistered = false;
            foreach (ICommandDiscoveryProvider registered in DiscoveryProviders)
            {
                if (ReferenceEquals(registered, provider))
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            if (!alreadyRegistered)
            {
                DiscoveryProviders.Add(provider);
            }

            return new DiscoveryProviderRegistration(provider);
        }

        /*
            Collects auto-registered commands for one assembly: from its
            generated catalog when the generator produced one, then from the
            ordered discovery providers, otherwise from the reflection
            compatibility walk. Never walks types for an assembly that
            produced a non-empty catalog. Returns where the commands came
            from.

            Constraint: a non-empty catalog replaces reflection for its
            assembly. Commands emitted into that assembly by a *different*
            source generator (invisible to this generator's syntax receiver)
            would be dropped, so such assemblies must register manually or
            through their own catalog-compatible surface. The same constraint
            applies to a provider that claims an assembly.

            Internal for test coverage of the provider stage (see
            WallstopStudios.DxCommandTerminal.Tests.Runtime).
         */
        internal static AutoCommandSource CollectAutoCommands(
            Assembly assembly,
            List<AutoCommand> commands
        )
        {
            DiscoveryCache cache = GetOrCreateDiscoveryCache(assembly);
            if (
                TryGetCatalogCollector(
                    assembly,
                    cache,
                    out Action<List<CommandCatalogEntry>> collector
                )
            )
            {
                List<CommandCatalogEntry> entries = new();
                bool collected = false;
                try
                {
                    collector(entries);
                    collected = true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[DxCommandTerminal] Generated command catalog in "
                            + $"{assembly.GetName().Name} failed to collect: {e.Message}; "
                            + $"falling back to provider and reflection discovery"
                    );
                }

                if (collected && 0 < entries.Count)
                {
                    foreach (CommandCatalogEntry entry in entries)
                    {
                        commands.Add(AutoCommand.FromCatalog(entry));
                    }

                    return AutoCommandSource.Catalog;
                }

                /*
                   An empty or failed catalog is not proof that the assembly has
                   no commands; the provider and reflection stages below stay
                   compatible with it.
                */
            }

            /*
               The count is snapshotted so a provider that mutates the registry
               mid-collection (out of contract) can never overflow the loop;
               a provider that disposes registrations during its own call can
               shrink the registry instead, so each read rechecks the live
               count. A provider that appends and then throws has its partial
               appends rolled back, so the assembly falls through to the
               remaining stages from a clean state.
            */
            List<ICommandDiscoveryProvider> providers = DiscoveryProviders;
            int providerCount = providers.Count;
            for (int i = 0; i < providerCount; ++i)
            {
                if (providers.Count <= i)
                {
                    break;
                }

                ICommandDiscoveryProvider provider = providers[i];
                int appended = commands.Count;
                try
                {
                    if (provider.TryCollect(assembly, commands))
                    {
                        return AutoCommandSource.Provider;
                    }
                }
                catch (Exception e)
                {
                    /*
                       A provider that removed from the shared buffer before
                       throwing (out of contract) makes the rollback count
                       negative; clamping keeps containment from throwing.
                    */
                    int rolledBack = commands.Count - appended;
                    if (0 < rolledBack)
                    {
                        commands.RemoveRange(appended, rolledBack);
                    }

                    Debug.LogWarning(
                        $"[DxCommandTerminal] Command discovery provider "
                            + $"{provider.GetType().Name} failed for assembly "
                            + $"{assembly.GetName().Name}: {e.Message}"
                    );
                }
            }

            if (cache.ReflectedCommands == null)
            {
                List<(MethodInfo method, RegisterCommandAttribute attribute)> reflected = new();
                CollectReflectedCommands(assembly, reflected);
                List<AutoCommand> cached = new(reflected.Count);
                foreach ((MethodInfo method, RegisterCommandAttribute attribute) in reflected)
                {
                    cached.Add(AutoCommand.FromReflected(method, attribute));
                }

                cache.ReflectedCommands = cached;
            }

            commands.AddRange(cache.ReflectedCommands);
            return AutoCommandSource.Reflected;
        }

        internal static bool MayContainCommands(Assembly assembly, AssemblyName self)
        {
            if (assembly.IsDynamic)
            {
                return false;
            }

            /*
               A null referenced-assembly list means the metadata read failed
               for this assembly: scanning it is cheaper than silently
               dropping commands it might carry.
            */
            AssemblyName[] referencedAssemblies = GetClassification(assembly).ReferencedAssemblies;
            if (referencedAssemblies == null)
            {
                return true;
            }

            foreach (AssemblyName referencedAssembly in referencedAssemblies)
            {
                if (AssemblyName.ReferenceMatchesDefinition(referencedAssembly, self))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIncludedScanAssembly(Assembly assembly)
        {
            foreach (Assembly included in IncludedScanAssemblies)
            {
                if (ReferenceEquals(included, assembly))
                {
                    return true;
                }
            }

            return false;
        }

        /*
            Only assemblies that reference this one can contain
            RegisterCommandAttribute, so everything else is skipped without
            loading a single type. Assemblies included through
            IncludeDiscoveryAssembly are scanned regardless. Our assembly is
            processed last so user commands, if they conflict with in-built
            ones, are always registered first.
         */
        private static List<Assembly> CollectScanCandidates(
            Assembly[] loadedAssemblies,
            Assembly ourAssembly
        )
        {
            AssemblyName self = ourAssembly.GetName();
            List<Assembly> scanCandidates = new(loadedAssemblies.Length);
            foreach (Assembly assembly in loadedAssemblies)
            {
                try
                {
                    /*
                        Included assemblies skip the classification entirely
                        (they are scanned regardless of what their metadata
                        would say); the self-assembly guard keeps the old
                        name-match skip authoritative even for an included
                        runtime assembly, so it is appended last exactly once.
                    */
                    if (!ReferenceEquals(assembly, ourAssembly) && IsIncludedScanAssembly(assembly))
                    {
                        scanCandidates.Add(assembly);
                        continue;
                    }

                    AssemblyClassification classification = GetClassification(assembly);
                    if (classification.Name == null)
                    {
                        /*
                            An unqueryable assembly name is the exotic-assembly
                            case the outer catch used to cover: scan it rather
                            than silently dropping commands it might carry.
                        */
                        scanCandidates.Add(assembly);
                        continue;
                    }

                    if (AssemblyName.ReferenceMatchesDefinition(classification.Name, self))
                    {
                        continue;
                    }

                    if (MayContainCommands(assembly, self))
                    {
                        scanCandidates.Add(assembly);
                    }
                }
                catch (Exception)
                {
                    /*
                        Classification must never be able to fail discovery; if
                        an assembly cannot be classified, scan it.
                    */
                    scanCandidates.Add(assembly);
                }
            }

            scanCandidates.Add(ourAssembly);
            return scanCandidates;
        }

        private static AssemblyClassification GetClassification(Assembly assembly)
        {
            /*
                The factory is an explicitly static lambda: captureless, so the
                compiler caches it as a singleton delegate (no per-call
                allocation), and an accidental future capture becomes a
                compile error instead of a silent per-read closure.
             */
            return AssemblyClassifications.GetValue(
                assembly,
                static loadedAssembly =>
                {
                    AssemblyName[] referencedAssemblies;
                    try
                    {
                        referencedAssemblies = loadedAssembly.GetReferencedAssemblies();
                    }
                    catch (Exception)
                    {
                        /*
                            Metadata reads can fail for exotic assemblies; a null
                            list marks them for unconditional scanning.
                        */
                        referencedAssemblies = null;
                    }

                    AssemblyName name;
                    try
                    {
                        name = loadedAssembly.GetName();
                    }
                    catch (Exception)
                    {
                        name = null;
                    }

                    return new AssemblyClassification(name, referencedAssemblies);
                }
            );
        }

        private static void CollectReflectedCommands(
            Assembly assembly,
            List<(MethodInfo method, RegisterCommandAttribute attribute)> commands
        )
        {
            if (!TryGetScanTypes(assembly, out Type[] types))
            {
                return;
            }

            foreach (Type type in types)
            {
                if (type == null)
                {
                    continue;
                }

                try
                {
                    foreach (MethodInfo method in type.GetMethods(ReflectedMethodBindingFlags))
                    {
                        try
                        {
                            if (!method.IsDefined(typeof(RegisterCommandAttribute), false))
                            {
                                continue;
                            }

                            if (
                                Attribute.GetCustomAttribute(
                                    method,
                                    typeof(RegisterCommandAttribute)
                                )
                                is not RegisterCommandAttribute attribute
                            )
                            {
                                continue;
                            }

                            attribute.NormalizeName(method);
                            commands.Add((method, attribute));
                        }
                        catch (Exception e)
                        {
                            Debug.LogError(
                                $"Failed to resolve method {method.Name} of type {type.FullName} with exception {e}"
                            );
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"Failed to resolve methods for type {type.FullName} with exception {e}"
                    );
                }
            }
        }

        private static DiscoveryCache GetOrCreateDiscoveryCache(Assembly assembly)
        {
            if (DiscoveryCaches.TryGetValue(assembly, out DiscoveryCache cache))
            {
                return cache;
            }

            cache = new DiscoveryCache();
            try
            {
                DiscoveryCaches.Add(assembly, cache);
            }
            catch (ArgumentException)
            {
                /*
                   A concurrent initialization registered its cache first;
                   sharing it is equivalent to having won the race.
                */
                DiscoveryCaches.TryGetValue(assembly, out cache);
            }

            return cache ?? new DiscoveryCache();
        }

        private static bool TryGetCatalogCollector(
            Assembly assembly,
            DiscoveryCache cache,
            out Action<List<CommandCatalogEntry>> collector
        )
        {
            if (!cache.CollectorProbed)
            {
                cache.Collector = BindCatalogCollector(assembly);
                cache.CollectorProbed = true;
            }

            collector = cache.Collector;
            return collector != null;
        }

        private static Action<List<CommandCatalogEntry>> BindCatalogCollector(Assembly assembly)
        {
            Type catalogType;
            try
            {
                catalogType = assembly.GetType(CatalogTypeName, false);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Failed to probe for a generated command catalog in "
                        + $"{assembly.GetName().Name}: {e.Message}"
                );
                return null;
            }

            if (catalogType == null)
            {
                return null;
            }

            MethodInfo collectMethod;
            try
            {
                collectMethod = catalogType.GetMethod(
                    CatalogCollectMethodName,
                    CatalogBindingFlags,
                    null,
                    new Type[] { typeof(List<CommandCatalogEntry>) },
                    null
                );
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Found a generated command catalog in "
                        + $"{assembly.GetName().Name} but failed to bind it: {e.Message}"
                );
                return null;
            }

            if (collectMethod == null || collectMethod.ReturnType != typeof(void))
            {
                return null;
            }

            try
            {
                return (Action<List<CommandCatalogEntry>>)
                    Delegate.CreateDelegate(
                        typeof(Action<List<CommandCatalogEntry>>),
                        collectMethod
                    );
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Found a generated command catalog in "
                        + $"{assembly.GetName().Name} but failed to bind it: {e.Message}"
                );
                return null;
            }
        }

        private static bool TryGetScanTypes(Assembly assembly, out Type[] types)
        {
            try
            {
                types = assembly.GetTypes();
                return true;
            }
            catch (ReflectionTypeLoadException e)
            {
                /*
                   Scan the subset that loaded; one unloadable type must not
                   break discovery for the entire session.
                */
                types = e.Types;
                Debug.LogWarning(
                    $"Some types of assembly {assembly.FullName} failed to load; "
                        + $"command discovery continues with the {e.Types.Length} types that loaded"
                );
                return true;
            }
            catch (Exception e)
            {
                types = Array.Empty<Type>();
                Debug.LogError(
                    $"Failed to enumerate types for assembly {assembly.FullName} with exception {e}"
                );
                return false;
            }
        }

        public bool TryConsumeErrorMessage(out string errorMessage)
        {
            return _errorMessages.TryDequeue(out errorMessage);
        }

        public int ClearAllCommands()
        {
            return ClearCustomCommands();
        }

        /// <summary>
        ///     Clears every registered command from this shell, including the
        ///     auto-registered set, and cancels a pending deferred registration.
        ///     The returned count covers all commands that were registered. The
        ///     owning terminal re-applies its configuration on its next refresh,
        ///     which restores auto commands on the following readiness.
        /// </summary>
        public int ClearCustomCommands()
        {
            /*
                The auto-registered set tracks names in _commands, so clearing
                _commands without it would leave counts and AutoRegisteredCommands
                describing commands that no longer exist (issue #64).
            */
            int count = ClearAutoRegisteredCommands() + _commands.Count;
            _commands.Clear();
            ++_commandVersion;
            return count;
        }

        //public bool RemoveCommand

        /// <summary>
        ///     Removes every registered auto command from this shell and cancels
        ///     a pending deferred registration. The owning terminal re-applies
        ///     its configuration on its next refresh, which restores auto
        ///     commands on the following readiness.
        /// </summary>
        public int ClearAutoRegisteredCommands()
        {
            /*
               A pending registration would resurrect the commands this call
               removes, so cancellation is part of clearing.
            */
            Interlocked.Exchange(ref _autoCommandsPending, 0);
            AutoCommandsRegistered = false;
            int count = _autoRegisteredCommands.Count;
            foreach (string command in _autoRegisteredCommands)
            {
                _commands.Remove(command);
            }

            if (0 < count)
            {
                ++_commandVersion;
            }

            _autoRegisteredCommands.Clear();
            AutoRegisteredCommands = ReadOnlyHashSet<string>.Empty;
            return count;
        }

        /// <summary>
        ///     Applies the auto command configuration and registers discovered
        ///     commands. With <paramref name="deferRegistration"/>, the discovery
        ///     scan and delegate materialization run at the first command request
        ///     (see <see cref="EnsureAutoCommandsRegistered"/>) or the first read
        ///     of <see cref="Commands"/>, keeping terminal enabling cheap; the
        ///     ignored/default configuration itself is applied immediately.
        /// </summary>
        public void InitializeAutoRegisteredCommands(
            IEnumerable<string> ignoredCommands = null,
            bool ignoreDefaultCommands = false,
            bool deferRegistration = false
        )
        {
            IgnoringDefaultCommands = ignoreDefaultCommands;
            ClearAutoRegisteredCommands();
            _ignoredCommands.Clear();
            _ignoredCommands.UnionWith(ignoredCommands ?? Array.Empty<string>());
            foreach (string ignoredCommand in _ignoredCommands)
            {
                _commands.Remove(ignoredCommand);
            }

            if (0 < _ignoredCommands.Count)
            {
                ++_commandVersion;
            }

            IgnoredCommands = _ignoredCommands.ToReadOnlyHashSet(StringComparer.OrdinalIgnoreCase);
            _rejectedCommands.Clear();

            if (deferRegistration)
            {
                Interlocked.Exchange(ref _autoCommandsPending, 1);
                return;
            }

            RegisterAutoCommands();
        }

        /// <summary>
        ///     Applies deferred auto command registration exactly once. Subsequent
        ///     calls observe the completed registration and do no work.
        /// </summary>
        public void EnsureAutoCommandsRegistered()
        {
            if (Interlocked.Exchange(ref _autoCommandsPending, 0) == 0)
            {
                return;
            }

            RegisterAutoCommands();
        }

        /// <summary>
        ///     Parses an input line into a command and runs that command.
        /// </summary>
        public bool RunCommand(string line)
        {
            /*
               A first command request is an explicit readiness boundary: any
               deferred registration must be applied before this parse.
            */
            EnsureAutoCommandsRegistered();
            _dispatchDepth++;
            try
            {
                List<CommandToken> tokens = GetTokenScope(_dispatchDepth);
                List<CommandArg> arguments = GetDispatchScope(_dispatchDepth);
                CommandTokenizer.Tokenize(line, tokens);

                foreach (CommandToken token in tokens)
                {
                    CommandArg argument = token.ToArgument();
                    string argumentString = argument.contents;
                    if (argument.endQuote == null)
                    {
                        if (string.IsNullOrWhiteSpace(argumentString))
                        {
                            continue;
                        }

                        if (argumentString.StartsWith('$'))
                        {
                            string variableName = argumentString[1..];

                            if (_variables.TryGetValue(variableName, out CommandArg variable))
                            {
                                // Replace variable argument if it's defined
                                argument = variable;
                            }
                        }
                    }

                    arguments.Add(argument);
                }

                if (arguments.Count == 0)
                {
                    // No command identified, unconditional push
                    _history.Push(line, false, true);
                    return false;
                }

                string commandName = arguments[0].contents ?? string.Empty;
                // Remove command name from arguments
                arguments.RemoveAt(0);

                // History lines rebuild lazily, only when a push happens.

                return RunCommandCore(
                    CommandExecutionContext.Current,
                    commandName,
                    arguments,
                    historyLine: null
                );
            }
            finally
            {
                _dispatchDepth--;
            }
        }

        public bool RunCommand(string commandName, CommandArg[] arguments)
        {
            /*
               A first command request is an explicit readiness boundary: any
               deferred registration must be applied before this lookup.
            */
            EnsureAutoCommandsRegistered();
            string line = BuildHistoryLine(commandName, arguments);
            return RunCommandCore(CommandExecutionContext.Current, commandName, arguments, line);
        }

        /// <summary>
        ///     Context-aware dispatch over a caller-owned argument list. The
        ///     list is read during the invocation only; a context-aware
        ///     handler (<see cref="CommandInfo.handler"/>) receives a borrowed
        ///     read-only view over it, so the caller can reuse the list once
        ///     this method returns. A legacy handler receives a fresh owned
        ///     array per invocation; that array is never pooled. History
        ///     reconstruction happens only when the command's history policy
        ///     actually pushes, so commands registered with
        ///     <c>AddToHistory = false</c> dispatch without rebuilding the
        ///     line.
        /// </summary>
        public bool RunCommand(
            CommandExecutionContext context,
            string commandName,
            List<CommandArg> arguments
        )
        {
            /*
               A first command request is an explicit readiness boundary: any
               deferred registration must be applied before this lookup.
            */
            EnsureAutoCommandsRegistered();
            return RunCommandCore(context, commandName, arguments, historyLine: null);
        }

        /// <summary>
        ///     Requests argument completions for the command named in
        ///     <paramref name="input"/> from its registered completion
        ///     provider. Returns false when the command is unknown, has no
        ///     provider, or the caret is completing the command name itself;
        ///     callers fall back to their own history-based suggestions in
        ///     those cases. Returns true otherwise — including when the
        ///     provider produced zero candidates or threw — so a provider
        ///     attached to a command keeps full-line history suggestions from
        ///     overwriting the token being edited.
        /// </summary>
        /// <remarks>
        ///     Results are deduplicated by insertion text (ordinal, first
        ///     occurrence wins) and preserve provider order. The buffer is
        ///     caller-owned and reusable. Provider exceptions are contained:
        ///     partial results are discarded, an error is reported, and later
        ///     completion requests are unaffected. When true is returned,
        ///     <paramref name="completionContext"/> describes the request the
        ///     provider answered, including the replacement range an accepted
        ///     completion applies to.
        /// </remarks>
        public bool TryComplete(
            CommandExecutionContext context,
            string input,
            int caretIndex,
            List<CommandCompletion> results,
            out CommandCompletionContext completionContext
        )
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            // The buffer is caller-owned and reusable; clear stale results.

            results.Clear();

            EnsureAutoCommandsRegistered();

            _dispatchDepth++;
            try
            {
                List<CommandToken> tokens = GetTokenScope(_dispatchDepth);
                List<CommandArg> precedingArguments = GetDispatchScope(_dispatchDepth);
                CommandTokenizer.Tokenize(input, tokens);
                if (
                    !CommandTokenizer.TryFindActiveToken(
                        input,
                        caretIndex,
                        tokens,
                        out int activeTokenIndex,
                        out int replacementStart,
                        out int replacementLength,
                        out bool isNewArgument
                    )
                    || activeTokenIndex == 0
                )
                {
                    // Command-name completion is the caller's territory.
                    completionContext = default;
                    return false;
                }

                string commandName = tokens[0].Contents;
                if (
                    string.IsNullOrWhiteSpace(commandName)
                    || !_commands.TryGetValue(commandName, out CommandInfo command)
                    || command.completionProvider == null
                )
                {
                    completionContext = default;
                    return false;
                }

                for (int i = 1; i < activeTokenIndex; ++i)
                {
                    precedingArguments.Add(tokens[i].ToArgument());
                }

                string token;
                bool isQuoted;
                char? quoteCharacter;
                if (isNewArgument)
                {
                    token = string.Empty;
                    isQuoted = false;
                    quoteCharacter = null;
                }
                else
                {
                    CommandToken activeToken = tokens[activeTokenIndex];
                    int clampedCaret = Math.Clamp(caretIndex, 0, input.Length);
                    token = input.Substring(activeToken.Start, clampedCaret - activeToken.Start);
                    isQuoted = activeToken.StartQuote != null;
                    quoteCharacter = activeToken.StartQuote;
                }

                completionContext = new CommandCompletionContext(
                    context,
                    input,
                    caretIndex,
                    // Stages count arguments after the command name.
                    activeTokenIndex - 1,
                    precedingArguments,
                    token,
                    replacementStart,
                    replacementLength,
                    isQuoted,
                    quoteCharacter
                );

                try
                {
                    command.completionProvider(completionContext, results);
                }
                catch (Exception e)
                {
                    results.Clear();
                    Debug.LogError(
                        $"[DxCommandTerminal] Completion provider for '{commandName}' failed: {e.Message}"
                    );
                    return true;
                }

                /*
                   Drop empty candidates and duplicate insertion texts; first
                   occurrence wins and provider order is preserved.
                */
                int writeIndex = 0;
                int resultCount = results.Count;
                _completionDeduplication.Clear();
                for (int readIndex = 0; readIndex < resultCount; ++readIndex)
                {
                    CommandCompletion completion = results[readIndex];
                    if (string.IsNullOrEmpty(completion.InsertionText))
                    {
                        continue;
                    }

                    if (_completionDeduplication.Add(completion.InsertionText))
                    {
                        if (writeIndex != readIndex)
                        {
                            results[writeIndex] = completion;
                        }

                        ++writeIndex;
                    }
                }

                if (writeIndex < results.Count)
                {
                    results.RemoveRange(writeIndex, results.Count - writeIndex);
                }

                return true;
            }
            finally
            {
                _dispatchDepth--;
            }
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool AddCommand(string name, CommandInfo info)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Command Name: {name}");
                return false;
            }

            if (name.Contains(' '))
            {
                name = name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            if (!_commands.TryAdd(name, info))
            {
                IssueErrorMessage($"Command {name} is already defined.");
                return false;
            }

            ++_commandVersion;
            return true;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool AddCommand(
            string name,
            Action<CommandArg[]> proc,
            int minArgs = 0,
            int? maxArgs = null,
            string help = "",
            string hint = null,
            bool addToHistory = true
        )
        {
            CommandInfo info = new(proc, minArgs, maxArgs, help, hint, addToHistory);
            return AddCommand(name, info);
        }

        /// <summary>
        ///     Registers a context-aware command from a
        ///     <see cref="CommandDefinition"/>. The definition's current
        ///     values are snapshotted into the shell, so later edits to the
        ///     definition do not affect the registration. Exactly one handler
        ///     must be set; name and duplicate rules match the other
        ///     <see cref="AddCommand"/> overloads.
        /// </summary>
        public bool AddCommand(CommandDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            string name = definition.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Command Name: {name}");
                return false;
            }

            Action<CommandArg[]> legacyHandler = definition.LegacyHandler;
            CommandHandler handler = definition.Handler;
            if ((legacyHandler == null) == (handler == null))
            {
                IssueErrorMessage(
                    $"Command {name} must declare exactly one handler "
                        + $"({nameof(CommandDefinition.Handler)} or "
                        + $"{nameof(CommandDefinition.LegacyHandler)})."
                );
                return false;
            }

            CommandInfo info = new(
                legacyHandler,
                handler,
                definition.CompletionProvider,
                definition.Contexts,
                definition.MinArgCount,
                definition.MaxArgCount,
                definition.Help,
                definition.Hint,
                definition.AddToHistory
            );
            return AddCommand(name, info);
        }

        /// <summary>
        ///     Registers a typed command from a <see cref="CommandBuilder"/>
        ///     and returns a handle that removes exactly this registration on
        ///     dispose. Configuration errors (no handler, duplicate argument
        ///     names, required-after-optional ordering, unparseable argument
        ///     types, defaults failing their own validation) throw at
        ///     definition time; duplicate names against the live shell return
        ///     false like the other <see cref="AddCommand"/> overloads.
        /// </summary>
        public bool AddCommand(CommandBuilder builder, out CommandRegistrationHandle handle)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            CommandDefinition definition = builder.Build(this);
            if (!AddCommand(definition))
            {
                handle = null;
                return false;
            }

            handle = new CommandRegistrationHandle(this, definition.Name, definition.Handler);
            return true;
        }

        public bool SetVariable(string name, string value)
        {
            value ??= string.Empty;
            return SetVariable(name, new CommandArg(value));
        }

        public bool ClearVariable(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Variable Name: {name}");
                return false;
            }

            if (name.Contains(' '))
            {
                name = name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            return _variables.Remove(name);
        }

        /*
            Clears every variable in one call. The snapshot list is cached so
            repeated bulk clears (or a clear run from inside a command while
            the shell is iterating) never allocate; dictionary keys cannot be
            enumerated while entries are being removed.
         */
        public int ClearVariables()
        {
            _variableClearBuffer.Clear();
            foreach (string variable in _variables.Keys)
            {
                _variableClearBuffer.Add(variable);
            }

            foreach (string variable in _variableClearBuffer)
            {
                _variables.Remove(variable);
            }

            return _variableClearBuffer.Count;
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public bool SetVariable(string name, CommandArg value)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                IssueErrorMessage($"Invalid Variable Name: {name}");
                return false;
            }

            if (name.Contains(' '))
            {
                name = name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
            }

            _variables[name] = value;
            return true;
        }

        // ReSharper disable once UnusedMember.Global
        public bool TryGetVariable(string name, out CommandArg variable)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                variable = default;
                return false;
            }

            if (name.Contains(' '))
            {
                name =
                    name.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
                    ?? string.Empty;
            }

            return _variables.TryGetValue(name, out variable);
        }

        public void IssueErrorMessage(string format, params object[] parameters)
        {
            string formattedMessage =
                (parameters is { Length: > 0 } ? string.Format(format, parameters) : format)
                ?? string.Empty;
            _errorMessages.Enqueue(formattedMessage);
        }

        /// <summary>
        ///     Removes the command registered by <paramref name="registration"/>
        ///     under <paramref name="name"/>, identified by its handler
        ///     delegate, so a stale registration handle can never remove a
        ///     later replacement registered with the same name.
        /// </summary>
        internal bool TryRemoveCommand(string name, CommandHandler registration)
        {
            if (string.IsNullOrWhiteSpace(name) || registration == null)
            {
                return false;
            }

            if (
                !_commands.TryGetValue(name, out CommandInfo existing)
                || !ReferenceEquals(existing.handler, registration)
            )
            {
                return false;
            }

            if (_commands.Remove(name))
            {
                ++_commandVersion;
                return true;
            }

            return false;
        }

        private void RegisterAutoCommands()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Stopwatch stopwatch = Stopwatch.StartNew();
#endif
            int registeredCount = 0;
            int catalogAssemblies = 0;
            int providerAssemblies = 0;
            int reflectedAssemblies = 0;
            int includedAssemblies = 0;

            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            List<Assembly> scanCandidates = CollectScanCandidates(
                loadedAssemblies,
                typeof(BuiltInCommands).Assembly
            );

            List<AutoCommand> autoCommands = new();
            foreach (Assembly assembly in scanCandidates)
            {
                if (IsIncludedScanAssembly(assembly))
                {
                    includedAssemblies++;
                }

                switch (CollectAutoCommands(assembly, autoCommands))
                {
                    case AutoCommandSource.Catalog:
                    {
                        catalogAssemblies++;
                        break;
                    }
                    case AutoCommandSource.Provider:
                    {
                        providerAssemblies++;
                        break;
                    }
                    default:
                    {
                        reflectedAssemblies++;
                        break;
                    }
                }
            }

            foreach (AutoCommand command in autoCommands)
            {
                string commandName = command.Name;
                if (_ignoredCommands.Contains(commandName))
                {
                    continue;
                }

                if (IgnoringDefaultCommands && command.IsDefault)
                {
                    continue;
                }

                if (command.EditorOnly && !Application.isEditor)
                {
                    continue;
                }

                if (command.DevelopmentOnly && !Application.isEditor && !Debug.isDebugBuild)
                {
                    continue;
                }

                if (command.Binder == null)
                {
                    RegisterRejectedCommand(command, commandName);
                    continue;
                }

                /*
                    Reflection-discovered commands register without binding:
                    the first invocation pays their one-time
                    Delegate.CreateDelegate, so readiness cost no longer scales
                    with the number of declared commands.
                 */
                Action<CommandArg[]> proc;
                if (command.DeferredProc != null)
                {
                    proc = command.DeferredProc;
                }
                else
                {
                    try
                    {
                        proc = command.Binder();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(
                            $"[DxCommandTerminal] Failed to bind command {commandName} "
                                + $"(method {command.MethodName}): {e.Message}"
                        );
                        continue;
                    }

                    if (proc == null)
                    {
                        Debug.LogError(
                            $"[DxCommandTerminal] Failed to bind command {commandName} "
                                + $"(method {command.MethodName}): no handler was produced"
                        );
                        continue;
                    }
                }

                /*
                    User commands win over auto ones (built-ins included). The
                    collision is a console warning, not a queued terminal
                    error, so readiness never surfaces it mid-session. The
                    insert attempt doubles as the collision check, so a
                    colliding name is detected without a separate probe.
                 */
                if (string.IsNullOrWhiteSpace(commandName))
                {
                    IssueErrorMessage($"Invalid Command Name: {commandName}");
                    continue;
                }

                CommandInfo info = new(
                    proc,
                    null,
                    null,
                    command.Contexts,
                    command.MinArgCount,
                    command.MaxArgCount,
                    command.Help,
                    command.Hint,
                    command.AddToHistory
                );

                if (_commands.TryAdd(commandName, info))
                {
                    _autoRegisteredCommands.Add(commandName);
                    ++_commandVersion;
                    registeredCount++;
                }
                else
                {
                    Debug.LogWarning(
                        $"[DxCommandTerminal] Auto command {commandName} "
                            + $"(method {command.MethodName}) skipped: a command with "
                            + $"that name is already registered"
                    );
                }
            }

            AutoRegisteredCommands = _autoRegisteredCommands.ToReadOnlyHashSet(
                StringComparer.OrdinalIgnoreCase
            );

            foreach (KeyValuePair<string, string> command in _rejectedCommands)
            {
                IssueErrorMessage(
                    $"{command.Key} has an invalid signature. "
                        + $"Expected: {command.Key}(CommandArg[]). "
                        + $"Found: {command.Value}"
                );
            }

            AutoCommandsRegistered = true;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log(
                $"[DxCommandTerminal] Registered {registeredCount} auto-registered commands in "
                    + $"{stopwatch.Elapsed.TotalMilliseconds:F2} ms "
                    + $"({catalogAssemblies} generated catalog(s), {providerAssemblies} "
                    + $"provider-served assembly(ies), {reflectedAssemblies} "
                    + $"reflection-scanned assembly(ies), {includedAssemblies} "
                    + $"explicitly included assembly(ies))"
            );
#endif
        }

        private void RegisterRejectedCommand(AutoCommand command, string commandName)
        {
            MethodInfo method = null;
            if (command.MethodAccessor != null)
            {
                try
                {
                    method = command.MethodAccessor();
                }
                catch (Exception e)
                {
                    Debug.LogWarning(
                        $"[DxCommandTerminal] Failed to resolve rejected command {commandName} "
                            + $"(method {command.MethodName}) for diagnostics: {e.Message}"
                    );
                }
            }

            if (method == null)
            {
                IssueErrorMessage(
                    $"{commandName} has an invalid signature. "
                        + $"Expected: {command.MethodName}(CommandArg[])."
                );
                return;
            }

            using CachedStringBuilder.Scope found = new(64);
            found.Builder.Append(method.Name);
            if (method.IsGenericMethodDefinition)
            {
                /*
                    A generic definition renders like the valid shape, which
                    would contradict the invalid-signature message; reflection
                    arity spelling keeps the diagnostic self-explanatory.
                 */
                found.Builder.Append('`').Append(method.GetGenericArguments().Length);
            }

            found.Builder.Append('(');
            bool first = true;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (!first)
                {
                    found.Builder.Append(',');
                }

                found.Builder.Append(parameter.ParameterType.Name);
                first = false;
            }

            found.Builder.Append(')');
            _rejectedCommands.TryAdd(commandName, found.Builder.ToString());
        }

        private List<CommandArg> GetDispatchScope(int depth)
        {
            while (_dispatchScopes.Count <= depth)
            {
                _dispatchScopes.Add(new List<CommandArg>());
            }

            List<CommandArg> scope = _dispatchScopes[depth];
            scope.Clear();
            return scope;
        }

        private List<CommandToken> GetTokenScope(int depth)
        {
            while (_tokenScopes.Count <= depth)
            {
                _tokenScopes.Add(new List<CommandToken>());
            }

            List<CommandToken> scope = _tokenScopes[depth];
            scope.Clear();
            return scope;
        }

        /*
            Shared dispatch for every RunCommand shape: eligibility, argument
            validation, history accounting, and invocation are identical for
            array, list, and parsed-line callers.
         */
        private bool RunCommandCore(
            CommandExecutionContext context,
            string commandName,
            IReadOnlyList<CommandArg> arguments,
            string historyLine
        )
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                IssueErrorMessage($"Invalid command name '{commandName}'");
                // Don't log empty commands
                return false;
            }

            if (commandName.Contains(' '))
            {
                commandName = commandName.Replace(
                    " ",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase
                );
            }

            if (!_commands.TryGetValue(commandName, out CommandInfo command))
            {
                IssueErrorMessage($"Command {commandName} not found");
                // Unknown command, unconditional push
                _history.Push(
                    historyLine ?? BuildHistoryLine(commandName, arguments),
                    false,
                    false
                );
                return false;
            }

            if (!context.IsEligibleFor(command.executionContexts))
            {
                _errorMessages.Enqueue(
                    $"{commandName} is not available in the current execution context"
                );
                // Known but unavailable command, respect addToHistory flag
                if (command.addToHistory)
                {
                    _history.Push(
                        historyLine ?? BuildHistoryLine(commandName, arguments),
                        false,
                        false
                    );
                }

                return false;
            }

            int argCount = arguments.Count;
            string errorMessage = null;
            int requiredArg = 0;

            if (argCount < command.minArgCount)
            {
                errorMessage = command.minArgCount == command.maxArgCount ? "exactly" : "at least";
                requiredArg = command.minArgCount;
            }
            else if (command.maxArgCount is int maxArgCount && maxArgCount < argCount)
            {
                errorMessage = command.minArgCount == command.maxArgCount ? "exactly" : "at most";
                requiredArg = maxArgCount;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                string pluralFix = requiredArg == 1 ? "" : "s";

                string invalidMessage =
                    $"{commandName} requires {errorMessage} {requiredArg} argument{pluralFix}";
                if (!string.IsNullOrWhiteSpace(command.hint))
                {
                    invalidMessage += $"\n    -> Usage: {command.hint}";
                }

                _errorMessages.Enqueue(invalidMessage);
                // Known command with invalid arguments, respect addToHistory flag
                if (command.addToHistory)
                {
                    _history.Push(
                        historyLine ?? BuildHistoryLine(commandName, arguments),
                        false,
                        false
                    );
                }

                return false;
            }

            int errorCount = _errorMessages.Count;
            if (command.handler != null)
            {
                command.handler(context, new BorrowedCommandArguments(arguments));
            }
            else if (arguments is CommandArg[] ownedArguments)
            {
                // Array callers dispatch their own array, as before.
                command.proc?.Invoke(ownedArguments);
            }
            else
            {
                /*
                   Legacy handlers may retain their argument array, so each
                   invocation materializes a fresh one. The array is never
                   pooled or reused after the handler returns.
                */
                CommandArg[] materialized = new CommandArg[arguments.Count];
                for (int i = 0; i < materialized.Length; ++i)
                {
                    materialized[i] = arguments[i];
                }

                command.proc?.Invoke(materialized);
            }

            // Known command executed, respect addToHistory flag
            if (command.addToHistory)
            {
                _history.Push(
                    historyLine ?? BuildHistoryLine(commandName, arguments),
                    true,
                    errorCount == _errorMessages.Count
                );
            }

            return true;
        }

        private string BuildHistoryLine(string commandName, IReadOnlyList<CommandArg> arguments)
        {
            _commandBuilder.Clear();
            _commandBuilder.Append(commandName);
            if (arguments.Count != 0)
            {
                _commandBuilder.Append(' ');
            }

            bool firstArgument = true;
            foreach (CommandArg argument in arguments)
            {
                if (!firstArgument)
                {
                    _commandBuilder.Append(' ');
                }

                firstArgument = false;

                if (argument.startQuote != null)
                {
                    _commandBuilder.Append(argument.startQuote.Value);
                }

                _commandBuilder.Append(argument.contents);
                if (argument.endQuote != null)
                {
                    _commandBuilder.Append(argument.endQuote.Value);
                }
            }

            return _commandBuilder.ToString();
        }

        /*
            One generated command registration, shared by the generated-catalog
            path, the discovery-provider path, and the reflection compatibility
            path so all apply identical filtering, validation, and diagnostics.
            Internal so Editor discovery services (and tests) can produce
            candidates through the shared factories.
         */
        internal readonly struct AutoCommand
        {
            public readonly string Name;
            public readonly string MethodName;
            public readonly int MinArgCount;
            public readonly int MaxArgCount;
            public readonly string Help;
            public readonly string Hint;
            public readonly bool AddToHistory;
            public readonly bool EditorOnly;
            public readonly bool DevelopmentOnly;
            public readonly bool IsDefault;
            public readonly CommandExecutionContexts Contexts;

            /*
               Non-null for valid (CommandArg[]) signatures. Null marks a
               rejected command; diagnostics then come from MethodAccessor.
            */
            public readonly Func<Action<CommandArg[]>> Binder;

            public readonly Func<MethodInfo> MethodAccessor;

            /*
               Ready-to-register handler for reflection-discovered commands: it
               binds on its first invocation, so readiness never pays one
               Delegate.CreateDelegate per declared command. Null for catalog
               commands (their binders are static-field reads) and rejected
               commands. Shared across shells; the first invocation anywhere in
               the domain performs the one-time bind.
            */
            public readonly Action<CommandArg[]> DeferredProc;

            private AutoCommand(
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
                CommandExecutionContexts contexts,
                Func<Action<CommandArg[]>> binder,
                Func<MethodInfo> methodAccessor,
                Action<CommandArg[]> deferredProc
            )
            {
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
                Contexts = contexts;
                Binder = binder;
                MethodAccessor = methodAccessor;
                DeferredProc = deferredProc;
            }

            public static AutoCommand FromCatalog(CommandCatalogEntry entry)
            {
                return new AutoCommand(
                    entry.Name,
                    entry.MethodName,
                    entry.MinArgCount,
                    entry.MaxArgCount,
                    entry.Help,
                    entry.Hint,
                    entry.AddToHistory,
                    entry.EditorOnly,
                    entry.DevelopmentOnly,
                    entry.IsDefault,
                    entry.Contexts,
                    entry.Binder,
                    entry.MethodAccessor,
                    null
                );
            }

            public static AutoCommand FromReflected(
                MethodInfo method,
                RegisterCommandAttribute attribute
            )
            {
                bool valid = IsValidSignature(method);
                Func<Action<CommandArg[]>> binder = valid
                    ? () =>
                        (Action<CommandArg[]>)
                            Delegate.CreateDelegate(typeof(Action<CommandArg[]>), method)
                    : null;
                return new AutoCommand(
                    attribute.Name,
                    method.Name,
                    attribute.MinArgCount,
                    attribute.MaxArgCount,
                    attribute.Help,
                    attribute.Hint,
                    attribute.AddToHistory,
                    attribute.EditorOnly,
                    attribute.DevelopmentOnly,
                    attribute.Default,
                    attribute.Contexts,
                    binder,
                    () => method,
                    valid
                        ? new DeferredCommandHandler(attribute.Name, method.Name, binder).Invoke
                        : null
                );
            }

            private static bool IsValidSignature(MethodInfo method)
            {
                /*
                    Generic method definitions can never bind - the measured,
                    deterministic Delegate.CreateDelegate failure on every
                    scripting backend - so they are rejected commands with the
                    shared invalid-signature diagnostics. Signatures the
                    current backend happens to bind anyway (methods of open
                    generic types, abstract statics) stay registered: the
                    deferred binder preserves whatever the eager binder did,
                    including its contained error when a platform cannot bind.
                 */
                ParameterInfo[] methodParams = method.GetParameters();
                return methodParams.Length == 1
                    && methodParams[0].ParameterType == typeof(CommandArg[])
                    && !method.IsGenericMethodDefinition;
            }
        }

        /*
            Probes the assembly for its generated `CommandCatalog` and binds its
            Collect method, and caches the reflection-compatibility result for
            assemblies without one. Both are computed once per assembly per
            domain on whatever thread first triggers readiness; the weak table
            keeps unloaded-collectible editor assemblies collectible.
         */
        private sealed class DiscoveryCache
        {
            /*
                Written once per assembly on first initialization; concurrent
                first uses share one cache through the weak table, so the
                worst case is a harmless redundant re-probe.
             */
            public Action<List<CommandCatalogEntry>> Collector;
            public bool CollectorProbed;
            public List<AutoCommand> ReflectedCommands;
        }

        /*
            Immutable assembly metadata for the discovery scan. A null name
            or a null referenced-assembly list marks an unqueryable assembly:
            the scan treats it as a potential command carrier rather than
            silently dropping it. Instances are created once per assembly and
            shared through the weak table. This must stay a class:
            ConditionalWeakTable's value parameter only accepts reference
            types, and the alternatives (a dictionary keyed by Assembly)
            would root every assembly for the domain's lifetime.
         */
        private sealed class AssemblyClassification
        {
            public readonly AssemblyName Name;
            public readonly AssemblyName[] ReferencedAssemblies;

            public AssemblyClassification(AssemblyName name, AssemblyName[] referencedAssemblies)
            {
                Name = name;
                ReferencedAssemblies = referencedAssemblies;
            }
        }

        /*
            Where one assembly's auto commands came from, for the readiness
            accounting and readiness log. Internal for the discovery tests.
         */
        internal enum AutoCommandSource
        {
            [Obsolete("Use a valid value")]
            Unknown = 0,
            Catalog = 3,
            Provider = 1,
            Reflected = 2,
        }

        /*
            Removes its provider on the first dispose; a second dispose is a
            no-op. Duplicate registrations of the same provider produce
            independent handles, so a later registrant's dispose removes the
            provider the first registrant still holds.
         */
        private sealed class DiscoveryProviderRegistration : IDisposable
        {
            private ICommandDiscoveryProvider _provider;

            public DiscoveryProviderRegistration(ICommandDiscoveryProvider provider)
            {
                _provider = provider;
            }

            public void Dispose()
            {
                ICommandDiscoveryProvider provider = _provider;
                if (provider == null)
                {
                    return;
                }

                _provider = null;
                for (int i = 0; i < DiscoveryProviders.Count; ++i)
                {
                    if (ReferenceEquals(DiscoveryProviders[i], provider))
                    {
                        DiscoveryProviders.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        /*
            Removes its assembly from discovery on the first dispose; a second
            dispose is a no-op. Duplicate registrations of the same assembly
            produce independent handles, so a later registrant's dispose
            removes the assembly the first registrant still holds.
         */
        private sealed class DiscoveryAssemblyRegistration : IDisposable
        {
            private Assembly _assembly;

            public DiscoveryAssemblyRegistration(Assembly assembly)
            {
                _assembly = assembly;
            }

            public void Dispose()
            {
                Assembly assembly = _assembly;
                if (assembly == null)
                {
                    return;
                }

                _assembly = null;
                for (int i = 0; i < IncludedScanAssemblies.Count; ++i)
                {
                    if (ReferenceEquals(IncludedScanAssemblies[i], assembly))
                    {
                        IncludedScanAssemblies.RemoveAt(i);
                        return;
                    }
                }
            }
        }

        /*
            First-invocation binder for reflection-discovered commands. One
            instance lives per AutoCommand (shared across shells); its first
            invocation performs the Delegate.CreateDelegate bind and caches
            the delegate, and every later invocation dispatches through the
            cached reference. A failed bind keeps the contained-error
            contract: the error logs once and the command becomes a no-op
            instead of aborting dispatch or retrying a deterministic failure
            (CreateDelegate failures are platform-deterministic; the latch is
            unreachable from supported factories on backends that bind every
            valid signature). Bound on whatever thread first invokes the
            command, like all shell command state.
         */
        private sealed class DeferredCommandHandler
        {
            private readonly Func<Action<CommandArg[]>> _binder;
            private readonly string _commandName;
            private readonly string _methodName;

            private Action<CommandArg[]> _bound;
            private bool _failed;

            public DeferredCommandHandler(
                string commandName,
                string methodName,
                Func<Action<CommandArg[]>> binder
            )
            {
                _binder = binder;
                _commandName = commandName;
                _methodName = methodName;
            }

            public void Invoke(CommandArg[] arguments)
            {
                if (_failed)
                {
                    return;
                }

                Action<CommandArg[]> bound = _bound;
                if (bound == null)
                {
                    bound = Bind();
                    if (bound == null)
                    {
                        return;
                    }
                }

                bound(arguments);
            }

            private Action<CommandArg[]> Bind()
            {
                try
                {
                    Action<CommandArg[]> bound = _binder();
                    if (bound != null)
                    {
                        _bound = bound;
                        return bound;
                    }

                    Debug.LogError(
                        $"[DxCommandTerminal] Failed to bind command {_commandName} "
                            + $"(method {_methodName}): no handler was produced"
                    );
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"[DxCommandTerminal] Failed to bind command {_commandName} "
                            + $"(method {_methodName}): {e.Message}"
                    );
                }

                _failed = true;
                return null;
            }
        }
    }
}
