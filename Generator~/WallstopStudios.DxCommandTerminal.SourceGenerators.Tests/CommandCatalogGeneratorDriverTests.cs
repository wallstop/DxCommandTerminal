namespace WallstopStudios.DxCommandTerminal.SourceGenerators.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Reflection;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Xunit;

    /*
        End-to-end tests for the shipped generator against the real runtime
        attribute and contract sources. Fixtures are compiled with the
        generator driver; every fixture needs the public
        WallstopStudios.DxCommandTerminal.Backend namespace to be present,
        which is asserted per fixture by the [RegisterCommand] usage inside
        it — visible in GeneratedCatalogCommands.txt in the generator's
        output folder for the shipped catalog.
     */
    public sealed class CommandCatalogGeneratorDriverTests
    {
        private const string StandardFixture =
            @"
namespace Fixtures
{
    using System.Collections.Generic;
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class Commands
    {
        public static int PublicInvocations;

        [RegisterCommand(Name = ""greet"", Help = ""Greets"", Hint = ""greet <name>"", MinArgCount = 1, MaxArgCount = 2, AddToHistory = false, EditorOnly = true, DevelopmentOnly = true)]
        public static void Greet(CommandArg[] args)
        {
            PublicInvocations++;
        }

        [RegisterCommand]
        private static void CommandSecret(CommandArg[] args)
        {
            SecretInvocations++;
        }

        public static int SecretInvocations;
    }
}";

        private const string ConditionalFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class ConditionalCommands
    {
#if UNITY_EDITOR
        [RegisterCommand] public static void EditorOnlyCommand(CommandArg[] args) { }
#endif

#if DEVELOPMENT_BUILD
        [RegisterCommand] public static void PlayerDevCommand(CommandArg[] args) { }
#endif

        [RegisterCommand] public static void AlwaysCommand(CommandArg[] args) { }
    }
}";

        private const string AliasFixture =
            @"
using RC = WallstopStudios.DxCommandTerminal.Attributes.RegisterCommandAttribute;

namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class AliasCommands
    {
        [RC] public static void AliasedCommand(CommandArg[] args) { }

        [WallstopStudios.DxCommandTerminal.Attributes.RegisterCommandAttribute]
        public static void QualifiedCommand(CommandArg[] args) { }
    }
}";

        private const string DeterminismFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class DeterministicCommands
    {
        [RegisterCommand(Help = ""First"")] public static void FirstCommand(CommandArg[] args) { }
        [RegisterCommand(Help = ""Second"")] public static void SecondCommand(CommandArg[] args) { }
        [RegisterCommand(Help = ""Third"")] public static void ThirdCommand(CommandArg[] args) { }
    }
}";

        private const string RejectionFixture =
            @"
namespace Fixtures
{
    using System.Collections.Generic;
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class InvalidCommands
    {
        [RegisterCommand(Help = ""broken"")]
        public static void BrokenCommand(int wrong, List<string> alsoWrong)
        {
            BrokenInvocations++;
        }

        public static int BrokenInvocations;

        [RegisterCommand]
        public static void ByRefCommand(ref CommandArg[] args)
        {
            ByRefInvocations++;
        }

        public static int ByRefInvocations;

        [RegisterCommand]
        public static void GenericCommand<T>(CommandArg[] args)
        {
            GenericInvocations++;
        }

        public static int GenericInvocations;
    }

    public class InstanceCommands
    {
        [RegisterCommand]
        public void InstanceCommand(CommandArg[] args)
        {
        }
    }

