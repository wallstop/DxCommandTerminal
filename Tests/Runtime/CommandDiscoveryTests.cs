namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using Attributes;
    using Backend;
    using NUnit.Framework;

    public sealed class CommandDiscoveryTests
    {
        private const BindingFlags DiscoveryFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

#if UNITY_EDITOR
        /*
           Defined once: dynamic assemblies are non-collectible, and IL2CPP
           players do not support Reflection.Emit, so this case is editor-only.
        */
        private static readonly Assembly DynamicAssembly = CreateDynamicAssembly();
#endif

        /*
           Declared in this test assembly (a consumer-style assembly that only
           references the runtime) so discovery from non-builtin assemblies is
           pinned. Names are normalized the same way production discovery does.
        */
        [RegisterCommand(Help = "Test command with an inferred name.")]
        private static void TestCommand(CommandArg[] args) { }

        [RegisterCommand(Help = "Test command with an explicit name.", Name = "discovery-named")]
        private static void TestDiscoveryNamed(CommandArg[] args) { }

        private static IEnumerable<TestCaseData> AssemblyFilterCases()
        {
            Assembly testAssembly = typeof(CommandDiscoveryTests).Assembly;
            // The core assembly never references the terminal package.
            Assembly coreAssembly = typeof(int).Assembly;

            /*
               Note: the runtime assembly itself is not classified by
               MayContainCommands (production scans it last via a name match);
               that ordering is pinned by the equivalence sweep below.
            */
            yield return new TestCaseData(testAssembly, true).SetName(
                "testAssemblyMayContainCommands"
            );
            yield return new TestCaseData(coreAssembly, false).SetName(
                "coreAssemblyMayNotContainCommands"
            );
#if UNITY_EDITOR
            yield return new TestCaseData(DynamicAssembly, false).SetName(
                "dynamicAssemblyMayNotContainCommands"
            );
#endif
        }

        private static string Describe(
            Assembly assembly,
            Type type,
            MethodInfo method,
            RegisterCommandAttribute attribute
        )
        {
            return $"{assembly.GetName().Name}:{type.FullName}:{method.Name}:{attribute.Name}";
        }

        private static Assembly CreateDynamicAssembly()
        {
            AssemblyName name = new($"DiscoveryTestDynamic-{Guid.NewGuid():N}");
            return AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        }

        [Test]
        [TestCaseSource(nameof(AssemblyFilterCases))]
        public void MayContainCommandsFiltersAssemblies(Assembly assembly, bool expected)
        {
            AssemblyName self = typeof(BuiltInCommands).Assembly.GetName();
            Assert.AreEqual(
                expected,
                CommandShell.MayContainCommands(assembly, self),
                $"Unexpected filter decision for assembly '{assembly.FullName}'"
            );
        }

        [Test]
        public void DiscoversCommandsFromConsumerAssemblies()
        {
            (MethodInfo method, RegisterCommandAttribute attribute)[] discovered = CommandShell
                .RegisteredCommands
                .Value;

            /*
               Command names are matched OrdinalIgnoreCase by the shell, so the
               inferred name may preserve the declaring method's casing.
            */
            RegisterCommandAttribute inferred = discovered
                .Select(tuple => tuple.attribute)
                .FirstOrDefault(attribute =>
                    string.Equals(attribute.Name, "test", StringComparison.OrdinalIgnoreCase)
                );
            Assert.IsNotNull(
                inferred,
                "Expected the inferred-name command from this test assembly to be discovered"
            );
            Assert.AreEqual(
                0,
                inferred.MinArgCount,
                "Attribute bounds should carry through discovery"
            );

            RegisterCommandAttribute named = discovered
                .Select(tuple => tuple.attribute)
                .FirstOrDefault(attribute => attribute.Name == "discovery-named");
            Assert.IsNotNull(
                named,
                "Expected the explicit-name command from this test assembly to be discovered"
            );
        }

        [Test]
        public void FilteredDiscoveryMatchesUnfilteredDiscovery()
        {
            /*
               The legacy discovery algorithm: scan every assembly in the
               domain, materializing attributes per method. This equivalence
               sweep pins that the assembly-reference filter never drops a
               command the legacy path would have found.
            */
            List<string> legacy = new();
            Assembly ourAssembly = typeof(BuiltInCommands).Assembly;
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            IEnumerable<Assembly> legacyOrder = loadedAssemblies
                .Where(assembly => !ReferenceEquals(assembly, ourAssembly))
                .Append(ourAssembly);

            foreach (Assembly assembly in legacyOrder)
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch (Exception)
                {
                    /*
                       Mirrors production TryGetScanTypes: an assembly that
                       cannot be enumerated contributes no commands.
                    */
                    continue;
                }

                foreach (Type type in types.Where(type => type != null))
                {
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(DiscoveryFlags);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    foreach (MethodInfo method in methods)
                    {
                        RegisterCommandAttribute attribute;
                        try
                        {
                            attribute =
                                Attribute.GetCustomAttribute(
                                    method,
                                    typeof(RegisterCommandAttribute)
                                ) as RegisterCommandAttribute;
                        }
                        catch (Exception)
                        {
                            /*
                               Mirrors production: attribute resolution failures
                               are contained, not fatal.
                            */
                            continue;
                        }

                        if (attribute == null)
                        {
                            continue;
                        }

                        attribute.NormalizeName(method);
                        legacy.Add(Describe(assembly, type, method, attribute));
                    }
                }
            }

            (MethodInfo method, RegisterCommandAttribute attribute)[] filtered = CommandShell
                .RegisteredCommands
                .Value;
            List<string> actual = filtered
                .Select(tuple =>
                {
                    Type declaringType = tuple.method.DeclaringType;
                    return Describe(
                        declaringType.Assembly,
                        declaringType,
                        tuple.method,
                        tuple.attribute
                    );
                })
                .ToList();

            Assert.AreEqual(
                string.Join("\n", legacy.OrderBy(name => name, StringComparer.Ordinal)),
                string.Join("\n", actual.OrderBy(name => name, StringComparer.Ordinal)),
                "Filtered discovery diverged from the legacy full-domain scan"
            );
        }
    }
}
