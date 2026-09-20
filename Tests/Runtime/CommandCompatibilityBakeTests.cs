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
        and Temp-file contracts the linker processor leans on.
     */
    public sealed class CommandCompatibilityBakeTests
    {
        private const string CatalogLessHolderTypeName =
            "WallstopStudios.DxCommandTerminal.Tests.BakeProbeCommands";

#if UNITY_EDITOR
        /*
            Defined once: dynamic assemblies are non-collectible, and IL2CPP
            players do not support Reflection.Emit, so this case is
            editor-only. The emitted holder carries attributed commands that
            no generated catalog serves, plus unattributed probes for the
            direct-bindable shape gate.
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
            ConstructorInfo attributeConstructor = typeof(RegisterCommandAttribute).GetConstructor(
                new[] { typeof(string) }
            );

            MethodBuilder preserved = type.DefineMethod(
                "ProbePreservedCommand",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            preserved.DefineParameter(1, ParameterAttributes.None, "args");
            preserved.GetILGenerator().Emit(OpCodes.Ret);
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

            /*
                Unattributed shape probes: the exact signatures the shape
                gate must classify, without the registration side effects a
                compiled attributed fixture would bring.
            */
            MethodBuilder nonVoid = type.DefineMethod(
                "ProbeNonVoid",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                new[] { typeof(CommandArg[]) }
            );
            nonVoid.DefineParameter(1, ParameterAttributes.None, "args");
            nonVoid.GetILGenerator().Emit(OpCodes.Ldc_I4_0);
            nonVoid.GetILGenerator().Emit(OpCodes.Ret);

            MethodBuilder byRefParameter = type.DefineMethod(
                "ProbeByRefParameter",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(CommandArg[]).MakeByRefType() }
            );
            byRefParameter.DefineParameter(1, ParameterAttributes.Out, "args");
            byRefParameter.GetILGenerator().Emit(OpCodes.Ret);

            type.CreateType();
            return assembly;
        }
