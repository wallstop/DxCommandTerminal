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
    using Debug = UnityEngine.Debug;

    /*
        Player compatibility bake (PLAN.md T05): at player build time,
        preserves every [RegisterCommand] handler whose bind path is
        reflection-by-name - the sites managed stripping cannot see. Two
        classes of handler bind that way:

        1. Private (or protected) non-partial handlers in assemblies whose
           generated catalog binds them through a cached GetMethod binder.
        2. Every attributed static handler in an assembly without a generated
           catalog (precompiled DLLs), where players fall back to the
           reflection walk.

        Public, internal, and protected-internal handlers in generated
        assemblies, and every handler a partial companion names directly, are
        rooted by generated code already and stay unpreserved, so the manifest
        stays surgical instead of preserve-everything.

        The manifest stages as a temporary link.xml under Assets for the
        duration of the build and is deleted afterwards; a failed build can
        leave it behind, and the next build's preprocess pass removes it. A
        consumer-owned file at the staging path is never touched, so staging
        failures degrade to today's behavior with a warning.
     */
    internal sealed class CommandCompatibilityBake
        : IPreprocessBuildWithReport,
            IPostprocessBuildWithReport
    {
        internal const string PartialCompanionTypeName = "DxCommandTerminalBinder";

        internal const string OwnershipMarker = "DxCommandTerminal player compatibility bake";

        internal const string StagingFileName = "DxCommandTerminalCommandCompatibility.link.xml";

        internal static string StagingAssetPath => "Assets/" + StagingFileName;

        private static readonly UTF8Encoding Utf8NoBom = new(false);

        public int callbackOrder => 0;

        internal static List<AttributedCommand> CollectAttributedCommands()
        {
            List<AttributedCommand> commands = new();
            Dictionary<Assembly, bool> playerAssemblies = new();
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
                if (!playerAssemblies.TryGetValue(assembly, out bool playerAssembly))
                {
                    playerAssembly = IsPlayerAssembly(assembly);
                    playerAssemblies.Add(assembly, playerAssembly);
                }

                if (!playerAssembly)
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

        internal static string WriteManifest(IReadOnlyList<PreservationEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            List<PreservationEntry> ordered = new(entries);
            ordered.Sort(CompareEntries);

            StringBuilder builder = new();
            builder.Append("<!-- ").Append(OwnershipMarker).Append(": preserves ");
            builder.Append("[RegisterCommand] handlers that are bound by name through ");
            builder.Append("reflection, which managed stripping cannot see. Rewritten on ");
            builder.Append("every player build; safe to delete. -->\n");
            builder.Append("<linker>\n");

            for (int i = 0; i < ordered.Count; )
            {
                PreservationEntry entry = ordered[i];
                builder
                    .Append("  <assembly fullname=\"")
                    .Append(SecurityElement.Escape(entry.AssemblyName))
                    .AppendLine("\">");
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
            return builder.ToString();
        }

        internal static bool TryStage(string path, string manifest)
        {
            if (string.IsNullOrWhiteSpace(path) || manifest == null)
            {
                return false;
            }

            if (File.Exists(path) && !IsOwnedStaging(path))
            {
                return false;
            }

            File.WriteAllText(path, manifest, Utf8NoBom);
            return true;
        }

        internal static bool IsOwnedStaging(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                string content = File.ReadAllText(path);
                return 0 <= content.IndexOf(OwnershipMarker, StringComparison.Ordinal);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static void CleanupStaging(string path)
        {
            if (!IsOwnedStaging(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
                string metaPath = path + ".meta";
                if (File.Exists(metaPath))
                {
                    File.Delete(metaPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Failed to remove player compatibility bake "
                        + $"staging at {path}: {e.Message}"
                );
            }
        }

        /*
            A player build cannot contain an assembly that hard-references an
            editor or test-framework assembly (it would fail to load), so
            those references identify exactly the assemblies whose commands
            never ship: editor tooling and test fixtures. Editor-compiled
            player-assembly lists cannot make this call - while test
            assemblies are compiled, they name test and user assemblies alike
            with the include-tests define. A failed reference read keeps the
            assembly (fail-open toward preservation).
         */
        private static bool IsPlayerAssembly(Assembly assembly)
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
                    return true;
                }

                foreach (AssemblyName reference in references)
                {
                    string name = reference.Name;
                    if (name == null)
                    {
                        continue;
                    }

                    if (
                        string.Equals(name, "UnityEditor", StringComparison.Ordinal)
                        || string.Equals(name, "UnityEngine.TestRunner", StringComparison.Ordinal)
                        || 0 <= name.IndexOf("nunit", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (Exception)
            {
                return true;
            }
        }

        private static bool IsRootedByGeneratedCode(
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

            if (IsDirectlyBindable(method))
            {
                return true;
            }

            return declaringType.GetNestedType(
                    PartialCompanionTypeName,
                    BindingFlags.Public | BindingFlags.NonPublic
                ) != null;
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
            The generated catalog names these handlers directly
            (public / internal / protected internal), which roots them for the
            linker; everything else in a generated assembly binds through a
            cached reflection-by-name binder.
         */
        private static bool IsDirectlyBindable(MethodInfo method)
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

        public void OnPreprocessBuild(BuildReport report)
        {
            if (IsOwnedStaging(StagingAssetPath))
            {
                CleanupStaging(StagingAssetPath);
                Debug.Log(
                    "[DxCommandTerminal] Removed stale player compatibility bake staging "
                        + "left behind by an earlier build"
                );
            }

            List<AttributedCommand> commands = CollectAttributedCommands();
            List<PreservationEntry> entries = CollectPreservations(commands);
            if (entries.Count == 0)
            {
                Debug.Log(
                    "[DxCommandTerminal] Player compatibility bake: every discovered "
                        + "command handler is rooted by generated code; nothing to preserve"
                );
                return;
            }

            string manifest = WriteManifest(entries);
            if (!TryStage(StagingAssetPath, manifest))
            {
                Debug.LogWarning(
                    $"[DxCommandTerminal] Player compatibility bake could not stage "
                        + $"{StagingFileName} under Assets (a file it does not own is in the "
                        + "way). Managed stripping may remove reflection-bound command "
                        + "handlers from this build"
                );
                return;
            }

            AssetDatabase.ImportAsset(StagingAssetPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log(
                $"[DxCommandTerminal] Player compatibility bake preserved {entries.Count} "
                    + $"reflection-bound command handler(s) -> {StagingAssetPath}"
            );
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!IsOwnedStaging(StagingAssetPath))
            {
                return;
            }

            CleanupStaging(StagingAssetPath);
            AssetDatabase.Refresh();
        }

        /*
            Gathers the bake's input from Unity's project-wide attributed-method
            index, restricted to assemblies that actually ship in a player:
            the index also covers editor and test assemblies, whose commands
            never reach a build, and a link.xml entry naming a missing assembly
            risks linker warnings.
         */

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
