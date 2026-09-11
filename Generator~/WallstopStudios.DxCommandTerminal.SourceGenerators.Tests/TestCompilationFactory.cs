namespace WallstopStudios.DxCommandTerminal.SourceGenerators.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.Loader;
    using System.Text;
    using Basic.Reference.Assemblies;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Emit;

    internal static class TestCompilationFactory
    {
        public const string CatalogTypeName =
            "WallstopStudios.DxCommandTerminal.Generated.CommandCatalog";

        public const string EntryTypeName =
            "WallstopStudios.DxCommandTerminal.Backend.CommandCatalogEntry";

        public const string GeneratedHintName = "DxCommandTerminalCommandCatalog.g.cs";

        private static readonly IReadOnlyList<MetadataReference> References = NetStandard21
            .References
            .All;

        private static readonly List<SyntaxTree> CoreRuntimeSources = LoadRuntimeSources(
            new[]
            {
                "Runtime/Attributes/RegisterCommandAttribute.cs",
                "Runtime/CommandTerminal/Backend/CommandArg.cs",
                "Runtime/CommandTerminal/Backend/CommandExecutionContexts.cs",
                "Runtime/CommandTerminal/Backend/CommandExecutionContextSets.cs",
                "Runtime/CommandTerminal/Backend/CommandExecutionContextsExtensions.cs",
                "Runtime/CommandTerminal/Backend/CommandArgParser.cs",
            }
        );

        private static readonly List<SyntaxTree> ContractSources = LoadRuntimeSources(
            new[] { "Runtime/CommandTerminal/Backend/CommandCatalogEntry.cs" }
        );

        private static readonly List<SyntaxTree> UnityShimSources = LoadRuntimeSources(
            new[]
            {
                "Generator~/WallstopStudios.DxCommandTerminal.SourceGenerators.Tests/UnityScriptingShim.cs",
            }
        );

        public static CSharpCompilation CreateCompilation(
            string assemblyName,
            string fixtureSource,
            string[] defines = null,
            bool includeCommandCatalogEntry = true
        )
        {
            List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();
            syntaxTrees.AddRange(UnityShimSources);
            syntaxTrees.AddRange(CoreRuntimeSources);
            if (includeCommandCatalogEntry)
            {
                syntaxTrees.AddRange(ContractSources);
            }

            // The fixture is parsed with the caller's preprocessor symbols so
            // conditional compilation is exercised at parse time, exactly as
            // it is in real compilations.
            syntaxTrees.Add(
                CSharpSyntaxTree.ParseText(
                    fixtureSource,
                    new CSharpParseOptions(preprocessorSymbols: defines ?? Array.Empty<string>())
                )
            );

            return CSharpCompilation.Create(
                assemblyName,
                syntaxTrees,
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }

        public static (SyntaxTree generated, CSharpCompilation output) RunGenerator(
            CSharpCompilation compilation
        )
        {
            CommandCatalogGenerator generator = new CommandCatalogGenerator();
            CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(
                compilation,
                out Compilation outputCompilation,
                out ImmutableArray<Diagnostic> generatorDiagnostics
            );

            List<Diagnostic> errors = generatorDiagnostics
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToList();
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "The generator produced error diagnostics: "
                        + string.Join(
                            Environment.NewLine,
                            errors.Select(diagnostic => diagnostic.ToString())
                        )
                );
            }

            CSharpCompilation output = (CSharpCompilation)outputCompilation;
            SyntaxTree generated = output.SyntaxTrees.FirstOrDefault(tree =>
                tree.FilePath == GeneratedHintName || tree.FilePath.EndsWith(GeneratedHintName)
            );
            return (generated, output);
        }

        public static Assembly CompileAndLoad(CSharpCompilation compilation)
        {
            List<Diagnostic> errors = compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToList();
            if (errors.Count > 0)
            {
                StringBuilder message = new StringBuilder("Fixture compilation has errors:");
                foreach (Diagnostic error in errors)
                {
                    message.AppendLine().Append(error.ToString());
                }

                SyntaxTree generatedTree = compilation.SyntaxTrees.FirstOrDefault(tree =>
                    tree.FilePath == GeneratedHintName || tree.ToString().Contains("auto-generated")
                );
                if (generatedTree != null)
                {
                    message
                        .AppendLine()
                        .AppendLine("Generated source was:")
                        .Append(generatedTree.ToString());
                }

                throw new InvalidOperationException(message.ToString());
            }

            using MemoryStream peStream = new MemoryStream();
            EmitResult emitResult = compilation.Emit(peStream);
            if (!emitResult.Success)
            {
                List<Diagnostic> emitErrors = emitResult
                    .Diagnostics.Where(diagnostic =>
                        diagnostic.Severity == DiagnosticSeverity.Error
                    )
                    .ToList();
                throw new InvalidOperationException(
                    "Fixture compilation could not be emitted to an assembly:"
                        + Environment.NewLine
                        + string.Join(
                            Environment.NewLine,
                            emitErrors.Select(diagnostic => diagnostic.ToString())
                        )
                );
            }

            peStream.Position = 0;
            TestAssemblyLoadContext loadContext = new TestAssemblyLoadContext();
            return loadContext.LoadFromStream(peStream);
        }

        private static List<SyntaxTree> LoadRuntimeSources(string[] relativePaths)
        {
            string repoRoot = FindRepoRoot();
            List<SyntaxTree> syntaxTrees = new List<SyntaxTree>();
            foreach (string relativePath in relativePaths)
            {
                string path = Path.Combine(repoRoot, relativePath);
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"Runtime source for the test harness was not found: {path}"
                    );
                }

                syntaxTrees.Add(
                    CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: relativePath)
                );
            }

            return syntaxTrees;
        }

        private static string FindRepoRoot()
        {
            string current = AppContext.BaseDirectory;
            while (current != null)
            {
                string generatorDirectory = Path.Combine(current, "Generator~");
                if (
                    Directory.Exists(generatorDirectory)
                    && File.Exists(
                        Path.Combine(current, "Runtime/Attributes/RegisterCommandAttribute.cs")
                    )
                )
                {
                    return current;
                }

                current = Path.GetDirectoryName(current.TrimEnd(Path.DirectorySeparatorChar));
            }

            throw new InvalidOperationException(
                "Could not locate the repository root from "
                    + AppContext.BaseDirectory
                    + "; expected an ancestor directory containing Generator~ and the Runtime sources."
            );
        }

        private sealed class TestAssemblyLoadContext : AssemblyLoadContext
        {
            public TestAssemblyLoadContext()
                : base(name: "DxCommandTerminalGeneratorTests", isCollectible: true) { }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                // netstandard and the System.* facades forward to the shared
                // framework; anything else is a genuine harness failure. The
                // ALC requires the resolved simple name to match, so the
                // netstandard facade is loaded from the runtime directory.
                if (assemblyName.Name == "netstandard")
                {
                    string runtimeDirectory = Path.GetDirectoryName(
                        typeof(object).Assembly.Location
                    );
                    string facadePath = Path.Combine(runtimeDirectory, "netstandard.dll");
                    return File.Exists(facadePath) ? LoadFromAssemblyPath(facadePath) : null;
                }

                return null;
            }
        }
    }

    /*
        Reflection-typed view over the generated catalog of a loaded
        synthetic assembly. The catalog and contract types belong to that
        assembly, so everything is accessed through reflection.
     */
    internal sealed class CatalogView
    {
        private readonly Type _entryType;

        public Assembly Assembly { get; }
        public IReadOnlyList<object> Entries { get; }

        private CatalogView(Assembly assembly, IReadOnlyList<object> entries, Type entryType)
        {
            Assembly = assembly;
            Entries = entries;
            _entryType = entryType;
        }

        public static CatalogView Load(Assembly assembly)
        {
            Type catalogType = assembly.GetType(TestCompilationFactory.CatalogTypeName);
            if (catalogType == null)
            {
                throw new InvalidOperationException(
                    $"Assembly {assembly.GetName().Name} has no generated command catalog."
                );
            }

            Type entryType = assembly.GetType(TestCompilationFactory.EntryTypeName);
            MethodInfo collect = catalogType.GetMethod(
                "Collect",
                BindingFlags.Public | BindingFlags.Static
            );
            if (collect == null)
            {
                throw new InvalidOperationException(
                    "The generated catalog does not expose a public static Collect method."
                );
            }

            IList entries = (IList)
                Activator.CreateInstance(typeof(List<>).MakeGenericType(entryType));
            collect.Invoke(null, new object[] { entries });
            List<object> snapshot = new List<object>();
            foreach (object entry in entries)
            {
                snapshot.Add(entry);
            }

            return new CatalogView(assembly, snapshot, entryType);
        }

        private object GetValue(object entry, string propertyName)
        {
            return _entryType
                .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                .GetValue(entry);
        }

        public string NameOf(object entry) => (string)GetValue(entry, "Name");

        public string MethodNameOf(object entry) => (string)GetValue(entry, "MethodName");

        public int MinArgCountOf(object entry) => (int)GetValue(entry, "MinArgCount");

        public int MaxArgCountOf(object entry) => (int)GetValue(entry, "MaxArgCount");

        public string HelpOf(object entry) => (string)GetValue(entry, "Help");

        public string HintOf(object entry) => (string)GetValue(entry, "Hint");

        public bool AddToHistoryOf(object entry) => (bool)GetValue(entry, "AddToHistory");

        public bool EditorOnlyOf(object entry) => (bool)GetValue(entry, "EditorOnly");

        public bool DevelopmentOnlyOf(object entry) => (bool)GetValue(entry, "DevelopmentOnly");

        public bool IsDefaultOf(object entry) => (bool)GetValue(entry, "IsDefault");

        public int ContextsOf(object entry) => (int)GetValue(entry, "Contexts");

        public Func<object[], object> BinderOf(object entry)
        {
            // Binder is Func<Action<CommandArg[]>> against the loaded
            // assembly's own CommandArg type; both legs go through
            // DynamicInvoke so no compile-time reference is needed.
            Delegate binderFactory = (Delegate)GetValue(entry, "Binder");
            if (binderFactory == null)
            {
                return null;
            }

            Delegate handler = (Delegate)binderFactory.DynamicInvoke();
            return arguments => handler.DynamicInvoke(arguments);
        }

        public MethodInfo MethodAccessorOf(object entry)
        {
            Delegate accessorFactory = (Delegate)GetValue(entry, "MethodAccessor");
            if (accessorFactory == null)
            {
                return null;
            }

            return (MethodInfo)accessorFactory.DynamicInvoke();
        }

        public bool HasValidSignature(object entry) => (bool)GetValue(entry, "IsValid");
    }
}