#endif

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
                "A public handler the catalog binds as a direct delegate is rooted "
                    + "by generated code; preserving it is over-preservation"
            );
        }

        [Test]
        public void ProtectedInternalHandlerBoundByGeneratedCodeIsNotPreserved()
        {
            MethodInfo method = typeof(BakeFixtureInaccessible).GetMethod(
                nameof(BakeFixtureInaccessible.ProtectedInternalCommand),
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method != null, "Sanity: expected the protected internal fixture");

            List<CommandCompatibilityBake.PreservationEntry> entries = Collect(method);

            Assert.IsEmpty(
                entries,
                "Protected internal is inside the generator's accessible-from-catalog "
                    + "set; a valid-shape handler there binds as a direct delegate"
            );
        }

        [Test]
        public void CompanionProbeVerifiesBinderShapeNotTheName()
        {
            MethodInfo method = typeof(BakeFixtureInaccessible).GetMethod(
                "PrivateCommand",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(method != null, "Sanity: expected the private cached-binder probe");

            Assert.IsFalse(
                CommandCompatibilityBake.IsRootedByGeneratedCode(
                    typeof(BakeFixtureInaccessible).Assembly,
                    typeof(BakeFixtureInaccessible),
                    method,
                    new Dictionary<Assembly, bool>()
                ),
                "A nested type that only borrows the companion name is not a "
                    + "companion: the handler still binds through a cached "
                    + "reflection-by-name binder and must be preserved"
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

        [Test]
        public void AccessibleInvalidSignatureHandlerIsNotRooted()
        {
            /*
                Accessibility alone does not root a handler: the catalog binds
                an invalid-signature one through the rejected-command accessor
                (string GetMethod), so stripping would take it and its
                diagnostics. The fixture is unattributed because a live
                rejected command queues readiness diagnostics on every shell;
                the rooting seam is the contract under test.
            */
            MethodInfo method = typeof(BakeFixtureInaccessible).GetMethod(
                nameof(BakeFixtureInaccessible.WrongParamCommand),
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            );
            Assert.That(method != null, "Sanity: expected the wrong-param probe");

            Assert.IsFalse(
                CommandCompatibilityBake.IsRootedByGeneratedCode(
                    typeof(BakeFixtureInaccessible).Assembly,
                    typeof(BakeFixtureInaccessible),
                    method,
                    new Dictionary<Assembly, bool>()
                ),
                "A handler the catalog cannot bind directly is reached by "
                    + "reflection-by-name and must be preserved, whatever its "
                    + "accessibility"
            );
        }

        [Test]
        public void GenericMethodInPartialTypeIsNotRootedDespiteCompanion()
        {
            MethodInfo method = typeof(BakePartialFixtureCommands).GetMethod(
                "PartialGenericCommand",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(method != null, "Sanity: expected the partial generic probe");

            Assert.IsFalse(
                CommandCompatibilityBake.IsRootedByGeneratedCode(
                    typeof(BakePartialFixtureCommands).Assembly,
                    typeof(BakePartialFixtureCommands),
                    method,
                    new Dictionary<Assembly, bool>()
                ),
                "Companions are per-method: a generic handler in a partial type "
                    + "binds by reflection-by-name and must be preserved even though "
                    + "the companion type exists for a sibling"
            );

            MethodInfo rooted = typeof(BakePartialFixtureCommands).GetMethod(
                "PartialPrivateCommand",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(rooted != null, "Sanity: expected the companion-rooted probe");
            Assert.IsTrue(
                CommandCompatibilityBake.IsRootedByGeneratedCode(
                    typeof(BakePartialFixtureCommands).Assembly,
                    typeof(BakePartialFixtureCommands),
                    rooted,
                    new Dictionary<Assembly, bool>()
                ),
                "The valid private handler is the one the companion roots"
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
                if (
                    ReferenceEquals(method.DeclaringType, holder)
                    && method.IsStatic
                    && method.IsDefined(typeof(RegisterCommandAttribute), false)
                )
                {
                    declared.Add(method);
                }
            }

            List<CommandCompatibilityBake.PreservationEntry> entries = Collect(declared.ToArray());

            Assert.AreEqual(
                1,
                entries.Count,
                "Exactly the attributed static handler in the catalog-less assembly is preserved"
            );
            Assert.AreEqual(
                "ProbePreservedCommand",
                entries[0].MethodName,
                "The static handler is reflection-bound and must be preserved"
            );
        }

        [Test]
        public void DirectBindableShapeMirrorsTheGenerator()
        {
            Type holder = CatalogLessAssembly.GetType(CatalogLessHolderTypeName);
            Assert.That(holder != null, "Sanity: expected the emitted command holder type");

            MethodInfo valid = holder.GetMethod("ProbePreservedCommand");
            MethodInfo nonVoid = holder.GetMethod("ProbeNonVoid");
            MethodInfo byRefParameter = holder.GetMethod("ProbeByRefParameter");
            MethodInfo generic = typeof(BakePartialFixtureCommands).GetMethod(
                "PartialGenericCommand",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            Assert.That(
                valid != null && nonVoid != null && byRefParameter != null && generic != null,
                "Sanity: expected the shape probes"
            );

            Assert.IsTrue(
                CommandCompatibilityBake.HasDirectBindableShape(valid),
                "Void + one by-value CommandArg[] parameter is the directly bindable shape"
            );
            Assert.IsFalse(
                CommandCompatibilityBake.HasDirectBindableShape(nonVoid),
                "A non-void return forces the cached reflection-by-name binder"
            );
            Assert.IsFalse(
                CommandCompatibilityBake.HasDirectBindableShape(byRefParameter),
                "A by-ref parameter type is not CommandArg[] by value"
            );
            Assert.IsFalse(
                CommandCompatibilityBake.HasDirectBindableShape(generic),
                "A generic method definition can never bind a closed delegate"
            );
        }

        [Test]
        public void AttributedCommandCollectionExcludesTestAssemblies()
        {
            List<CommandCompatibilityBake.AttributedCommand> commands =
                CommandCompatibilityBake.CollectAttributedCommands();

            foreach (CommandCompatibilityBake.AttributedCommand command in commands)
            {
                foreach (AssemblyName reference in command.Assembly.GetReferencedAssemblies())
                {
                    string name = reference.Name;
                    if (name == null)
                    {
                        continue;
                    }

                    Assert.IsFalse(
                        string.Equals(name, "UnityEngine.TestRunner", StringComparison.Ordinal)
                            || 0 <= name.IndexOf("nunit", StringComparison.OrdinalIgnoreCase),
                        $"{command.Assembly.GetName().Name} references {name}, so it "
                            + "never ships in players and must not enter the bake"
                    );
                }

                Assert.IsTrue(
                    command.Method.IsStatic,
                    "Only static handlers are discovered and preserved"
                );
            }
        }

        [Test]
        public void ManifestFileWritesUnderTemp()
        {
            Assert.IsTrue(
                CommandCompatibilityBake.TryBuildManifest(
                    new[]
                    {
                        new CommandCompatibilityBake.PreservationEntry(
                            "Assembly-A",
                            "Ns.Type",
                            "Handler"
                        ),
                    },
                    out string manifest
                ),
                "The preservation set produces a manifest"
            );

            Assert.IsTrue(
                CommandCompatibilityBake.TryWriteManifestFile(manifest, out string path),
                "The Temp manifest write succeeds"
            );
            Assert.IsTrue(File.Exists(path), "The manifest file exists at the returned Temp path");
            Assert.AreEqual(
                manifest,
                File.ReadAllText(path),
                "The written file carries the manifest byte-for-byte"
            );
            Assert.That(
                path.EndsWith(".link.xml", StringComparison.Ordinal),
                "The manifest file is named as a linker payload"
            );
            Assert.That(
                path.StartsWith(
                    Path.Combine(Directory.GetCurrentDirectory(), "Temp"),
                    StringComparison.Ordinal
                ),
                "The manifest is written under Temp, never under Assets"
            );
        }
#endif

        [Test]
        public void EmptyCommandListWritesNoManifest()
        {
            Assert.IsFalse(
                CommandCompatibilityBake.TryBuildManifest(
                    new List<CommandCompatibilityBake.PreservationEntry>(),
                    out string manifest
                ),
                "A build with nothing to preserve must not write a linker file"
            );
            Assert.That(
                manifest == null,
                "No manifest text is produced for an empty preservation set"
            );
        }

        [Test]
        public void ManifestIsDeterministicOrderedDeduplicatedAndEscaped()
        {
            Assert.IsTrue(
                CommandCompatibilityBake.TryBuildManifest(
                    new[]
                    {
                        new CommandCompatibilityBake.PreservationEntry("B", "T", "M"),
                        new CommandCompatibilityBake.PreservationEntry("A", "T2", "M2"),
                        new CommandCompatibilityBake.PreservationEntry("A", "T1", "M1"),
                        new CommandCompatibilityBake.PreservationEntry("A", "T1", "M1"),
                        new CommandCompatibilityBake.PreservationEntry("A", "T1", "Handler<1>&"),
                    },
                    out string manifest
                ),
                "The preservation set produces a manifest"
            );

            string expected =
                "<!-- "
                + CommandCompatibilityBake.OwnershipMarker
                + ": preserves [RegisterCommand] handlers that are bound by name "
                + "through reflection, which managed stripping cannot see. Written "
                + "under Temp per player build; safe to delete. -->\n"
                + "<linker>\n"
                + "  <assembly fullname=\"A\" ignoreIfMissing=\"1\">\n"
                + "    <type fullname=\"T1\" preserve=\"nothing\">\n"
                + "      <method name=\"Handler&lt;1&gt;&amp;\" />\n"
                + "      <method name=\"M1\" />\n"
                + "    </type>\n"
                + "    <type fullname=\"T2\" preserve=\"nothing\">\n"
                + "      <method name=\"M2\" />\n"
                + "    </type>\n"
                + "  </assembly>\n"
                + "  <assembly fullname=\"B\" ignoreIfMissing=\"1\">\n"
                + "    <type fullname=\"T\" preserve=\"nothing\">\n"
                + "      <method name=\"M\" />\n"
                + "    </type>\n"
                + "  </assembly>\n"
                + "</linker>\n";

            Assert.AreEqual(expected, manifest);
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
            /*
                Unattributed on purpose: an attributed invalid signature is a
                rejected command whose readiness diagnostics queue on every
                shell. The rooting seam test drives it directly instead.
            */
            public static void WrongParamCommand(string args) { }

            [RegisterCommand(
                Help = "Bake fixture rooted by a direct generated delegate.",
                Name = "bake-protected-internal"
            )]
            protected internal static void ProtectedInternalCommand(CommandArg[] args) { }

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
