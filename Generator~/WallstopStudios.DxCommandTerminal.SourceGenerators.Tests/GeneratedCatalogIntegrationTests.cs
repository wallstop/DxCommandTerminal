namespace WallstopStudios.DxCommandTerminal.SourceGenerators.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;
    using Xunit;

    /*
        Fixture commands compiled INTO this test assembly. The shipped
        analyzer (referenced through the <Analyzer> item) generates this
        assembly's catalog at build time, so these tests validate the exact
        binary Unity loads — not a fresh rebuild of the sources beside it.
     */
    public static class CatalogFixtureCommands
    {
        public static int PublicInvocations;
        public static int SecretInvocations;

        [RegisterCommand(
            Name = "fixture-public",
            Help = "Public fixture command",
            MinArgCount = 0,
            MaxArgCount = 1
        )]
        public static void FixturePublicCommand(CommandArg[] args)
        {
            PublicInvocations++;
        }

        [RegisterCommand(Help = "Invalid signature")]
        public static void FixtureBrokenCommand(int wrong, List<string> alsoWrong) { }

        [RegisterCommand]
        public static void FixtureGenericCommand<T>(CommandArg[] args) { }

        [RegisterCommand]
        private static void CommandSecret(CommandArg[] args)
        {
            SecretInvocations++;
        }
    }

    public sealed class GeneratedCatalogIntegrationTests
    {
        private const string CatalogTypeName =
            "WallstopStudios.DxCommandTerminal.Generated.CommandCatalog";

        /*
            The reflection oracle: what the compatibility discovery path sees
            for this assembly's declared commands, one entry per attributed
            static method.
         */
        private static List<(
            MethodInfo method,
            RegisterCommandAttribute attribute
        )> CollectLegacyAttributes()
        {
            List<(MethodInfo, RegisterCommandAttribute)> legacy =
                new List<(MethodInfo, RegisterCommandAttribute)>();
            const BindingFlags discoveryFlags =
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (MethodInfo method in typeof(CatalogFixtureCommands).GetMethods(discoveryFlags))
            {
                RegisterCommandAttribute attribute =
                    method.GetCustomAttribute<RegisterCommandAttribute>(inherit: false);
                if (attribute == null)
                {
                    continue;
                }

                attribute.NormalizeName(method);
                legacy.Add((method, attribute));
            }

            return legacy;
        }

        private static Type BindCatalogType()
        {
            Type catalogType = typeof(GeneratedCatalogIntegrationTests).Assembly.GetType(
                CatalogTypeName,
                false
            );
            Assert.NotNull(catalogType);
            return catalogType;
        }

        private static List<CommandCatalogEntry> Collect()
        {
            MethodInfo collect = BindCatalogType()
                .GetMethod("Collect", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(collect);
            List<CommandCatalogEntry> entries = new List<CommandCatalogEntry>();
            collect.Invoke(null, new object[] { entries });
            return entries;
        }

        [Fact]
        public void TestAssemblyHasGeneratedCatalog()
        {
            Assert.NotNull(BindCatalogType());
        }

        [Fact]
        public void CollectsOneEntryPerDeclaredCommand()
        {
            List<CommandCatalogEntry> entries = Collect();
            List<(MethodInfo, RegisterCommandAttribute)> legacy = CollectLegacyAttributes();

            Assert.Equal(legacy.Count, entries.Count);
            Assert.Equal(4, entries.Count);
        }

        [Fact]
        public void EntryNamesMatchLegacyAttributeNormalization()
        {
            Dictionary<string, string> legacyNames = new Dictionary<string, string>(
                StringComparer.Ordinal
            );
            foreach (
                (MethodInfo method, RegisterCommandAttribute attribute) in CollectLegacyAttributes()
            )
            {
                legacyNames[method.Name] = attribute.Name;
            }

            foreach (CommandCatalogEntry entry in Collect())
            {
                Assert.True(
                    legacyNames.TryGetValue(entry.MethodName, out string legacyName),
                    $"Method {entry.MethodName} has no legacy-discovered counterpart."
                );
                Assert.Equal(legacyName, entry.Name);
            }
        }

        [Fact]
        public void EntryMetadataMatchesLegacyAttributes()
        {
            Dictionary<string, RegisterCommandAttribute> legacy = new Dictionary<
                string,
                RegisterCommandAttribute
            >(StringComparer.Ordinal);
            foreach (
                (MethodInfo method, RegisterCommandAttribute attribute) in CollectLegacyAttributes()
            )
            {
                legacy[method.Name] = attribute;
            }

            foreach (CommandCatalogEntry entry in Collect())
            {
                RegisterCommandAttribute attribute = legacy[entry.MethodName];
                Assert.Equal(attribute.MinArgCount, entry.MinArgCount);
                Assert.Equal(attribute.MaxArgCount, entry.MaxArgCount);
                Assert.Equal(attribute.Help, entry.Help);
                Assert.Equal(attribute.Hint, entry.Hint);
                Assert.Equal(attribute.AddToHistory, entry.AddToHistory);
                Assert.Equal(attribute.EditorOnly, entry.EditorOnly);
                Assert.Equal(attribute.DevelopmentOnly, entry.DevelopmentOnly);
            }
        }

        [Fact]
        public void RejectedSignaturesResolveTheSameMethodAsLegacyReflection()
        {
            Dictionary<string, MethodInfo> legacy = new Dictionary<string, MethodInfo>(
                StringComparer.Ordinal
            );
            foreach (
                (MethodInfo method, RegisterCommandAttribute attribute) in CollectLegacyAttributes()
            )
            {
                bool validSignature =
                    method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(CommandArg[])
                    && !method.IsGenericMethod;
                if (!validSignature)
                {
                    legacy[attribute.Name] = method;
                }
            }

            Assert.Equal(2, legacy.Count);

            foreach (CommandCatalogEntry entry in Collect())
            {
                if (entry.IsValid)
                {
                    Assert.Null(entry.MethodAccessor);
                    continue;
                }

                MethodInfo accessorMethod = entry.MethodAccessor();
                Assert.NotNull(accessorMethod);
                Assert.True(
                    legacy.TryGetValue(entry.Name, out MethodInfo legacyMethod),
                    $"Rejected command {entry.Name} was not discovered by the reflection oracle."
                );
                Assert.Equal(legacyMethod.Name, accessorMethod.Name);
                ParameterInfo[] accessorParameters = accessorMethod.GetParameters();
                ParameterInfo[] legacyParameters = legacyMethod.GetParameters();
                Assert.Equal(legacyParameters.Length, accessorParameters.Length);
                for (int i = 0; i < accessorParameters.Length; i++)
                {
                    Assert.Equal(
                        legacyParameters[i].ParameterType,
                        accessorParameters[i].ParameterType
                    );
                }
            }
        }

        [Fact]
        public void BindersExecutePublicAndPrivateCommands()
        {
            List<CommandCatalogEntry> entries = Collect();
            CommandCatalogEntry publicEntry = entries.Single(entry =>
                entry.Name == "fixture-public"
            );
            CommandCatalogEntry secretEntry = entries.Single(entry => entry.Name == "Secret");

            Assert.Equal(0, CatalogFixtureCommands.PublicInvocations);
            Assert.Equal(0, CatalogFixtureCommands.SecretInvocations);

            publicEntry.Binder()(new CommandArg[] { new CommandArg("ignored") });
            Assert.Equal(1, CatalogFixtureCommands.PublicInvocations);

            secretEntry.Binder()(new CommandArg[] { });
            Assert.Equal(1, CatalogFixtureCommands.SecretInvocations);

            // Binders are cached per command; repeated collection and binding
            // reuse the same delegate without re-resolving reflection.
            Assert.Equal(1, Collect().Count(entry => entry.Name == "Secret"));
            secretEntry.Binder()(new CommandArg[] { });
            Assert.Equal(2, CatalogFixtureCommands.SecretInvocations);
        }
    }
}
