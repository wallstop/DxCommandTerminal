namespace WallstopStudios.DxCommandTerminal.SourceGenerators.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
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

        private const string NestedHolderFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class Holders
    {
        public static class Inner
        {
            public static int Invocations;

            [RegisterCommand]
            public static void NestedCommand(CommandArg[] args)
            {
                Invocations++;
            }
        }
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

        private const string PartialPrivateCommandFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static partial class PartialPrivateCommands
    {
        public static int SecretInvocations;

        [RegisterCommand(Help = ""private command in a partial holder"")]
        private static void CommandSecret(CommandArg[] args)
        {
            SecretInvocations++;
        }
    }
}";

        private const string PartialPrivateNestedCommandFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static partial class PartialOuter
    {
        public static partial class PartialInner
        {
            public static int SecretInvocations;

            [RegisterCommand(Help = ""private command in a nested partial holder"")]
            private static void CommandSecret(CommandArg[] args)
            {
                SecretInvocations++;
            }
        }
    }
}";

        private const string PartialStructPrivateCommandFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public partial struct PartialStructCommands
    {
        public static int SecretInvocations;

        [RegisterCommand(Help = ""private command in a partial struct"")]
        private static void CommandSecret(CommandArg[] args)
        {
            SecretInvocations++;
        }
    }
}";

        private const string PartialMixedCommandsFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static partial class PartialMixedCommands
    {
        public static int Invocations;

        [RegisterCommand(Help = ""public command"")]
        public static void Public(CommandArg[] args)
        {
            Invocations++;
        }

        [RegisterCommand(Help = ""private command one"")]
        private static void CommandFirst(CommandArg[] args)
        {
            Invocations++;
        }

        [RegisterCommand(Help = ""private command two"")]
        private static void CommandSecond(CommandArg[] args)
        {
            Invocations++;
        }
    }

    public static partial class OtherPartialCommands
    {
        [RegisterCommand(Help = ""other private command"")]
        private static void CommandOther(CommandArg[] args)
        {
        }
    }
}";

        private const string KeywordNamedPartialHolderFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static partial class @object
    {
        public static int SecretInvocations;

        [RegisterCommand(Help = ""private command in a keyword-named holder"")]
        private static void CommandSecret(CommandArg[] args)
        {
            SecretInvocations++;
        }
    }
}";

        private const string GenericPartialHolderFixture =
            @"