    public class OpenGenericCommands<T>
    {
        [RegisterCommand]
        public static void OpenTypeCommand(CommandArg[] args)
        {
        }
    }
}";

        private const string DefaultishFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class DefaultishCommands
    {
        [RegisterCommand(isDefault: true, Name = ""builtin-ish"")]
        public static void DefaultishCommand(CommandArg[] args)
        {
        }

        [RegisterCommand(isDefault: false, Help = ""explicitly-not-default"")]
        public static void NotDefaultCommand(CommandArg[] args)
        {
        }
    }
}";

        [Fact]
        public void RegistersExplicitAttributes()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("ExplicitAttributes", StandardFixture)
            );
            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);

            object first = catalog.Entries[0];
            Assert.Equal("greet", catalog.NameOf(first));
            Assert.Equal("Greet", catalog.MethodNameOf(first));
            Assert.Equal(1, catalog.MinArgCountOf(first));
            Assert.Equal(2, catalog.MaxArgCountOf(first));
            Assert.Equal("Greets", catalog.HelpOf(first));
            Assert.Equal("greet <name>", catalog.HintOf(first));
            Assert.False(catalog.AddToHistoryOf(first));
            Assert.True(catalog.EditorOnlyOf(first));
            Assert.True(catalog.DevelopmentOnlyOf(first));
            Assert.False(catalog.IsDefaultOf(first));
            Assert.True(catalog.HasValidSignature(first));
            Assert.Null(catalog.MethodAccessorOf(first));
            Assert.NotNull(catalog.BinderOf(first));

            object second = catalog.Entries[1];
            Assert.Equal("Secret", catalog.NameOf(second));
            Assert.Equal("CommandSecret", catalog.MethodNameOf(second));
            Assert.True(catalog.HasValidSignature(second));
            Assert.NotNull(catalog.BinderOf(second));
            Assert.Null(catalog.MethodAccessorOf(second));
        }

        [Fact]
        public void ExecutesPublicAndPrivateBinders()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation("BinderExecution", StandardFixture)
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Type commandsType = assembly.GetType("Fixtures.Commands");
            FieldInfo publicInvocations = commandsType.GetField("PublicInvocations");
            FieldInfo secretInvocations = commandsType.GetField("SecretInvocations");
            Assert.Equal(0, publicInvocations.GetValue(null));
            Assert.Equal(0, secretInvocations.GetValue(null));

            Type argType = assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg");

            // CommandArg is a value type, so its arrays cannot be cast to
            // object[]; the arguments are boxed inside a one-element object[].
            Array publicArgs = Array.CreateInstance(argType, 1);
            catalog.BinderOf(catalog.Entries[0])(new object[] { publicArgs });
            Assert.Equal(1, publicInvocations.GetValue(null));

            Array secretArgs = Array.CreateInstance(argType, 0);
            catalog.BinderOf(catalog.Entries[1])(new object[] { secretArgs });
            Assert.Equal(1, secretInvocations.GetValue(null));
        }

        [Fact]
        public void RespectsConditionalCompilation()
        {
            AssertConditional(0, 1);
            AssertConditional(1, 2);
            AssertConditional(2, 3);
        }

        private static void AssertConditional(int defineCount, int expectedEntryCount)
        {
            string[] defines =
                defineCount == 0 ? new string[0]
                : defineCount == 1 ? new[] { "UNITY_EDITOR" }
                : new[] { "UNITY_EDITOR", "DEVELOPMENT_BUILD" };

            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "ConditionalDefines" + defineCount,
                            ConditionalFixture,
                            defines
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(expectedEntryCount, catalog.Entries.Count);
            Assert.Contains(catalog.Entries, entry => catalog.NameOf(entry) == "Always");
        }

        [Fact]
        public void RegistersAliasesAndQualifiedAttributes()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(TestCompilationFactory.CreateCompilation("Aliases", AliasFixture))
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);
            Assert.Equal("Aliased", catalog.NameOf(catalog.Entries[0]));
            Assert.Equal("Qualified", catalog.NameOf(catalog.Entries[1]));
        }

        [Fact]
        public void GeneratedTextIsDeterministic()
        {
            string first = TestCompilationFactory
                .RunGenerator(
                    TestCompilationFactory.CreateCompilation("DeterminismA", DeterminismFixture)
                )
                .generated?.ToString();
            string second = TestCompilationFactory
                .RunGenerator(
                    TestCompilationFactory.CreateCompilation("DeterminismB", DeterminismFixture)
                )
                .generated?.ToString();

            Assert.Equal(first, second);
        }

        [Fact]
        public void EntryOrderMatchesDeclarationOrder()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation("EntryOrder", DeterminismFixture)
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(3, catalog.Entries.Count);
            Assert.Equal("First", catalog.NameOf(catalog.Entries[0]));
            Assert.Equal("Second", catalog.NameOf(catalog.Entries[1]));
            Assert.Equal("Third", catalog.NameOf(catalog.Entries[2]));
        }

        private const string NoCommandsFixture =
            @"
