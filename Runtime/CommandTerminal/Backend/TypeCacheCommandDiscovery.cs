namespace WallstopStudios.DxCommandTerminal.Backend
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Attributes;
    using UnityEditor;
    using Debug = UnityEngine.Debug;

    /*
        Editor command discovery backed by Unity's TypeCache, serving
        precompiled attributed command assemblies exactly instead of walking
        their types and methods. The TypeCache index is computed by Unity at
        compile/import time and is unavailable in players, where this type
        compiles out and discovery keeps the catalog-then-reflection path.

        The InitializeOnLoad hook fires at domain load, before any shell
        exists, so provider registration precedes every session reset -
        including disabled-domain-reload play sessions, where the static
        registration persists and the duplicate-registration no-op keeps the
        hook idempotent.
     */
    [InitializeOnLoad]
    internal sealed class TypeCacheCommandDiscovery : ICommandDiscoveryProvider
    {
        internal static readonly TypeCacheCommandDiscovery Instance =
            new TypeCacheCommandDiscovery();

        /*
            Snapshots are keyed weakly by assembly and hold only that
            assembly's own methods, so the table never pins an unreferenced
            collectible editor assembly alive.
         */
        private readonly ConditionalWeakTable<Assembly, TypeCacheSnapshot> _snapshots =
            new ConditionalWeakTable<Assembly, TypeCacheSnapshot>();

        private TypeCacheCommandDiscovery() { }

        static TypeCacheCommandDiscovery()
        {
            CommandShell.RegisterDiscoveryProvider(Instance);
        }

        private static bool AppendSnapshot(
            TypeCacheSnapshot snapshot,
            List<CommandShell.AutoCommand> commands
        )
        {
            if (!snapshot.Known)
            {
                return false;
            }

            commands.AddRange(snapshot.Commands);
            return true;
        }

        /*
            Builds the per-assembly snapshot from Unity's project-wide
            attributed-method index. An assembly the index does not know is
            reported unknown so it falls through to the next provider and the
            reflection walk (dynamic and late-loaded assemblies included);
            there is no per-assembly TypeCache membership probe, so an
            assembly with zero surviving candidates is also reported unknown
            and keeps paying its cheap cached reflection pass. The snapshot
            keeps only static candidates, exactly like the reflection oracle
            it replaces.
         */
        private static TypeCacheSnapshot BuildSnapshot(Assembly assembly)
        {
            List<CommandShell.AutoCommand> collected = new();
            try
            {
                TypeCache.MethodCollection attributedMethods =
                    TypeCache.GetMethodsWithAttribute<RegisterCommandAttribute>();
                foreach (MethodInfo method in attributedMethods)
                {
                    if (method == null)
                    {
                        continue;
                    }

                    Type declaringType = method.DeclaringType;
                    if (
                        declaringType == null
                        || !method.IsStatic
                        || !ReferenceEquals(declaringType.Assembly, assembly)
                    )
                    {
                        continue;
                    }

                    RegisterCommandAttribute attribute =
                        method.GetCustomAttribute<RegisterCommandAttribute>(false);
                    if (attribute == null)
                    {
                        continue;
                    }

                    attribute.NormalizeName(method);
                    collected.Add(CommandShell.AutoCommand.FromReflected(method, attribute));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] TypeCache command discovery failed for assembly "
                        + $"{assembly.GetName().Name}: {e.Message}; discovery continues "
                        + $"by reflection"
                );
                return TypeCacheSnapshot.Unknown;
            }

            return new TypeCacheSnapshot(collected, 0 < collected.Count);
        }

        public bool TryCollect(Assembly assembly, List<CommandShell.AutoCommand> commands)
        {
            if (assembly == null || commands == null)
            {
                return false;
            }

            if (_snapshots.TryGetValue(assembly, out TypeCacheSnapshot snapshot))
            {
                return AppendSnapshot(snapshot, commands);
            }

            snapshot = BuildSnapshot(assembly);
            try
            {
                _snapshots.Add(assembly, snapshot);
            }
            catch (ArgumentException)
            {
                /*
                    A concurrent first use built the snapshot first; sharing it
                    is equivalent to having won the race.
                */
                if (!_snapshots.TryGetValue(assembly, out snapshot))
                {
                    return false;
                }
            }

            return AppendSnapshot(snapshot, commands);
        }

        private sealed class TypeCacheSnapshot
        {
            internal static readonly TypeCacheSnapshot Unknown = new(
                new List<CommandShell.AutoCommand>(),
                false
            );

            public readonly List<CommandShell.AutoCommand> Commands;
            public readonly bool Known;

            public TypeCacheSnapshot(List<CommandShell.AutoCommand> commands, bool known)
            {
                Commands = commands;
                Known = known;
            }
        }
    }
#endif
}