namespace Fixtures
{
    using System;
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static partial class GenericPartialHolder<T>
    {
        [RegisterCommand]
        private static void CommandSecret(CommandArg[] args)
        {
        }
    }
}";

        private const string FileLocalPartialHolderFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    file static partial class FileLocalCommands
    {
        [RegisterCommand]
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

        private const string ContextsFixture =
            @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static class ContextCommands
    {
        [RegisterCommand(
            Name = ""edit-mode-only"",
            Contexts = CommandExecutionContexts.EditorEditMode
        )]
        public static void EditModeOnly(CommandArg[] args)
        {
        }

        [RegisterCommand(
            Name = ""gameplay-only"",
            Contexts = CommandExecutionContexts.EditorPlayMode
                | CommandExecutionContexts.Player
        )]
        public static void GameplayOnly(CommandArg[] args)
        {
        }

        [RegisterCommand(Name = ""unrestricted"")]
        public static void Unrestricted(CommandArg[] args)
        {
        }

        [RegisterCommand(Name = ""numeric-contexts"", Contexts = (CommandExecutionContexts)5)]
        public static void NumericContexts(CommandArg[] args)
        {
        }
    }
}";

        private static void AssertConditional(int defineCount, int expectedEntryCount)
        {
            string[] defines =
                defineCount == 0 ? Array.Empty<string>()
                : defineCount == 1 ? new[] { "UNITY_EDITOR" }
                : new[] { "UNITY_EDITOR", "DEVELOPMENT_BUILD" };

            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("ConditionalDefines" + defineCount, ConditionalFixture, defines)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(expectedEntryCount, catalog.Entries.Count);
            Assert.Contains(catalog.Entries, entry => catalog.NameOf(entry) == "Always");
        }

        [Fact]
        public void SharedConstructionTablesRequireLongNullMetadataRunsAndPreserveEntries()
        {
            const int count = 112;
            StringBuilder fixture = new StringBuilder();
            fixture.AppendLine("namespace Fixtures { using WallstopStudios.DxCommandTerminal.Attributes; using WallstopStudios.DxCommandTerminal.Backend;");
            fixture.AppendLine("public static class TableCommands { public static int[] Calls = new int[112];");
            string[] expectedHelp = new string[count];
            string[] expectedHint = new string[count];
            int[] groups = new int[count];
            for (int index = 0; index < count; index++)
            {
                int group =
                    index < 15 ? 0
                    : index < 31 ? 1
                    : index < 48 ? 2
                    : 3;
                groups[index] = group;
                string suffix = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                if (group == 2)
                {
                    expectedHelp[index] =
                        index % 3 == 0 ? null
                        : index % 3 == 1 ? string.Empty
                        : "quote \" slash \\ newline\n NUL\0 Ω 😀";
                    expectedHint[index] =
                        expectedHelp[index] == null ? string.Empty
                        : index % 2 == 0 ? "hint\t\"\\\0 Ω"
                        : null;
                }
                fixture
                    .Append(group == 2 ? "[RegisterCommand(true, Name = \"table" : "[RegisterCommand(Name = \"table")
                    .Append(suffix)
                    .Append("\", Help = ")
                    .Append(expectedHelp[index] == null ? "null" : SymbolDisplay.FormatLiteral(expectedHelp[index], true))
                    .Append(", Hint = ")
                    .Append(expectedHint[index] == null ? "null" : SymbolDisplay.FormatLiteral(expectedHint[index], true))
                    .Append(", MinArgCount = ")
                    .Append(group)
                    .Append(", MaxArgCount = ")
                    .Append(group + 4)
                    .Append(", AddToHistory = ")
                    .Append(group == 3 ? "false" : "true")
                    .Append(", EditorOnly = ")
                    .Append(group == 1 ? "true" : "false")
                    .Append(", DevelopmentOnly = ")
                    .Append(group == 2 ? "true" : "false")
                    .Append(", Contexts = (CommandExecutionContexts)(")
                    .Append(group - 17)
                    .Append("))] ")
                    .Append("public static void Command")
                    .Append(suffix)
                    .Append("(CommandArg[] arguments) { Calls[")
                    .Append(index)
                    .AppendLine("]++; }");
            }
            fixture.AppendLine("} }");
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("ConstructionTables", fixture.ToString())
            );
            List<VariableDeclaratorSyntax> tables = generated
                .GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(variable =>
                    string.Equals(variable.Identifier.ValueText, "metadata", StringComparison.Ordinal)
                    && variable.Initializer.Value is LiteralExpressionSyntax literal
                    && literal.IsKind(SyntaxKind.StringLiteralExpression)
                )
                .ToList();
            Assert.Equal(3, tables.Count);
            string[][] fields = tables.Select(table => ((LiteralExpressionSyntax)table.Initializer.Value).Token.ValueText.Split('\0')).ToArray();
            Assert.Equal(32, fields[0].Length);
            Assert.Equal(32, fields[1].Length);
            Assert.Equal(96, fields[2].Length);
            Assert.Equal("table0015", fields[0][0]);
            Assert.Equal("table0048", fields[1][0]);
            Assert.Equal("table0064", fields[2][0]);
            Assert.All(
                tables,
                table =>
                {
                    BlockSyntax block = table.Ancestors().OfType<BlockSyntax>().First();
                    ForStatementSyntax loop = Assert.Single(block.Statements.OfType<ForStatementSyntax>());
                    InvocationExpressionSyntax create = Assert.Single(
                        loop.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(call => call.Expression.ToString().StartsWith("CreateEntry", StringComparison.Ordinal))
                    );
                    Assert.True(create.ArgumentList.Arguments[2].Expression.IsKind(SyntaxKind.NullLiteralExpression));
                    Assert.True(create.ArgumentList.Arguments[3].Expression.IsKind(SyntaxKind.NullLiteralExpression));
                    string[] values = ((LiteralExpressionSyntax)table.Initializer.Value).Token.ValueText.Split('\0');
                    BinaryExpressionSyntax condition = Assert.IsType<BinaryExpressionSyntax>(loop.Condition);
                    Assert.Equal(values.Length / 2, (int)((LiteralExpressionSyntax)condition.Right).Token.Value);
                    for (int offset = 0; offset < values.Length; offset += 2)
                    {
                        Assert.Equal(string.Concat("Command", values[offset].AsSpan("table".Length)), values[offset + 1]);
                    }
                }
            );
            Assert.Equal(
                35,
                generated
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Count(call => string.Equals(call.Expression.ToString(), "entries.Add", StringComparison.Ordinal))
            );
            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            CatalogView repeated = CatalogView.Load(assembly);
            Assert.Equal(count, catalog.Entries.Count);
            Assert.Equal(count, repeated.Entries.Count);
            FieldInfo cachedHandler = catalog.Entries[0].GetType().GetField("_boundHandler", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] arguments = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            for (int index = 0; index < count; index++)
            {
                object entry = catalog.Entries[index];
                int group = groups[index];
                string suffix = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Same(entry, repeated.Entries[index]);
                Assert.Equal("table" + suffix, catalog.NameOf(entry));
                Assert.Equal("Command" + suffix, catalog.MethodNameOf(entry));
                Assert.Equal(expectedHelp[index], catalog.HelpOf(entry));
                Assert.Equal(expectedHint[index], catalog.HintOf(entry));
                Assert.Equal(group, catalog.MinArgCountOf(entry));
                Assert.Equal(group + 4, catalog.MaxArgCountOf(entry));
                Assert.Equal(group != 3, catalog.AddToHistoryOf(entry));
                Assert.Equal(group == 1, catalog.EditorOnlyOf(entry));
                Assert.Equal(group == 2, catalog.DevelopmentOnlyOf(entry));
                Assert.Equal(group == 2, catalog.IsDefaultOf(entry));
                Assert.Equal(group - 17, catalog.ContextsOf(entry));
                Assert.Equal("Fixtures.TableCommands", catalog.DeclaringTypeNameOf(entry));
                Assert.Null(cachedHandler.GetValue(entry));
                (object first, object second) = catalog.GetHandlerPairOf(entry);
                Assert.Same(first, second);
                Assert.Same(first, cachedHandler.GetValue(entry));
                Delegate handler = (Delegate)first;
                Assert.Null(handler.Target);
                Assert.True(handler.Method.IsStatic);
                Assert.Equal("Command" + suffix, handler.Method.Name);
                Assert.Equal("Fixtures.TableCommands", handler.Method.DeclaringType.FullName);
                handler.DynamicInvoke(arguments);
            }
            int[] calls = (int[])assembly.GetType("Fixtures.TableCommands").GetField("Calls").GetValue(null);
            Assert.All(calls, value => Assert.Equal(1, value));
        }

        [Fact]
        public void PackedConstructionPreservesNulFallbackEmptyNamesAndUnicode()
        {
            const int count = 128;
            StringBuilder fixture = new StringBuilder();
            fixture.AppendLine("namespace Fixtures { using WallstopStudios.DxCommandTerminal.Attributes; using WallstopStudios.DxCommandTerminal.Backend;");
            fixture.AppendLine("public static class PackedCommands { public static int[] Calls = new int[128];");
            string[] expectedNames = new string[count];
            string[] expectedMethods = new string[count];
            for (int index = 0; index < count; index++)
            {
                string suffix = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                expectedNames[index] =
                    index == 8 ? "packed0008\0tail"
                    : index == 64 ? string.Empty
                    : index == 65 ? "packed0065Ω😀quote\"slash\\newline\nend"
                    : "packed" + suffix;
                expectedMethods[index] =
                    index == 64 ? "Command"
                    : index == 66 ? "CommandΩ"
                    : "Command" + suffix;
                fixture.Append("[RegisterCommand(");
                if (index != 64)
                {
                    fixture.Append("Name = ").Append(SymbolDisplay.FormatLiteral(expectedNames[index], true)).Append(", ");
                }
                fixture
                    .Append("MinArgCount = 0, MaxArgCount = 0)] public static void ")
                    .Append(expectedMethods[index])
                    .Append("(CommandArg[] arguments) { Calls[")
                    .Append(index)
                    .AppendLine("]++; }");
            }
            fixture.AppendLine("} }");
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("PackedConstructionEdges", fixture.ToString())
            );
            List<VariableDeclaratorSyntax> tables = generated
                .GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Where(variable => string.Equals(variable.Identifier.ValueText, "metadata", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(2, tables.Count);
            ArrayCreationExpressionSyntax fallback = Assert.IsType<ArrayCreationExpressionSyntax>(tables[0].Initializer.Value);
            Assert.Equal(256, fallback.Initializer.Expressions.Count);
            for (int index = 0; index < 64; index++)
            {
                int offset = index * 4;
                Assert.Equal(expectedNames[index], ((LiteralExpressionSyntax)fallback.Initializer.Expressions[offset]).Token.ValueText);
                Assert.Equal(expectedMethods[index], ((LiteralExpressionSyntax)fallback.Initializer.Expressions[offset + 1]).Token.ValueText);
                Assert.True(fallback.Initializer.Expressions[offset + 2].IsKind(SyntaxKind.NullLiteralExpression));
                Assert.True(fallback.Initializer.Expressions[offset + 3].IsKind(SyntaxKind.NullLiteralExpression));
            }
            LiteralExpressionSyntax packed = Assert.IsType<LiteralExpressionSyntax>(tables[1].Initializer.Value);
            Assert.True(packed.IsKind(SyntaxKind.StringLiteralExpression));
            string[] fields = packed.Token.ValueText.Split('\0');
            Assert.Equal(128, fields.Length);
            for (int index = 64; index < count; index++)
            {
                int offset = (index - 64) * 2;
                Assert.Equal(expectedNames[index], fields[offset]);
                Assert.Equal(expectedMethods[index], fields[offset + 1]);
            }
            Assert.Single(
                generated
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Where(method => string.Equals(method.Identifier.ValueText, "ReadPackedMetadata", StringComparison.Ordinal))
            );
            Assert.Equal(
                2,
                generated
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Count(call => string.Equals(call.Expression.ToString(), "entries.Add", StringComparison.Ordinal))
            );
            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            CatalogView repeated = CatalogView.Load(assembly);
            Assert.Equal(count, catalog.Entries.Count);
            Assert.Equal(count, repeated.Entries.Count);
            FieldInfo cachedHandler = catalog.Entries[0].GetType().GetField("_boundHandler", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] arguments = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            for (int index = 0; index < count; index++)
            {
                object entry = catalog.Entries[index];
                Assert.Same(entry, repeated.Entries[index]);
                Assert.Equal(expectedNames[index], catalog.NameOf(entry));
                Assert.Equal(expectedMethods[index], catalog.MethodNameOf(entry));
                Assert.Null(catalog.HelpOf(entry));
                Assert.Null(catalog.HintOf(entry));
                Assert.Equal(0, catalog.MinArgCountOf(entry));
                Assert.Equal(0, catalog.MaxArgCountOf(entry));
                Assert.True(catalog.AddToHistoryOf(entry));
                Assert.False(catalog.EditorOnlyOf(entry));
                Assert.False(catalog.DevelopmentOnlyOf(entry));
                Assert.False(catalog.IsDefaultOf(entry));
                Assert.Equal(7, catalog.ContextsOf(entry));
                Assert.Equal("Fixtures.PackedCommands", catalog.DeclaringTypeNameOf(entry));
                Assert.Null(catalog.MethodAccessorOf(entry));
                Assert.Null(cachedHandler.GetValue(entry));
                (object first, object second) = catalog.GetHandlerPairOf(entry);
                Assert.Same(first, second);
                Assert.Same(first, cachedHandler.GetValue(entry));
                Delegate handler = (Delegate)first;
                Assert.Null(handler.Target);
                Assert.True(handler.Method.IsStatic);
                Assert.Equal(expectedMethods[index], handler.Method.Name);
                Assert.Equal("Fixtures.PackedCommands", handler.Method.DeclaringType.FullName);
                handler.DynamicInvoke(arguments);
            }
            int[] calls = (int[])assembly.GetType("Fixtures.PackedCommands").GetField("Calls").GetValue(null);
            Assert.All(calls, value => Assert.Equal(1, value));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(64)]
        [InlineData(65)]
        [InlineData(129)]
        [InlineData(1000)]
        public void LargeCatalogBuildersStayBoundedAndPreserveEveryHandler(int count)
        {
            StringBuilder fixture = new StringBuilder();
            fixture.AppendLine("namespace Fixtures { using WallstopStudios.DxCommandTerminal.Attributes; using WallstopStudios.DxCommandTerminal.Backend;");
            fixture.Append("public static partial class BatchCommands { public static int[] Calls = new int[").Append(count).AppendLine("]; }");
            int[] metadataCounts = new int[16];
            for (int index = 0; index < count; index++)
            {
                fixture.AppendLine(index % 16 < 8 ? "public static partial class BatchCommands {" : "public static partial class OtherBatchCommands {");
                if (index != count - 1)
                {
                    ++metadataCounts[index % 16];
                }
                string suffix = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                fixture
                    .Append(index % 8 == 6 ? "[RegisterCommand(true, Name = \"batch" : "[RegisterCommand(Name = \"batch")
                    .Append(suffix)
                    .Append("\", Help = \"help")
                    .Append(suffix)
                    .Append("\", Hint = \"hint")
                    .Append(suffix)
                    .Append("\", MinArgCount = ")
                    .Append(index % 8 == 1 ? 1 : 0)
                    .Append(", MaxArgCount = ")
                    .Append(
                        index == count - 1 ? 17
                        : index % 8 == 2 ? -1
                        : 3
                    )
                    .Append(", EditorOnly = ")
                    .Append(index % 8 == 4 ? "true" : "false")
                    .Append(", DevelopmentOnly = ")
                    .Append(index % 8 == 5 ? "true" : "false")
                    .Append(", AddToHistory = ")
                    .Append(index % 8 != 3 ? "true" : "false")
                    .Append(", Contexts = (CommandExecutionContexts)(")
                    .Append(index % 8 == 7 ? -17 : 7)
                    .Append("))] ");
                fixture.Append("public static void Command").Append(suffix).Append("(CommandArg[] arguments) { BatchCommands.Calls[").Append(index).AppendLine("]++; } }");
            }
            fixture.AppendLine("}");
            string[] defines = { "UNITY_EDITOR", "DEVELOPMENT_BUILD" };
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("Batch" + count, fixture.ToString(), defines)
            );
            List<int> constructionCounts = generated
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Select(method =>
                    method
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Count(invocation => string.Equals(invocation.Expression.ToString(), "entries.Add", StringComparison.Ordinal))
                )
                .Where(value => 0 < value)
                .ToList();
            Assert.Equal(count, constructionCounts.Sum());
            Assert.All(constructionCounts, value => Assert.InRange(value, 1, 64));
            List<MethodDeclarationSyntax> binderGroups = generated
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText.StartsWith("BindGroup", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(64 < count, 0 < binderGroups.Count);
            Assert.All(
                binderGroups,
                method =>
                {
                    Assert.InRange(method.DescendantNodes().OfType<CaseSwitchLabelSyntax>().Count(), 1, 64);
                    IEnumerable<string> holders = method
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Select(creation => creation.ArgumentList.Arguments[0].Expression)
                        .OfType<MemberAccessExpressionSyntax>()
                        .Select(member => member.Expression.ToString())
                        .Distinct(StringComparer.Ordinal);
                    Assert.Single(holders);
                }
            );
            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(count, catalog.Entries.Count);
            CatalogView repeated = CatalogView.Load(assembly);
            object[] arguments = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            FieldInfo cachedHandler = catalog.Entries[0].GetType().GetField("_boundHandler", BindingFlags.Instance | BindingFlags.NonPublic);
            HashSet<int> slots = new HashSet<int>();
            for (int index = 0; index < count; index++)
            {
                object entry = catalog.Entries[index];
                Assert.Same(entry, repeated.Entries[index]);
                string suffix = index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal("batch" + suffix, catalog.NameOf(entry));
                Assert.Equal("help" + suffix, catalog.HelpOf(entry));
                Assert.Equal("hint" + suffix, catalog.HintOf(entry));
                Assert.Equal(
                    index == count - 1 ? 17
                        : index % 8 == 2 ? -1
                        : 3,
                    catalog.MaxArgCountOf(entry)
                );
                Assert.Equal(index % 8 == 4, catalog.EditorOnlyOf(entry));
                Assert.Equal(index % 8 == 5, catalog.DevelopmentOnlyOf(entry));
                Assert.Equal(index % 8 != 3, catalog.AddToHistoryOf(entry));
                Assert.Equal(index % 8 == 7 ? -17 : 7, catalog.ContextsOf(entry));
                Assert.Equal("Command" + suffix, catalog.MethodNameOf(entry));
                Assert.Equal(index % 8 == 1 ? 1 : 0, catalog.MinArgCountOf(entry));
                Assert.Equal(index % 8 == 6, catalog.IsDefaultOf(entry));
                string holder = index % 16 < 8 ? "Fixtures.BatchCommands" : "Fixtures.OtherBatchCommands";
                Assert.Equal(holder, catalog.DeclaringTypeNameOf(entry));
                Assert.Null(cachedHandler.GetValue(entry));
                Delegate factory = (Delegate)entry.GetType().GetProperty("Binder").GetValue(entry);
                if (64 < count && index != count - 1 && 4 <= metadataCounts[index % 16])
                {
                    Assert.NotNull(factory.Target);
                    Assert.Equal("IndexedBinder", factory.Target.GetType().Name);
                    FieldInfo slot = Assert.Single(factory.Target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
                    Assert.Equal(typeof(int), slot.FieldType);
                    Assert.True(slot.IsInitOnly);
                    int value = (int)slot.GetValue(factory.Target);
                    Assert.True(slots.Add(value));
                    MethodDeclarationSyntax group = Assert.Single(
                        binderGroups,
                        method =>
                            string.Equals(
                                method.Identifier.ValueText,
                                "BindGroup" + (value / 64).ToString(System.Globalization.CultureInfo.InvariantCulture),
                                StringComparison.Ordinal
                            )
                    );
                    Assert.Contains(
                        group.DescendantNodes().OfType<CaseSwitchLabelSyntax>(),
                        label => string.Equals(label.Value.ToString(), (value % 64).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    );
                }
                else
                {
                    Assert.Null(factory.Target);
                }
                (object first, object second) = catalog.GetHandlerPairOf(entry);
                Assert.Same(first, second);
                Delegate handler = (Delegate)first;
                Assert.Null(handler.Target);
                Assert.True(handler.Method.IsStatic);
                Assert.Equal("Command" + suffix, handler.Method.Name);
                Assert.Equal(holder, handler.Method.DeclaringType.FullName);
                handler.DynamicInvoke(arguments);
            }
            Assert.Equal(slots.Count, binderGroups.Sum(method => method.DescendantNodes().OfType<CaseSwitchLabelSyntax>().Count()));
            int[] calls = (int[])assembly.GetType("Fixtures.BatchCommands").GetField("Calls").GetValue(null);
            Assert.All(calls, value => Assert.Equal(1, value));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public void SharedBindersDoNotLoadAnExcludedHolderDependency(int excludedCount)
        {
            StringBuilder fixture = new StringBuilder();
            fixture.AppendLine("namespace Fixtures { using WallstopStudios.DxCommandTerminal.Attributes; using WallstopStudios.DxCommandTerminal.Backend;");
            fixture.AppendLine("public static class GoodCommands { public static int Calls;");
            for (int index = 0; index < 65; index++)
            {
                fixture
                    .Append("[RegisterCommand(Name = \"good")
                    .Append(index)
                    .Append("\", MaxArgCount = 0)] public static void Command")
                    .Append(index)
                    .AppendLine("(CommandArg[] arguments) { Calls++; }");
            }
            fixture.AppendLine("} public class ExcludedCommands : OptionalDependency.Base { public static int Calls;");
            for (int index = 0; index < excludedCount; index++)
            {
                fixture
                    .Append("[RegisterCommand(Name = \"excluded")
                    .Append(index)
                    .Append("\", MaxArgCount = 0, EditorOnly = true)] public static void Excluded")
                    .Append(index)
                    .AppendLine("(CommandArg[] arguments) { Calls++; }");
            }
            fixture.AppendLine("} }");
            CSharpCompilation consumer = TestCompilationFactory.CreateCompilation("OptionalBindingIsolation" + excludedCount, fixture.ToString());
            CSharpCompilation dependency = CSharpCompilation.Create(
                "OptionalCatalogDependency",
                new[] { CSharpSyntaxTree.ParseText("namespace OptionalDependency { public class Base { } }") },
                consumer.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
            using MemoryStream dependencyStream = new MemoryStream();
            Microsoft.CodeAnalysis.Emit.EmitResult emitted = dependency.Emit(dependencyStream);
            Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
            byte[] dependencyImage = dependencyStream.ToArray();
            consumer = consumer.AddReferences(MetadataReference.CreateFromImage(dependencyImage));
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(consumer);
            int[] groupSizes = generated
                .GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(method => method.Identifier.ValueText.StartsWith("BindGroup", StringComparison.Ordinal))
                .Select(method => method.DescendantNodes().OfType<CaseSwitchLabelSyntax>().Count())
                .OrderBy(size => size)
                .ToArray();
            Assert.Equal(excludedCount == 1 ? 2 : 3, groupSizes.Length);
            Assert.Equal(1, groupSizes[0]);
            Assert.Equal(64, groupSizes[groupSizes.Length - 1]);
            if (excludedCount == 4)
            {
                Assert.Equal(4, groupSizes[1]);
            }

            Assembly presentAssembly = TestCompilationFactory.CompileAndLoad(output, dependencyImage);
            CatalogView present = CatalogView.Load(presentAssembly);
            object[] presentArguments = { Array.CreateInstance(presentAssembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            foreach (object entry in present.Entries)
            {
                (object first, object second) = present.GetHandlerPairOf(entry);
                Assert.Same(first, second);
                ((Delegate)first).DynamicInvoke(presentArguments);
            }
            Assert.Equal(65, presentAssembly.GetType("Fixtures.GoodCommands").GetField("Calls").GetValue(null));
            Assert.Equal(excludedCount, presentAssembly.GetType("Fixtures.ExcludedCommands").GetField("Calls").GetValue(null));

            Assembly missingAssembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView missing = CatalogView.Load(missingAssembly);
            CatalogView repeated = CatalogView.Load(missingAssembly);
            Assert.Equal(65 + excludedCount, missing.Entries.Count);
            FieldInfo cache = missing.Entries[0].GetType().GetField("_boundHandler", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.All(missing.Entries, entry => Assert.Null(cache.GetValue(entry)));
            object[] missingArguments = { Array.CreateInstance(missingAssembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            for (int index = 0; index < 65; index++)
            {
                object entry = missing.Entries[index];
                Assert.Same(entry, repeated.Entries[index]);
                Assert.False(missing.EditorOnlyOf(entry));
                (object first, object second) = missing.GetHandlerPairOf(entry);
                Assert.Same(first, second);
                Delegate handler = (Delegate)first;
                Assert.Null(handler.Target);
                Assert.Equal("Command" + index, handler.Method.Name);
                Assert.Equal("Fixtures.GoodCommands", handler.Method.DeclaringType.FullName);
                handler.DynamicInvoke(missingArguments);
            }
            Assert.Equal(65, missingAssembly.GetType("Fixtures.GoodCommands").GetField("Calls").GetValue(null));
            for (int index = 65; index < missing.Entries.Count; index++)
            {
                object entry = missing.Entries[index];
                Assert.True(missing.EditorOnlyOf(entry));
                Assert.Null(cache.GetValue(entry));
                TargetInvocationException failure = Assert.Throws<TargetInvocationException>(() => missing.GetHandlerPairOf(entry));
                Assert.True(failure.InnerException is FileNotFoundException or FileLoadException or TypeLoadException, failure.ToString());
                Assert.Contains("OptionalCatalogDependency", failure.ToString(), StringComparison.Ordinal);
                Assert.Null(cache.GetValue(entry));
            }
        }

        [Fact]
        public void RegistersExplicitAttributes()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("ExplicitAttributes", StandardFixture));
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
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("BinderExecution", StandardFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Type commandsType = assembly.GetType("Fixtures.Commands");
            FieldInfo publicInvocations = commandsType.GetField("PublicInvocations");
            FieldInfo secretInvocations = commandsType.GetField("SecretInvocations");
            Assert.Equal(0, publicInvocations.GetValue(null));
            Assert.Equal(0, secretInvocations.GetValue(null));

            Type argType = assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg");

            /*
               CommandArg is a value type, so its arrays cannot be cast to
               object[]; the arguments are boxed inside a one-element object[].
            */
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

        [Fact]
        public void RegistersAliasesAndQualifiedAttributes()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("Aliases", AliasFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);
            Assert.Equal("Aliased", catalog.NameOf(catalog.Entries[0]));
            Assert.Equal("Qualified", catalog.NameOf(catalog.Entries[1]));
        }

        [Fact]
        public void GeneratedTextIsDeterministic()
        {
            StringBuilder fixture = new StringBuilder(DeterminismFixture);
            for (int index = 0; index < 64; index++)
            {
                fixture.Append(
                    "namespace Fixtures { using WallstopStudios.DxCommandTerminal.Attributes; using WallstopStudios.DxCommandTerminal.Backend; public static partial class "
                );
                fixture.Append(index % 2 == 0 ? "IndexedFirst" : "IndexedSecond");
                fixture.Append(" { [RegisterCommand(Contexts = (CommandExecutionContexts)(-17), MaxArgCount = ").Append(index % 3).Append(")] public static void Command");
                fixture.Append(index).AppendLine("(CommandArg[] arguments) { } } }");
            }
            string source = fixture.ToString();
            System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                string first = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("DeterminismA", source)).generated?.ToString();
                System.Globalization.CultureInfo alternate = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
                alternate.NumberFormat.NegativeSign = "minus";
                System.Globalization.CultureInfo.CurrentCulture = alternate;
                string second = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("DeterminismB", source)).generated?.ToString();
                Assert.Equal(first, second);
                Assert.Contains("private sealed class IndexedBinder", first, StringComparison.Ordinal);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void GeneratedCatalogCarriesStrippingPreservation()
        {
            string generated = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("Preserved", DeterminismFixture)).generated?.ToString();

            Assert.NotNull(generated);
            string preserveAttribute = "[global::UnityEngine.Scripting.Preserve]";
            Assert.Contains(preserveAttribute, generated, StringComparison.Ordinal);
            Assert.Contains(preserveAttribute + Environment.NewLine + "        internal static class CommandCatalog", generated, StringComparison.Ordinal);
            Assert.Contains(preserveAttribute + Environment.NewLine + "            public static void Collect(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void CatalogOmitsPreservationWithoutUnityEngine()
        {
            (SyntaxTree generated, _) = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("NoUnity", DeterminismFixture, includeUnityShim: false));

            Assert.NotNull(generated);
            Assert.DoesNotContain("Preserve", generated.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void EntryOrderMatchesDeclarationOrder()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("EntryOrder", DeterminismFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(3, catalog.Entries.Count);
            Assert.Equal("First", catalog.NameOf(catalog.Entries[0]));
            Assert.Equal("Second", catalog.NameOf(catalog.Entries[1]));
            Assert.Equal("Third", catalog.NameOf(catalog.Entries[2]));
        }

        /*
            The name form is the one Assembly.GetType resolves: the nested
            chain joins with plus signs (Type.FullName shape). A dot-joined
            name compiled and looked correct while failing the lookup on
            Mono - the live-editor failure this fixture pins.
        */
        [Fact]
        public void NestedHolderNamesResolveThroughAssemblyGetType()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("NestedHolderName", NestedHolderFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.True(catalog.HasValidSignature(entry));
            string holderName = catalog.DeclaringTypeNameOf(entry);
            Assert.Equal("Fixtures.Holders+Inner", holderName);

            Type holder = assembly.GetType(holderName);
            Assert.NotNull(holder);
            Type argumentArrayType = assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg");
            MethodInfo method = holder.GetMethod(
                catalog.MethodNameOf(entry),
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { argumentArrayType.MakeArrayType() },
                null
            );
            Assert.NotNull(method);

            FieldInfo counter = holder.GetField("Invocations");
            Assert.NotNull(counter);
            int before = (int)counter.GetValue(null);
            catalog.BinderOf(entry)(new object[] { Array.CreateInstance(argumentArrayType, 0) });
            Assert.Equal(before + 1, (int)counter.GetValue(null));
        }

        [Fact]
        public void ValidEntriesResolveTheirHandlerThroughTheDeclaringTypeName()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("HolderName", StandardFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            BindingFlags discoveryFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (object entry in catalog.Entries)
            {
                if (!catalog.HasValidSignature(entry))
                {
                    Assert.Null(catalog.DeclaringTypeNameOf(entry));
                    continue;
                }

                string holderName = catalog.DeclaringTypeNameOf(entry);
                Type holder = assembly.GetType(holderName);
                Assert.NotNull(holder);
                Type argumentArrayType = assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg");
                MethodInfo method = holder.GetMethod(catalog.MethodNameOf(entry), discoveryFlags, null, new[] { argumentArrayType.MakeArrayType() }, null);
                Assert.NotNull(method);

                /*
                    The resolved handler must be the one the binder binds: it
                    writes to the fixture's invocation counter.
                */
                if (method.Name == "Greet")
                {
                    FieldInfo counter = holder.GetField("PublicInvocations");
                    Assert.NotNull(counter);
                    int before = (int)counter.GetValue(null);
                    object[] args = { Array.CreateInstance(argumentArrayType, 0) };
                    catalog.BinderOf(entry)(args);
                    Assert.Equal(before + 1, (int)counter.GetValue(null));
                }
            }
        }

        [Fact]
        public void GetHandlerReturnsTheSameDelegateInstanceOnASecondCall()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("HandlerMemo", StandardFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            foreach (object entry in catalog.Entries)
            {
                (object first, object second) = catalog.GetHandlerPairOf(entry);
                if (!catalog.HasValidSignature(entry))
                {
                    Assert.Null(first);
                    continue;
                }

                Assert.NotNull(first);
                Assert.True(ReferenceEquals(first, second), $"Command '{catalog.NameOf(entry)}' re-bound its handler on a second call");
            }
        }

        [Fact]
        public void RegistersDefaultishInternalConstructor()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("Defaultish", DefaultishFixture)).output
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
        public void CarriesExecutionContextsIntoCatalogEntries()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("Contexts", ContextsFixture)).output
            );

            /*
                Entries are emitted in declaration order. The omitted case
                must match RegisterCommandAttribute's default (All = 7), so
                attributed commands without Contexts keep unrestricted
                eligibility through the catalog path too.
             */
            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(4, catalog.Entries.Count);
            Assert.Equal(1, catalog.ContextsOf(catalog.Entries[0]));
            Assert.Equal(6, catalog.ContextsOf(catalog.Entries[1]));
            Assert.Equal(7, catalog.ContextsOf(catalog.Entries[2]));
            Assert.Equal(5, catalog.ContextsOf(catalog.Entries[3]));
        }

        [Fact]
        public void RegistersCommandsWithExactSignatureAccessors()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("ExactAccessors", RejectionFixture)).output
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
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("Skips", RejectionFixture)).output
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
                TestCompilationFactory.CreateCompilation("NoContract", StandardFixture, includeCommandCatalogEntry: false)
            );

            Assert.Null(generated);
            Assert.Null(output.SyntaxTrees.FirstOrDefault(tree => tree.FilePath == TestCompilationFactory.GeneratedHintName));
        }

        [Fact]
        public void SkipsCommandsWhenNoCommandsArePresent()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("NoCommands", NoCommandsFixture));

            Assert.Null(generated);
            Assert.Null(output.SyntaxTrees.FirstOrDefault(tree => tree.FilePath == TestCompilationFactory.GeneratedHintName));
        }

        [Fact]
        public void SkipsCommandsWhenFixtureIsEmpty()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("EmptyFixture", string.Empty));

            Assert.Null(generated);
            Assert.Null(output.SyntaxTrees.FirstOrDefault(tree => tree.FilePath == TestCompilationFactory.GeneratedHintName));
        }

        [Fact]
        public void RegistersPositionalConstructorNames()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("PositionalName", PositionalNameFixture)).output
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
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("KeywordMethods", KeywordMethodFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);
            Assert.Equal("params", catalog.NameOf(catalog.Entries[0]));
            Assert.Equal("params", catalog.MethodNameOf(catalog.Entries[0]));
            Assert.Equal("void", catalog.NameOf(catalog.Entries[1]));

            FieldInfo invocations = assembly.GetType("Fixtures.KeywordCommands").GetField("KeywordInvocations");
            Assert.Equal(0, invocations.GetValue(null));
            catalog.BinderOf(catalog.Entries[0])(new object[] { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) });
            Assert.Equal(1, invocations.GetValue(null));
            catalog.BinderOf(catalog.Entries[1])(new object[] { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) });
            Assert.Equal(11, invocations.GetValue(null));
        }

        [Fact]
        public void BlankInferredNamesAreEmittedAndRejectedLikeLegacy()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("BlankName", BlankNameFixture)).output
            );

            /*
               The catalog is loadable: a blank inferred name must not poison
               the assembly's static initializer.
            */
            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal(string.Empty, catalog.NameOf(entry));
            Assert.True(catalog.HasValidSignature(entry));
        }

        [Fact]
        public void PartialMethodDeclarationsProduceOneEntry()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("PartialMethods", PartialMethodFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal("Partial", catalog.NameOf(entry));
        }

        [Fact]
        public void DuplicateInferredNamesEachProduceAnEntry()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("DuplicateNames", DuplicateNameFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);
            Assert.All(catalog.Entries, entry => Assert.Equal("Heal", catalog.NameOf(entry)));
        }

        [Fact]
        public void NonVoidHandlerEmitCachedBinderThatThrowsLikeReflection()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("NonVoidHandlers", NonVoidHandlerFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object value = catalog.Entries.Single(entry => catalog.NameOf(entry) == "NonVoidValue");
            Assert.True(catalog.HasValidSignature(value));
            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            Assert.ThrowsAny<Exception>(() => catalog.BinderOf(value)(args));
        }

        [Fact]
        public void NonVoidTaskHandlerEmitCachedBinderThatThrowsLikeReflection()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("NonVoidHandlersTask", NonVoidHandlerFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object task = catalog.Entries.Single(entry => catalog.NameOf(entry) == "NonVoidTask");
            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            Assert.ThrowsAny<Exception>(() => catalog.BinderOf(task)(args));
        }

        [Fact]
        public void NonVoidFixtureKeepsVoidHandlerDirectlyBindable()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("NonVoidHandlersVoid", NonVoidHandlerFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object voidCommand = catalog.Entries.Single(entry => catalog.NameOf(entry) == "Void");
            Assert.True(catalog.HasValidSignature(voidCommand));
            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            catalog.BinderOf(voidCommand)(args);
        }

        [Fact]
        public void InaccessibleHoldersSkipTheCatalogForTheWholeAssembly()
        {
            (SyntaxTree generated, _) = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("InaccessibleHolder", InaccessibleHolderFixture));

            Assert.Null(generated);
        }

        [Fact]
        public void InaccessibleHoldersForceReflectionEvenWhenOtherCommandsExist()
        {
            (SyntaxTree generated, _) = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("InaccessibleMixed", InaccessibleMixedFixture));

            Assert.Null(generated);
        }

        [Theory]
        [InlineData("public", ",", 2)]
        [InlineData("private", ",", 2)]
        [InlineData("public", ",,", 3)]
        [InlineData("private", ",,", 3)]
        public void MultidimensionalCommandArraysRemainRejectedInPartialHolders(string accessibility, string dimensions, int rank)
        {
            string fixture =
                @"
namespace Fixtures
{
    using WallstopStudios.DxCommandTerminal.Attributes;
    using WallstopStudios.DxCommandTerminal.Backend;

    public static partial class ArrayCommands
    {
        public static int Invocations;

        [RegisterCommand]
        "
                + accessibility
                + @" static void Matrix(CommandArg["
                + dimensions
                + @"] args) { }

        [RegisterCommand]
        private static void Vector(CommandArg[] args)
        {
            Invocations++;
        }
    }
}";
            CSharpCompilation compilation = TestCompilationFactory.CreateCompilation("MultidimensionalArrays" + accessibility + rank, fixture);
            Assert.DoesNotContain(compilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(compilation);
            Assert.NotNull(generated);
            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(2, catalog.Entries.Count);
            object matrix = catalog.Entries[0];
            Assert.Equal("Matrix", catalog.NameOf(matrix));
            Assert.False(catalog.HasValidSignature(matrix));
            Assert.Null(catalog.BinderOf(matrix));
            MethodInfo accessor = catalog.MethodAccessorOf(matrix);
            Assert.NotNull(accessor);
            Assert.Equal("Matrix", accessor.Name);
            Assert.Equal(rank, Assert.Single(accessor.GetParameters()).ParameterType.GetArrayRank());

            object vector = catalog.Entries[1];
            Assert.True(catalog.HasValidSignature(vector));
            Assert.Null(catalog.MethodAccessorOf(vector));
            catalog.BinderOf(vector)(new object[] { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) });
            Assert.Equal(1, assembly.GetType("Fixtures.ArrayCommands").GetField("Invocations").GetValue(null));
        }

        [Fact]
        public void PartialHolderPrivateCommandsBindWithoutReflection()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("PartialPrivate", PartialPrivateCommandFixture)
            );

            Assert.NotNull(generated);
            string source = generated.ToString();
            Assert.Contains("DxCommandTerminalBinder", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetMethod", source, StringComparison.Ordinal);

            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal("Secret", catalog.NameOf(entry));
            Assert.True(catalog.HasValidSignature(entry));
            Assert.Null(catalog.MethodAccessorOf(entry));

            FieldInfo invocations = assembly.GetType("Fixtures.PartialPrivateCommands").GetField("SecretInvocations");
            Assert.Equal(0, invocations.GetValue(null));
            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            catalog.BinderOf(entry)(args);
            Assert.Equal(1, invocations.GetValue(null));
        }

        [Fact]
        public void PartialHolderPrivateCommandsReuseTheCachedBinder()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("PartialPrivateCached", PartialPrivateCommandFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            catalog.BinderOf(entry)(args);
            Assert.Same(catalog.BinderOf(entry)(args), catalog.BinderOf(entry)(args));
        }

        [Fact]
        public void PartialNestedHolderPrivateCommandsBindWithoutReflection()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("PartialNested", PartialPrivateNestedCommandFixture)
            );

            Assert.NotNull(generated);
            string source = generated.ToString();
            Assert.Contains("DxCommandTerminalBinder", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GetMethod", source, StringComparison.Ordinal);

            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal("Secret", catalog.NameOf(entry));
            Assert.True(catalog.HasValidSignature(entry));

            FieldInfo invocations = assembly.GetType("Fixtures.PartialOuter+PartialInner").GetField("SecretInvocations");
            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            catalog.BinderOf(entry)(args);
            Assert.Equal(1, invocations.GetValue(null));
        }

        [Fact]
        public void PartialStructPrivateCommandsBindWithoutReflection()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("PartialStruct", PartialStructPrivateCommandFixture)
            );

            Assert.NotNull(generated);
            Assert.DoesNotContain("GetMethod", generated.ToString(), StringComparison.Ordinal);

            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.True(catalog.HasValidSignature(entry));

            FieldInfo invocations = assembly.GetType("Fixtures.PartialStructCommands").GetField("SecretInvocations");
            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            catalog.BinderOf(entry)(args);
            Assert.Equal(1, invocations.GetValue(null));
        }

        [Fact]
        public void PartialHoldersGroupCompanionsAndKeepAccessibleCommandsDirect()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("PartialMixed", PartialMixedCommandsFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            Assert.Equal(4, catalog.Entries.Count);
            FieldInfo invocations = assembly.GetType("Fixtures.PartialMixedCommands").GetField("Invocations");

            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            foreach (object entry in catalog.Entries)
            {
                catalog.BinderOf(entry)(args);
            }

            Assert.Equal(3, invocations.GetValue(null));
        }

        [Fact]
        public void KeywordNamedPartialHoldersEmitCompilableCompanions()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("KeywordPartial", KeywordNamedPartialHolderFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.True(catalog.HasValidSignature(entry));

            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            catalog.BinderOf(entry)(args);
        }

        [Fact]
        public void NonPartialHolderPrivateCommandsKeepTheReflectionBinder()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("NonPartialPrivate", StandardFixture));

            Assert.NotNull(generated);
            string source = generated.ToString();
            Assert.DoesNotContain("DxCommandTerminalBinder", source, StringComparison.Ordinal);
            Assert.Contains("GetMethod", source, StringComparison.Ordinal);

            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            object secret = catalog.Entries.Single(entry => catalog.NameOf(entry) == "Secret");
            Assert.True(catalog.HasValidSignature(secret));
            object[] args = { Array.CreateInstance(assembly.GetType("WallstopStudios.DxCommandTerminal.Backend.CommandArg"), 0) };
            catalog.BinderOf(secret)(args);
        }

        [Fact]
        public void GenericPartialHoldersEmitNoCompanion()
        {
            (SyntaxTree generated, CSharpCompilation output) = TestCompilationFactory.RunGenerator(
                TestCompilationFactory.CreateCompilation("GenericPartial", GenericPartialHolderFixture)
            );

            Assert.NotNull(generated);
            Assert.DoesNotContain("DxCommandTerminalBinder", generated.ToString(), StringComparison.Ordinal);

            Assembly assembly = TestCompilationFactory.CompileAndLoad(output);
            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.False(catalog.HasValidSignature(entry));
            Assert.NotNull(catalog.MethodAccessorOf(entry));
        }

        [Fact]
        public void FileLocalHoldersSkipTheCatalogForTheWholeAssembly()
        {
            CSharpCompilation compilation = TestCompilationFactory.CreateCompilation("FileLocalPartial", FileLocalPartialHolderFixture);
            Assert.DoesNotContain(compilation.GetParseDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

            (SyntaxTree generated, _) = TestCompilationFactory.RunGenerator(compilation);
            Assert.Null(generated);
        }

        [Fact]
        public void KeywordNamedHoldersEmitCompilableCatalogs()
        {
            Assembly assembly = TestCompilationFactory.CompileAndLoad(
                TestCompilationFactory.RunGenerator(TestCompilationFactory.CreateCompilation("KeywordHolder", KeywordNamedHolderFixture)).output
            );

            CatalogView catalog = CatalogView.Load(assembly);
            object entry = Assert.Single(catalog.Entries);
            Assert.Equal("Run", catalog.NameOf(entry));
            Assert.True(catalog.HasValidSignature(entry));
        }
    }
}
