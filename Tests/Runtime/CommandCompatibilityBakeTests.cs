namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Reflection.Emit;
    using Attributes;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UnityEngine;

    /*
        Pins the player-compatibility bake: which [RegisterCommand] handlers
        are rooted by generated code (never preserved) and which are bound by
        reflection by name (preserved at player build time), plus the manifest
        and staging contracts the build hook leans on.
     */
    public sealed class CommandCompatibilityBakeTests
    {
        private const string CatalogLessHolderTypeName =
            "WallstopStudios.DxCommandTerminal.Tests.BakeProbeCommands";

#if UNITY_EDITOR
        /*
            Defined once: dynamic assemblies are non-collectible, and IL2CPP
            players do not support Reflection.Emit, so this case is
            editor-only. The emitted holder carries the only attributed
            commands in the domain that no generated catalog serves.
         */
        private static readonly Assembly CatalogLessAssembly = CreateCatalogLessAssembly();

        private static Assembly CreateCatalogLessAssembly()
        {
            AssemblyName name = new($"CompatibilityBakeTestDynamic-{Guid.NewGuid():N}");
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                name,
                AssemblyBuilderAccess.Run
            );
            ModuleBuilder module = assembly.DefineDynamicModule("Commands");
            TypeBuilder type = module.DefineType(
                CatalogLessHolderTypeName,
                TypeAttributes.Public | TypeAttributes.Sealed
            );
            MethodBuilder preserved = type.DefineMethod(
                "ProbePreservedCommand",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            preserved.DefineParameter(1, ParameterAttributes.None, "args");
            preserved.GetILGenerator().Emit(OpCodes.Ret);
            ConstructorInfo attributeConstructor = typeof(RegisterCommandAttribute).GetConstructor(
                new[] { typeof(string) }
            );
            preserved.SetCustomAttribute(
                new CustomAttributeBuilder(
                    attributeConstructor,
                    new object[] { "bake-probe-preserved" }
                )
            );
            MethodBuilder instance = type.DefineMethod(
                "ProbeInstanceCommand",
                MethodAttributes.Public,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            instance.DefineParameter(1, ParameterAttributes.None, "args");
            instance.GetILGenerator().Emit(OpCodes.Ret);
            instance.SetCustomAttribute(
                new CustomAttributeBuilder(
                    attributeConstructor,
                    new object[] { "bake-probe-instance" }
                )
            );
            type.CreateType();
            return assembly;
        }
#endif

        private string _stagingRoot;

        private static string SampleManifest()
        {
            return CommandCompatibilityBake.WriteManifest(
                new[]
                {
                    new CommandCompatibilityBake.PreservationEntry(
                        "Assembly-A",
                        "Ns.Type",
                        "Handler"
                    ),
                }
            );
        }

        private static bool ContainsEntry(
            List<CommandCompatibilityBake.PreservationEntry> entries,
            string assemblyName,
            string typeFullName,
            string methodName
        )
        {
            foreach (CommandCompatibilityBake.PreservationEntry entry in entries)
            {
                if (
                    string.Equals(entry.AssemblyName, assemblyName, StringComparison.Ordinal)
                    && string.Equals(entry.TypeFullName, typeFullName, StringComparison.Ordinal)
                    && string.Equals(entry.MethodName, methodName, StringComparison.Ordinal)
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static List<CommandCompatibilityBake.PreservationEntry> Collect(
            params MethodInfo[] methods
        )
        {
            List<CommandCompatibilityBake.AttributedCommand> commands = new();
            foreach (MethodInfo method in methods)
            {
                RegisterCommandAttribute attribute =
                    method.GetCustomAttribute<RegisterCommandAttribute>(false);
                Assert.That(
                    attribute != null,
                    $"Sanity: expected [RegisterCommand] on {method.Name}"
                );
                commands.Add(
                    new CommandCompatibilityBake.AttributedCommand(
                        method.DeclaringType.Assembly,
                        method,
                        attribute
                    )
                );
            }

            return CommandCompatibilityBake.CollectPreservations(commands);
        }

        [SetUp]
        public void SetUp()
        {
            _stagingRoot = Path.Combine(
                Application.temporaryCachePath,
                "CompatibilityBakeTests-" + Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_stagingRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_stagingRoot))
            {
                Directory.Delete(_stagingRoot, true);
            }
        }

        [TestCase("TestCommand")]
        [TestCase("TestDiscoveryNamed")]
        public void PrivateNonPartialHandlersInGeneratedAssembliesArePreserved(string methodName)
        {
            MethodInfo method = typeof(CommandDiscoveryTests).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(
                method != null,
                $"Sanity: expected the {methodName} fixture on CommandDiscoveryTests"
            );

            List<CommandCompatibilityBake.PreservationEntry> entries = Collect(method);

            Assert.IsTrue(
                ContainsEntry(
                    entries,
                    typeof(CommandDiscoveryTests).Assembly.GetName().Name,
                    typeof(CommandDiscoveryTests).FullName,
                    methodName
                ),
                $"Private non-partial handler {methodName} is bound through a "
                    + "reflection-by-name cached binder and must be preserved"
            );
        }

        [Test]
        public void PublicHandlerBoundByGeneratedCodeIsNotPreserved()
        {
            MethodInfo method = typeof(BakeFixtureRooted).GetMethod(
                nameof(BakeFixtureRooted.RootedCommand),
                BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method != null, "Sanity: expected the rooted public fixture");

            List<CommandCompatibilityBake.PreservationEntry> entries = Collect(method);

            Assert.IsEmpty(
                entries,
                "A public handler is named directly by generated catalog code, "
                    + "which roots it for the linker; preserving it is over-preservation"
            );
        }

        [Test]
        public void PartialCompanionHandlerIsNotPreserved()
        {
            MethodInfo method = typeof(BakePartialFixtureCommands).GetMethod(
                "PartialPrivateCommand",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(method != null, "Sanity: expected the partial companion fixture");

            List<CommandCompatibilityBake.PreservationEntry> entries = Collect(method);

            Assert.IsEmpty(
                entries,
                "A partial companion names the private handler directly, which "
                    + "roots it for the linker; preserving it is over-preservation"
            );
        }

        [Test]
        public void EditorOnlyHandlerIsNotPreserved()
        {
            MethodInfo method = typeof(BakeFixtureEditorOnly).GetMethod(
                "EditorOnlyCommand",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(method != null, "Sanity: expected the editor-only fixture");

            List<CommandCompatibilityBake.PreservationEntry> entries = Collect(method);

            Assert.IsEmpty(
                entries,
                "Editor-only handlers never register in players, so their "
                    + "preservation data would be dead weight"
            );
        }

        [TestCase("PrivateCommand")]
        [TestCase("ProtectedCommand")]
        public void InaccessibleNonPartialHandlersArePreserved(string methodName)
        {
            MethodInfo method = typeof(BakeFixtureInaccessible).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method != null, $"Sanity: expected the {methodName} inaccessible fixture");

            List<CommandCompatibilityBake.PreservationEntry> entries = Collect(method);

            Assert.IsTrue(
                ContainsEntry(
                    entries,
                    typeof(BakeFixtureInaccessible).Assembly.GetName().Name,
                    typeof(BakeFixtureInaccessible).FullName,
                    methodName
                ),
                $"Inaccessible non-partial handler {methodName} binds through a "
                    + "reflection-by-name cached binder and must be preserved"
            );
        }

#if UNITY_EDITOR
        [Test]
        public void HandlersInAssembliesWithoutGeneratedCatalogsArePreserved()
        {
            Type holder = CatalogLessAssembly.GetType(CatalogLessHolderTypeName);
            Assert.That(holder != null, "Sanity: expected the emitted command holder type");

            List<MethodInfo> declared = new();
            foreach (
                MethodInfo method in holder.GetMethods(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance
                )
            )
            {
                if (ReferenceEquals(method.DeclaringType, holder))
                {
                    declared.Add(method);
                }
            }

            List<CommandCompatibilityBake.PreservationEntry> entries = Collect(declared.ToArray());

            Assert.AreEqual(
                1,
                entries.Count,
                "Exactly the static handler in the catalog-less assembly is preserved"
            );
            Assert.AreEqual(
                "ProbePreservedCommand",
                entries[0].MethodName,
                "The static handler is reflection-bound and must be preserved"
            );
        }

        [Test]
        public void AttributedCommandCollectionExcludesNonPlayerAssemblies()
        {
            List<CommandCompatibilityBake.AttributedCommand> commands =
                CommandCompatibilityBake.CollectAttributedCommands();

            foreach (CommandCompatibilityBake.AttributedCommand command in commands)
            {
                Assembly assembly = command.Assembly;
                Assert.IsFalse(assembly.IsDynamic, "Dynamic assemblies never ship in players");
                foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
                {
                    string name = reference.Name;
                    if (name == null)
                    {
                        continue;
                    }

                    Assert.IsFalse(
                        string.Equals(name, "UnityEditor", StringComparison.Ordinal)
                            || string.Equals(
                                name,
                                "UnityEngine.TestRunner",
                                StringComparison.Ordinal
                            )
                            || 0 <= name.IndexOf("nunit", StringComparison.OrdinalIgnoreCase),
                        $"{assembly.GetName().Name} references {name}, so it never "
                            + "ships in players and must not enter the bake"
                    );
                }

                Assert.IsTrue(
                    command.Method.IsStatic,
                    "Only static handlers are discovered and preserved"
                );
            }
        }
#endif

        [Test]
        public void EmptyCommandListWritesNoManifest()
        {
            Assert.That(
                CommandCompatibilityBake.WriteManifest(
                    new List<CommandCompatibilityBake.PreservationEntry>()
                ) == null,
                "A build with nothing to preserve must not write a linker file"
            );
        }

        [Test]
        public void ManifestIsDeterministicOrderedDeduplicatedAndEscaped()
        {
            string manifest = CommandCompatibilityBake.WriteManifest(
                new[]
                {
                    new CommandCompatibilityBake.PreservationEntry("B", "T", "M"),
                    new CommandCompatibilityBake.PreservationEntry("A", "T2", "M2"),
                    new CommandCompatibilityBake.PreservationEntry("A", "T1", "M1"),
                    new CommandCompatibilityBake.PreservationEntry("A", "T1", "M1"),
                    new CommandCompatibilityBake.PreservationEntry("A", "T1", "Handler<1>&"),
                }
            );

            string expected =
                "<!-- "
                + CommandCompatibilityBake.OwnershipMarker
                + ": preserves [RegisterCommand] handlers that are bound by name "
                + "through reflection, which managed stripping cannot see. "
                + "Rewritten on every player build; safe to delete. -->\n"
                + "<linker>\n"
                + "  <assembly fullname=\"A\">\n"
                + "    <type fullname=\"T1\" preserve=\"nothing\">\n"
                + "      <method name=\"Handler&lt;1&gt;&amp;\" />\n"
                + "      <method name=\"M1\" />\n"
                + "    </type>\n"
                + "    <type fullname=\"T2\" preserve=\"nothing\">\n"
                + "      <method name=\"M2\" />\n"
                + "    </type>\n"
                + "  </assembly>\n"
                + "  <assembly fullname=\"B\">\n"
                + "    <type fullname=\"T\" preserve=\"nothing\">\n"
                + "      <method name=\"M\" />\n"
                + "    </type>\n"
                + "  </assembly>\n"
                + "</linker>\n";

            Assert.AreEqual(expected, manifest);
        }

        [Test]
        public void StagingClaimsOnlyOwnedFiles()
        {
            string ownedPath = Path.Combine(_stagingRoot, "owned.link.xml");
            string foreignPath = Path.Combine(_stagingRoot, "foreign.link.xml");
            File.WriteAllText(foreignPath, "consumer-owned linker rules");

            Assert.IsTrue(
                CommandCompatibilityBake.TryStage(ownedPath, SampleManifest()),
                "A fresh staging path accepts the manifest"
            );
            Assert.IsTrue(
                CommandCompatibilityBake.TryStage(ownedPath, SampleManifest()),
                "A re-run over a file the bake owns rewrites it"
            );
            Assert.IsFalse(
                CommandCompatibilityBake.TryStage(foreignPath, "<linker></linker>\n"),
                "A same-named file the bake does not own is never overwritten"
            );
            Assert.AreEqual(
                "consumer-owned linker rules",
                File.ReadAllText(foreignPath),
                "The foreign file's content stays untouched"
            );
            Assert.IsFalse(
                CommandCompatibilityBake.IsOwnedStaging(foreignPath),
                "A file without the ownership marker is foreign"
            );
            Assert.IsTrue(
                CommandCompatibilityBake.IsOwnedStaging(ownedPath),
                "A staged manifest carries the ownership marker"
            );
        }

        [Test]
        public void StagingCleanupRemovesOnlyOwnedFiles()
        {
            string ownedPath = Path.Combine(_stagingRoot, "owned.link.xml");
            string foreignPath = Path.Combine(_stagingRoot, "foreign.link.xml");
            File.WriteAllText(foreignPath, "consumer-owned linker rules");
            CommandCompatibilityBake.TryStage(ownedPath, SampleManifest());

            CommandCompatibilityBake.CleanupStaging(foreignPath);
            Assert.IsTrue(
                File.Exists(foreignPath),
                "Cleanup never deletes a file the bake does not own"
            );

            CommandCompatibilityBake.CleanupStaging(ownedPath);
            Assert.IsFalse(File.Exists(ownedPath), "Cleanup removes the staged manifest");

            CommandCompatibilityBake.CleanupStaging(ownedPath);
            Assert.IsTrue(File.Exists(foreignPath), "A repeated cleanup pass stays a no-op");
        }

        internal static class BakeFixtureRooted
        {
            [RegisterCommand(
                Help = "Bake fixture rooted by a direct generated delegate.",
                Name = "bake-rooted"
            )]
            public static void RootedCommand(CommandArg[] args) { }
        }

        internal static class BakeFixtureEditorOnly
        {
            [RegisterCommand(
                Help = "Bake fixture that never registers in players.",
                Name = "bake-editor-only",
                EditorOnly = true
            )]
            private static void EditorOnlyCommand(CommandArg[] args) { }
        }

        internal class BakeFixtureInaccessible
        {
            [RegisterCommand(
                Help = "Bake fixture bound through a cached binder.",
                Name = "bake-protected"
            )]
            protected static void ProtectedCommand(CommandArg[] args) { }

            [RegisterCommand(
                Help = "Bake fixture bound through a cached binder.",
                Name = "bake-private"
            )]
            private static void PrivateCommand(CommandArg[] args) { }
        }
    }
}
