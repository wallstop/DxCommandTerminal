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

        private const string PositionalNameFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class PositionalCommands
    {
        [RegisterCommand(""positional-name"")]
        public static void PositionalCommand(CommandArg[] args)
        {
        }
    }
}";

        private const string KeywordMethodFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class KeywordCommands
    {
        public static int KeywordInvocations;

        [RegisterCommand]
        public static void @params(CommandArg[] args)
        {
            KeywordInvocations++;
        }

        [RegisterCommand(Help = ""keyword void"")]
        private static void @void(CommandArg[] args)
        {
            KeywordInvocations += 10;
        }
    }
}";

        private const string BlankNameFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class BlankNameCommands
    {
        [RegisterCommand]
        public static void Command(CommandArg[] args)
        {
        }
    }
}";

        private const string NonVoidHandlerFixture =
            @"
namespace Fixtures
{
    using System.Threading.Tasks;
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class NonVoidCommands
    {
        [RegisterCommand(Help = ""returns a value"")]
        public static int NonVoidValue(CommandArg[] args)
        {
            return 0;
        }

        [RegisterCommand]
        public static Task NonVoidTask(CommandArg[] args)
        {
            return Task.CompletedTask;
        }

        [RegisterCommand]
        public static void VoidCommand(CommandArg[] args)
        {
        }
    }
}";

        private const string InaccessibleHolderFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public class CommandHost
    {
        private static class PrivateHolder
        {
            [RegisterCommand(Help = ""private nested holder"")]
            public static void Hidden(CommandArg[] args)
            {
            }
        }
    }
}";

        private const string InaccessibleMixedFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class VisibleCommands
    {
        [RegisterCommand(Help = ""visible"")]
        public static void Visible(CommandArg[] args)
        {
        }
    }

    public class CommandHost
    {
        private static class PrivateHolder
        {
            [RegisterCommand]
            public static void Hidden(CommandArg[] args)
            {
            }
        }
    }
}";

        private const string KeywordNamedHolderFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class @object
    {
        [RegisterCommand(Help = ""holder named after a keyword"")]
        public static void Run(CommandArg[] args)
        {
        }
    }
}";

        private const string PartialMethodFixture =
            @"
namespace Fixtures
{
    using System;
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static partial class PartialCommands
    {
        [RegisterCommand]
        public static partial void PartialCommand(CommandArg[] args);
    }

    public static partial class PartialCommands
    {
        // Both parts carry attribute lists, but only the declaration carries
        // RegisterCommand; the receiver must not emit the merged symbol twice.
        [Obsolete(""marker"")]
        public static partial void PartialCommand(CommandArg[] args)
        {
        }
    }
}";

        private const string DuplicateNameFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class DuplicateCommandsA
    {
        [RegisterCommand]
        public static void CommandHeal(CommandArg[] args)
        {
        }
    }

    public static class DuplicateCommandsB
    {
        [RegisterCommand]
        public static void CommandHeal(CommandArg[] args)
        {
        }
    }
}";

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

        [Fact]
        public void RegistersPositionalConstructorNames()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "PositionalName",
                            PositionalNameFixture
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal("positional-name", catalog.NameOf(entry));
            Assert.True(catalog.HasValidSignature(entry));
            Assert.NotNull(catalog.BinderOf(entry));
        }

        [Fact]
        public void EmitsCompilableCodeForKeywordMethodNames()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "KeywordMethods",
                            KeywordMethodFixture
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);
            Assert.Equal("params", catalog.NameOf(catalog.Entries[0]));
            Assert.Equal("params", catalog.MethodNameOf(catalog.Entries[0]));
            Assert.Equal("void", catalog.NameOf(catalog.Entries[1]));

            FieldInfo invocations = assembly
                .GetType("Fixtures.KeywordCommands")
                .GetField("KeywordInvocations");
            Assert.Equal(0, invocations.GetValue(null));
            catalog.BinderOf(catalog.Entries[0])(
                new object[]
                {
                    Array.CreateInstance(
                        assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"),
                        0
                    ),
                }
            );
            Assert.Equal(1, invocations.GetValue(null));
            catalog.BinderOf(catalog.Entries[1])(
                new object[]
                {
                    Array.CreateInstance(
                        assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"),
                        0
                    ),
                }
            );
            Assert.Equal(11, invocations.GetValue(null));
        }

        [Fact]
        public void BlankInferredNamesAreEmittedAndRejectedLikeLegacy()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation("BlankName", BlankNameFixture)
                    )
                    .output
            );

            // The catalog is loadable: a blank inferred name must not poison
            // the assembly's static initializer.
            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal(string.Empty, catalog.NameOf(entry));
            Assert.True(catalog.HasValidSignature(entry));
        }

        [Fact]
        public void PartialMethodDeclarationsProduceOneEntry()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "PartialMethods",
                            PartialMethodFixture
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal("Partial", catalog.NameOf(entry));
        }

        [Fact]
        public void DuplicateInferredNamesEachProduceAnEntry()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "DuplicateNames",
                            DuplicateNameFixture
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);
            Assert.All(catalog.Entries, entry => Assert.Equal("Heal", catalog.NameOf(entry)));
        }

        [Fact]
        public void NonVoidHandlerEmitCachedBinderThatThrowsLikeReflection()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "NonVoidHandlers",
                            NonVoidHandlerFixture
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object value = catalog.Entries.Single(entry => catalog.NameOf(entry) == "NonVoidValue");
            Assert.True(catalog.HasValidSignature(value));
            object[] args =
            {
                Array.CreateInstance(
                    assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"),
                    0
                ),
            };
            Assert.ThrowsAny<Exception>(() => catalog.BinderOf(value)(args));
        }

        [Fact]
        public void NonVoidTaskHandlerEmitCachedBinderThatThrowsLikeReflection()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "NonVoidHandlersTask",
                            NonVoidHandlerFixture
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object task = catalog.Entries.Single(entry => catalog.NameOf(entry) == "NonVoidTask");
            object[] args =
            {
                Array.CreateInstance(
                    assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"),
                    0
                ),
            };
            Assert.ThrowsAny<Exception>(() => catalog.BinderOf(task)(args));
        }

        [Fact]
        public void NonVoidFixtureKeepsVoidHandlerDirectlyBindable()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "NonVoidHandlersVoid",
                            NonVoidHandlerFixture
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object voidCommand = catalog.Entries.Single(entry => catalog.NameOf(entry) == "Void");
            Assert.True(catalog.HasValidSignature(voidCommand));
            object[] args =
            {
                Array.CreateInstance(
                    assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"),
                    0
                ),
            };
            catalog.BinderOf(voidCommand)(args);
        }

        [Fact]
        public void InaccessibleHoldersSkipTheCatalogForTheWholeAssembly()
        {
            (SyntaxTree generated, _) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation(
                    "InaccessibleHolder",
                    InaccessibleHolderFixture
                )
            );

            Assert.Null(generated);
        }

        [Fact]
        public void InaccessibleHoldersForceReflectionEvenWhenOtherCommandsExist()
        {
            (SyntaxTree generated, _) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation(
                    "InaccessibleMixed",
                    InaccessibleMixedFixture
                )
            );

            Assert.Null(generated);
        }

        [Fact]
        public void KeywordNamedHoldersEmitCompilableCatalogs()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory
                    .RunGenerator(
                        TestCompilationFactory.CreateCompilation(
                            "KeywordHolder",
                            KeywordNamedHolderFixture
                        )
                    )
                    .output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal("Run", catalog.NameOf(entry));
            Assert.True(catalog.HasValidSignature(entry));
        }
    }
}