namespace Fixtures
{
    public static class PlainCommands
    {
        public static int NotACommand(int wrong)
        {
            return wrong;
        }
    }
}";

        [Fact]
        public void RegistersDefaultishInternalConstructor()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation("Defaultish", DefaultishFixture)
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);

            object defaultish = catalog.Entries[0];
            Assert.True(catalog.IsDefaultOf(defaultish));
            Assert.Equal("builtin-ish", catalog.NameOf(defaultish));
            Assert.True(catalog.HasValidSignature(defaultish));

            object notDefault = catalog.Entries[1];
            Assert.False(catalog.IsDefaultOf(notDefault));
            Assert.True(catalog.HasValidSignature(notDefault));
            Assert.NotNull(catalog.BinderOf(notDefault));
        }

        [Fact]
        public void RegistersCommandsWithExactSignatureAccessors()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation("ExactAccessors", RejectionFixture)
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(4, catalog.Entries.Count);

            object broken = catalog.Entries[0];
            Assert.False(catalog.HasValidSignature(broken));
            Assert.Equal("BrokenCommand", catalog.MethodNameOf(broken));
            MethodInfo brokenAccessor = catalog.MethodAccessorOf(broken);
            Assert.NotNull(brokenAccessor);
            Assert.Equal("BrokenCommand", brokenAccessor.Name);
            Assert.Equal(2, brokenAccessor.GetParameters().Length);
            Assert.Equal(typeof(int), brokenAccessor.GetParameters()[0].ParameterType);
            Assert.Equal("Broken", catalog.NameOf(broken));

            object byRef = catalog.Entries[1];
            Assert.False(catalog.HasValidSignature(byRef));
            MethodInfo byRefAccessor = catalog.MethodAccessorOf(byRef);
            Assert.NotNull(byRefAccessor);
            ParameterInfo byRefParameter = Assert.Single(byRefAccessor.GetParameters());
            Assert.True(byRefParameter.ParameterType.IsByRef);

            object generic = catalog.Entries[2];
            Assert.False(catalog.HasValidSignature(generic));
            MethodInfo genericAccessor = catalog.MethodAccessorOf(generic);
            Assert.NotNull(genericAccessor);
            Assert.True(genericAccessor.IsGenericMethod);
        }

        [Fact]
        public void SkipsInstanceCommandsAndOpenGenericContainers()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation("Skips", RejectionFixture)
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            List<string> names = new List<string>();
            foreach (object entry in catalog.Entries)
            {
                names.Add(catalog.NameOf(entry));
            }

            Assert.DoesNotContain("Instance", names);
            Assert.Contains("OpenType", names);
        }

        [Fact]
        public void SkipsCommandsWhenCommandCatalogEntryIsAbsent()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation(
                    "NoContract",
                    StandardFixture,
                    includeCommandCatalogEntry: false
                )
            );

            Assert.Null(generated);
            Assert.Null(
                output.SyntaxTrees.FirstOrDefault(tree =>
                    tree.FilePath == TestCompilationFactory.GeneratedHintName
                )
            );
        }

        [Fact]
        public void SkipsCommandsWhenNoCommandsArePresent()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("NoCommands", NoCommandsFixture)
            );

            Assert.Null(generated);
            Assert.Null(
                output.SyntaxTrees.FirstOrDefault(tree =>
                    tree.FilePath == TestCompilationFactory.GeneratedHintName
                )
            );
        }

        [Fact]
        public void SkipsCommandsWhenFixtureIsEmpty()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("EmptyFixture", string.Empty)
            );

            Assert.Null(generated);
            Assert.Null(
                output.SyntaxTrees.FirstOrDefault(tree =>
                    tree.FilePath == TestCompilationFactory.GeneratedHintName
                )
            );
        }
    }
}
