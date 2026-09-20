namespace WallstopStudios.DxCommandTerminal.Backend
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Security;
    using System.Text;
    using Attributes;
    using UnityEditor;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;
    using UnityEditor.UnityLinker;
    using Debug = UnityEngine.Debug;
    using IUnityLinkerProcessor = UnityEditor.Build.IUnityLinkerProcessor;

    /*
        Player compatibility bake (PLAN.md T05): at player build time,
        preserves every [RegisterCommand] handler whose bind path is
        reflection-by-name - the sites managed stripping cannot see. Three
        classes of handler bind that way:

        1. Private (or protected) handlers in generated assemblies that are
           not in a partial chain: the catalog binds them through a cached
           GetMethod binder.
        2. Handlers whose shape the catalog cannot bind directly even when
           accessible - non-void returns, generic methods, open generic
           declaring types, and invalid signatures: the catalog reaches them
           by string name too (cached binder or rejected-command accessor).
        3. Every attributed static handler in an assembly without a generated
           catalog (precompiled DLLs), where players fall back to the
           reflection walk.

        Only handlers a partial companion names directly, and handlers the
        catalog binds as direct delegates (valid shape AND public, internal,
        or protected internal), are rooted by generated code; they stay
        unpreserved, so the manifest stays surgical instead of
        preserve-everything.

        The manifest is fed to the linker as an additional link.xml file
        (Unity only auto-loads Assets files named exactly "link.xml") and is
        written under Temp for the duration of the linker run, so nothing
        under Assets is ever touched. Assembly entries carry
        ignoreIfMissing="1": the editor domain can name an assembly that a
        particular player build does not contain (test or editor assemblies),
        and the linker skips those silently instead of warning.
     */
    internal sealed class CommandCompatibilityBake : IUnityLinkerProcessor
    {
        internal const string PartialCompanionTypeName = "DxCommandTerminalBinder";

        internal const string OwnershipMarker = "DxCommandTerminal player compatibility bake";

        private const string ManifestFileName = "DxCommandTerminalCompatibilityBake.link.xml";

        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public int callbackOrder => 0;

        internal static List<AttributedCommand> CollectAttributedCommands()
        {
            List<AttributedCommand> commands = new();
            Dictionary<Assembly, bool> shippingAssemblies = new();
            foreach (
                MethodInfo method in TypeCache.GetMethodsWithAttribute<RegisterCommandAttribute>()
            )
            {
                if (method == null || !method.IsStatic)
                {
                    continue;
                }

                Type declaringType = method.DeclaringType;
                if (declaringType == null)
                {
                    continue;
                }

                Assembly assembly = declaringType.Assembly;
                if (!shippingAssemblies.TryGetValue(assembly, out bool shippingAssembly))
                {
                    shippingAssembly = IsTestAssembly(assembly);
                    shippingAssemblies.Add(assembly, shippingAssembly);
                }

                if (shippingAssembly)
                {
                    continue;
                }

                RegisterCommandAttribute attribute =
                    method.GetCustomAttribute<RegisterCommandAttribute>(false);
                if (attribute == null)
                {
                    continue;
                }

                commands.Add(new AttributedCommand(assembly, method, attribute));
            }

            return commands;
        }

        /*
            Test assemblies never ship in players, and their commands never
            belong in a preservation manifest; they are identifiable by their
            framework references. Nothing else is excluded: an assembly that
            references UnityEditor in the editor may still ship a player
            variant under the same name (runtime sources with `#if
            UNITY_EDITOR` blocks do), so editor references cannot mark
            non-shipping assemblies. Entries for assemblies a given build
            does not contain are inert through ignoreIfMissing="1".
         */
        internal static List<PreservationEntry> CollectPreservations(
            IReadOnlyList<AttributedCommand> commands
        )
        {
            List<PreservationEntry> entries = new();
            if (commands == null || commands.Count == 0)
            {
                return entries;
            }

            Dictionary<Assembly, bool> catalogAssemblies = new();
            foreach (AttributedCommand command in commands)
            {
                MethodInfo method = command.Method;
                if (method == null || !method.IsStatic)
                {
                    continue;
                }

                Type declaringType = method.DeclaringType;
                if (declaringType == null)
                {
                    continue;
                }

                RegisterCommandAttribute attribute = command.Attribute;
                if (attribute == null || attribute.EditorOnly)
                {
                    continue;
                }

                if (
                    IsRootedByGeneratedCode(
                        command.Assembly,
                        declaringType,
                        method,
                        catalogAssemblies
                    )
                )
                {
                    continue;
                }

                string assemblyName = command.Assembly.GetName().Name;
                string typeFullName = declaringType.FullName;
                if (assemblyName == null || typeFullName == null)
                {
                    continue;
                }

                entries.Add(new PreservationEntry(assemblyName, typeFullName, method.Name));
            }

            return entries;
        }

        internal static bool TryBuildManifest(
            IReadOnlyList<PreservationEntry> entries,
            out string manifest
        )
        {
            manifest = null;
            if (entries == null || entries.Count == 0)
            {
                return false;
            }

            List<PreservationEntry> ordered = new(entries);
            ordered.Sort(CompareEntries);

            StringBuilder builder = new();
            builder.Append("<!-- ").Append(OwnershipMarker).Append(": preserves ");
            builder.Append("[RegisterCommand] handlers that are bound by name through ");
            builder.Append("reflection, which managed stripping cannot see. Written under ");
            builder.Append("Temp per player build; safe to delete. -->\n");
            builder.Append("<linker>\n");

            for (int i = 0; i < ordered.Count; )
            {
                PreservationEntry entry = ordered[i];
                builder
                    .Append("  <assembly fullname=\"")
                    .Append(SecurityElement.Escape(entry.AssemblyName))
                    .AppendLine("\" ignoreIfMissing=\"1\">");
                for (; i < ordered.Count; )
                {
                    if (
                        !string.Equals(
                            ordered[i].AssemblyName,
                            entry.AssemblyName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        break;
                    }

                    PreservationEntry typeEntry = ordered[i];
                    builder
                        .Append("    <type fullname=\"")
                        .Append(SecurityElement.Escape(typeEntry.TypeFullName))
                        .AppendLine("\" preserve=\"nothing\">");
                    string lastMethodName = null;
                    for (; i < ordered.Count; ++i)
                    {
                        if (
                            !string.Equals(
                                ordered[i].AssemblyName,
                                entry.AssemblyName,
                                StringComparison.Ordinal
                            )
                            || !string.Equals(
                                ordered[i].TypeFullName,
                                typeEntry.TypeFullName,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            break;
                        }

                        string methodName = ordered[i].MethodName;
                        if (string.Equals(lastMethodName, methodName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        lastMethodName = methodName;
                        builder
                            .Append("      <method name=\"")
                            .Append(SecurityElement.Escape(methodName))
                            .AppendLine("\" />");
                    }

                    builder.AppendLine("    </type>");
                }

                builder.AppendLine("  </assembly>");
            }

            builder.AppendLine("</linker>");
            manifest = builder.ToString();
            return true;
        }

        internal static bool TryWriteManifestFile(string manifest, out string path)
        {
            path = null;
            if (string.IsNullOrEmpty(manifest))
            {
                return false;
            }

            try
            {
                string directory = Path.Combine(Directory.GetCurrentDirectory(), "Temp");
                Directory.CreateDirectory(directory);
                /*
                    Unity's own generated linker files use a per-write unique
                    Temp name; a stale file from an earlier build can never be
                    mistaken for this build's manifest and Temp is purged by
                    the editor, so no cleanup lifecycle is needed.
                */
                string fileName = $"UnityTempFile-{Guid.NewGuid():N}-{ManifestFileName}";
                path = Path.Combine(directory, fileName);
                File.WriteAllText(path, manifest, Utf8NoBom);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Player compatibility bake manifest write failed: "
                        + $"{e.Message}"
                );
                path = null;
                return false;
            }
        }

        internal static bool HasDirectBindableShape(MethodInfo method)
        {
            if (method.ReturnType != typeof(void) || method.IsGenericMethod)
            {
                return false;
            }

            for (
                Type containing = method.DeclaringType;
                containing != null;
                containing = containing.DeclaringType
            )
            {
                if (containing.IsGenericTypeDefinition)
                {
                    return false;
                }
            }

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 1 && parameters[0].ParameterType == typeof(CommandArg[]);
        }

        /*
            The generator's accessible-from-catalog set (public, internal,
            protected internal); private, protected, and private protected
            handlers need the partial companion to be rooted.
         */
        internal static bool IsRootedByGeneratedCode(
            Assembly assembly,
            Type declaringType,
            MethodInfo method,
            Dictionary<Assembly, bool> catalogAssemblies
        )
        {
            if (!catalogAssemblies.TryGetValue(assembly, out bool hasCatalog))
            {
                hasCatalog = ProbeGeneratedCatalog(assembly);
                catalogAssemblies.Add(assembly, hasCatalog);
            }

            if (!hasCatalog)
            {
                return false;
            }

            /*
                Mirrors the generator's per-method binding decision, not a
                type-wide one: a companion emitted for one method roots only
                the methods it names, and an accessible method with a shape
                the catalog cannot bind directly (non-void, generic, invalid
                signature) still binds through a reflection-by-name binder.
            */
            if (!HasDirectBindableShape(method))
            {
                return false;
            }

            if (IsDirectlyAccessible(method))
            {
                return true;
            }

            return HasCompanionBinder(declaringType);
        }

        /*
            A partial companion is the generator-emitted nested holder whose
            members are zero-argument static methods returning
            Action<CommandArg[]>. The name alone is not proof: a non-partial
            holder can declare its own unrelated nested
            DxCommandTerminalBinder without colliding, and rooting on the
            name would silently leave its cached-binder handlers strippable.
         */
        private static bool HasCompanionBinder(Type declaringType)
        {
            Type companion = declaringType.GetNestedType(
                PartialCompanionTypeName,
                BindingFlags.Public | BindingFlags.NonPublic
            );
            if (companion == null)
            {
                return false;
            }

            foreach (
                MethodInfo binder in companion.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                )
            )
            {
                if (
                    binder.ReturnType == typeof(Action<CommandArg[]>)
                    && 0 == binder.GetParameters().Length
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTestAssembly(Assembly assembly)
        {
            if (assembly.IsDynamic)
            {
                return false;
            }

            try
            {
                AssemblyName[] references = assembly.GetReferencedAssemblies();
                if (references == null)
                {
                    return false;
                }

                foreach (AssemblyName reference in references)
                {
                    string name = reference.Name;
                    if (name == null)
                    {
                        continue;
                    }

                    if (
                        string.Equals(name, "UnityEngine.TestRunner", StringComparison.Ordinal)
                        || 0 <= name.IndexOf("nunit", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool ProbeGeneratedCatalog(Assembly assembly)
        {
            try
            {
                return assembly.GetType(CommandShell.CatalogTypeName, false) != null;
            }
            catch (Exception)
            {
                /*
                    A probe failure must never drop preservation: treat the
                    assembly as catalog-less so every handler it declares is
                    preserved (over-preservation is the safe direction).
                */
                return false;
            }
        }

        /*
            The shape the generator binds as a direct delegate: void return,
            exactly one CommandArg[] parameter by value, no generic method,
            and no open generic declaration in the containing chain. Ref/out
            parameters fail the parameter-type comparison automatically
            (their managed type is a by-ref of the array). Everything else
            binds by reflection-by-name, whatever its accessibility.
         */
        private static bool IsDirectlyAccessible(MethodInfo method)
        {
            return method.IsPublic || method.IsAssembly || method.IsFamilyOrAssembly;
        }

        private static int CompareEntries(PreservationEntry left, PreservationEntry right)
        {
            int assembly = string.CompareOrdinal(left.AssemblyName, right.AssemblyName);
            if (assembly != 0)
            {
                return assembly;
            }

            int type = string.CompareOrdinal(left.TypeFullName, right.TypeFullName);
            if (type != 0)
            {
                return type;
            }

            return string.CompareOrdinal(left.MethodName, right.MethodName);
        }

        /*
            Empty bodies: Unity 6 warns on non-empty OnBeforeRun/OnAfterRun
            implementations (both hooks no longer run), and the manifest needs
            no lifecycle around the linker run - it is written under Temp,
            which Unity purges, following the same convention as the engine's
            own generated linker files.
         */
        public void OnBeforeRun(BuildReport report, UnityLinkerBuildPipelineData data) { }

        /*
            Runs at the stripping stage of every player build, where the
            editor's TypeCache is still available and the manifest can name
            the exact handler set the player domain will hold.
         */
        public string GenerateAdditionalLinkXmlFile(
            BuildReport report,
            UnityLinkerBuildPipelineData data
        )
        {
            List<PreservationEntry> entries = CollectPreservations(CollectAttributedCommands());
            if (!TryBuildManifest(entries, out string manifest))
            {
                Debug.Log(
                    "[DxCommandTerminal] Player compatibility bake: every discovered "
                        + "command handler is rooted by generated code; nothing to preserve"
                );
                return null;
            }

            if (!TryWriteManifestFile(manifest, out string path))
            {
                Debug.LogWarning(
                    "[DxCommandTerminal] Player compatibility bake could not write its "
                        + "preservation manifest; managed stripping may remove "
                        + "reflection-bound command handlers from this build"
                );
                return null;
            }

            Debug.Log(
                $"[DxCommandTerminal] Player compatibility bake preserved {entries.Count} "
                    + $"reflection-bound command handler(s) -> {path}"
            );
            return path;
        }

        public void OnAfterRun(BuildReport report, string outputFolder) { }

        internal readonly struct AttributedCommand
        {
            public readonly Assembly Assembly;
            public readonly MethodInfo Method;
            public readonly RegisterCommandAttribute Attribute;

            public AttributedCommand(
                Assembly assembly,
                MethodInfo method,
                RegisterCommandAttribute attribute
            )
            {
                Assembly = assembly;
                Method = method;
                Attribute = attribute;
            }
        }

        internal readonly struct PreservationEntry
        {
            public readonly string AssemblyName;
            public readonly string TypeFullName;
            public readonly string MethodName;

            public PreservationEntry(string assemblyName, string typeFullName, string methodName)
            {
                AssemblyName = assemblyName;
                TypeFullName = typeFullName;
                MethodName = methodName;
            }
        }
    }
#endif
}
