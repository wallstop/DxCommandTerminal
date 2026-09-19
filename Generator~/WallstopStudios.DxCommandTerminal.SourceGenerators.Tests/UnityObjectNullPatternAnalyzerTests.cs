namespace WallstopStudios.DxCommandTerminal.SourceGenerators.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using WallstopStudios.DxCommandTerminal.Analyzers;
    using Xunit;

    /*
        Contract tests for the shipped Unity-null-pattern analyzer (context rule 25).
        Fixtures compile against the shim's Object and GUIStyle, the two fake-null
        types. Violation cases pin the diagnostic id and how many times it fires;
        compliant cases pin that the correct Unity forms (== null / != null) and
        every plain-C# construct never fire anything.
    */
    public sealed class UnityObjectNullPatternAnalyzerTests
    {
        private const string DiagnosticIdPrefix = "DxCmd";

        private const string RepoAssemblyName = "WallstopStudios.DxCommandTerminal";

        private static readonly string[] RepoAssemblyNames =
        {
            "WallstopStudios.DxCommandTerminal",
            "WallstopStudios.DxCommandTerminal.Editor",
            "WallstopStudios.DxCommandTerminal.Tests.Runtime",
            "WallstopStudios.DxCommandTerminal.Samples.TerminalCommands",
        };

        private static readonly ImmutableArray<DiagnosticAnalyzer> Analyzer =
            ImmutableArray.Create<DiagnosticAnalyzer>(new UnityObjectNullPatternAnalyzer());

        public static IEnumerable<object[]> ViolationCases()
        {
            yield return new object[]
            {
                "UnityEngine.Object conditional access",
                Fixture("        _target?.ToString();"),
                "DxCmd0001",
                1,
            };
            yield return new object[]
            {
                "GUIStyle conditional access",
                Fixture("        _style?.ToString();"),
                "DxCmd0001",
                1,
            };
            yield return new object[]
            {
                "Derived receiver chain flags every fake-null hop",
                Fixture("        _widget?.Style?.ToString();"),
                "DxCmd0001",
                2,
            };
            yield return new object[]
            {
                "UnityEngine.Object coalesce",
                Fixture("        UnityEngine.Object merged = _target ?? new UnityEngine.Object();"),
                "DxCmd0002",
                1,
            };
            yield return new object[]
            {
                "GUIStyle coalesce",
                Fixture("        GUIStyle merged = _style ?? new GUIStyle();"),
                "DxCmd0002",
                1,
            };
            yield return new object[]
            {
                "UnityEngine.Object coalesce assignment",
                Fixture("        _target ??= new UnityEngine.Object();"),
                "DxCmd0002",
                1,
            };
            yield return new object[]
            {
                "GUIStyle coalesce assignment",
                Fixture("        _style ??= new GUIStyle();"),
                "DxCmd0002",
                1,
            };
            yield return new object[]
            {
                "if truthiness",
                Fixture("        if (_target) { }"),
                "DxCmd0003",
                1,
            };
            yield return new object[]
            {
                "negated truthiness",
                Fixture("        if (!_target) { }"),
                "DxCmd0003",
                1,
            };
            yield return new object[]
            {
                "GUIStyle while truthiness",
                Fixture("        while (_style) { break; }"),
                "DxCmd0003",
                1,
            };
            yield return new object[]
            {
                "ternary condition truthiness",
                Fixture("        int picked = _target ? 1 : 0;"),
                "DxCmd0003",
                1,
            };
            yield return new object[]
            {
                "both operands of && convert",
                Fixture("        bool both = _target && _style;"),
                "DxCmd0003",
                2,
            };
            yield return new object[]
            {
                "explicit cast to bool",
                Fixture("        bool direct = (bool)_target;"),
                "DxCmd0003",
                1,
            };
            yield return new object[]
            {
                "is null pattern",
                Fixture("        bool absent = _target is null;"),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "is not null pattern",
                Fixture("        bool present = _target is not null;"),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "GUIStyle is not null pattern",
                Fixture("        bool present = _style is not null;"),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "ReferenceEquals on UnityEngine.Object",
                Fixture("        bool same = object.ReferenceEquals(_target, null);"),
                "DxCmd0005",
                1,
            };
            yield return new object[]
            {
                "ReferenceEquals on GUIStyle",
                Fixture("        bool same = object.ReferenceEquals(null, _style);"),
                "DxCmd0005",
                1,
            };
            yield return new object[]
            {
                "unqualified ReferenceEquals on UnityEngine.Object",
                Fixture("        bool same = ReferenceEquals(_target, null);"),
                "DxCmd0005",
                1,
            };
            yield return new object[]
            {
                "coalesce inside a lambda",
                Fixture(
                    "        System.Func<UnityEngine.Object> make = () => _target ?? new UnityEngine.Object();"
                ),
                "DxCmd0002",
                1,
            };
            yield return new object[]
            {
                "parenthesized truthiness",
                Fixture("        if ((_target)) { }"),
                "DxCmd0003",
                1,
            };
            yield return new object[]
            {
                "do/while truthiness",
                Fixture("        do { } while (_style);"),
                "DxCmd0003",
                1,
            };
            yield return new object[]
            {
                "switch statement null case",
                Fixture(
                    "        switch (_target)\n        {\n            case null:\n                break;\n        }"
                ),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "switch statement 'case not null' pattern label",
                Fixture(
                    "        switch (_target)\n        {\n            case not null:\n                break;\n        }"
                ),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "switch statement 'case { }' pattern label",
                Fixture(
                    "        switch (_target)\n        {\n            case { }:\n                break;\n        }"
                ),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "switch statement 'case not { }' pattern label",
                Fixture(
                    "        switch (_target)\n        {\n            case not { }:\n                break;\n        }"
                ),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "switch statement null case with when clause",
                Fixture(
                    "        switch (_target)\n        {\n            case null when true:\n                break;\n        }"
                ),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "switch expression null arm",
                Fixture("        int picked = _target switch { null => 1, _ => 0 };"),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "is { } property pattern",
                Fixture("        bool present = _target is { };"),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "is not { } property pattern",
                Fixture("        bool present = _target is not { };"),
                "DxCmd0004",
                1,
            };
            yield return new object[]
            {
                "generic constrained to UnityEngine.Object coalesce",
                Fixture(
                    "        T Merge<T>(T value) where T : UnityEngine.Object => value ?? new UnityEngine.Object();\n        UnityEngine.Object merged = Merge(_widget);"
                ),
                "DxCmd0002",
                1,
            };
            yield return new object[]
            {
                "generic constrained to UnityEngine.Object is null",
                Fixture(
                    "        static bool GenericIsAbsent<T>(T value) where T : UnityEngine.Object\n        {\n            return value is null;\n        }\n\n        bool absent = GenericIsAbsent(_widget);"
                ),
                "DxCmd0004",
                1,
            };
        }

        public static IEnumerable<object[]> CompliantCases()
        {
            yield return new object[]
            {
                "plain class conditional access",
                Fixture("        _holder?.Name.ToString();"),
            };
            yield return new object[]
            {
                "List coalesce",
                Fixture("        List<int> merged = _list ?? new List<int>();"),
            };
            yield return new object[]
            {
                "array receiver coalesce is not a Unity receiver",
                Fixture(
                    "        UnityEngine.Object[] merged = _targets ?? new UnityEngine.Object[0];"
                ),
            };
            yield return new object[]
            {
                "delegate coalesce",
                Fixture("        System.Action action = _action ?? delegate { };"),
            };
            yield return new object[]
            {
                "nullable int coalesce",
                Fixture("        int value = _maybeInt ?? 0;"),
            };
            yield return new object[]
            {
                "string coalesce",
                Fixture("        string text = _text ?? string.Empty;"),
            };
            yield return new object[]
            {
                "explicit Unity == null / != null never fire",
                Fixture("        bool checks = _target == null || _target != null;"),
            };
            yield return new object[]
            {
                "plain class truthiness",
                Fixture("        if (_plain) { }"),
            };
            yield return new object[]
            {
                "declaration pattern on UnityEngine.Object",
                Fixture(
                    "        string label = _target is UnityEngine.Object live ? live.name : string.Empty;"
                ),
            };
            yield return new object[]
            {
                "ReferenceEquals on plain types",
                Fixture("        bool same = object.ReferenceEquals(_list, _text);"),
            };
            yield return new object[]
            {
                "error-type receiver does not crash the analyzer",
                Fixture("        _unknown?.ToString();"),
            };
            yield return new object[]
            {
                "string switch null case is not a Unity receiver",
                Fixture(
                    "        switch (_text)\n        {\n            case null:\n                break;\n        }"
                ),
            };
            yield return new object[]
            {
                "typed property pattern is a type test, not a null check",
                Fixture("        bool typed = _target is UnityEngine.Object { };"),
            };
            yield return new object[]
            {
                "declaration pattern case label is a type test",
                Fixture(
                    "        switch (_target)\n        {\n            case UnityEngine.Object live:\n                break;\n        }"
                ),
            };
        }

        private static ImmutableArray<Diagnostic> Analyze(string source)
        {
            return AnalyzeAs(RepoAssemblyName, source);
        }

        private static ImmutableArray<Diagnostic> AnalyzeAs(string assemblyName, string source)
        {
            CSharpCompilation compilation = TestCompilationFactory.CreateCompilation(
                assemblyName,
                source
            );
            CompilationWithAnalyzers analysis = compilation.WithAnalyzers(Analyzer);
            return analysis.GetAllDiagnosticsAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        private static string Fixture(string body)
        {
            return @"
namespace Fixtures
{
    using System.Collections.Generic;
    using UnityEngine;

    public static class UnityNullPatternCases
    {
        private static UnityEngine.Object _target;
        private static GUIStyle _style;
        private static Widget _widget;
        private static Holder _holder;
        private static Plain _plain;
        private static List<int> _list;
        private static UnityEngine.Object[] _targets;
        private static System.Action _action;
        private static int? _maybeInt;
        private static string _text;
        private static UnknownThing _unknown;

        private sealed class Widget : UnityEngine.Object
        {
            public GUIStyle Style;
        }

        private sealed class Holder
        {
            public string Name;
        }

        private sealed class Plain
        {
            public static implicit operator bool(Plain exists)
            {
                return exists != null;
            }
        }

        public static void Run()
        {
"
                + body
                + @"
        }
    }
}";
        }

        private static string Describe(ImmutableArray<Diagnostic> diagnostics)
        {
            if (diagnostics.IsEmpty)
            {
                return "(none)";
            }

            return string.Join("; ", diagnostics.Select(diagnostic => diagnostic.ToString()));
        }

        [Theory]
        [MemberData(nameof(ViolationCases))]
        public void FlagsBannedPatterns(
            string caseName,
            string source,
            string diagnosticId,
            int expectedCount
        )
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(source);
            int actual = diagnostics.Count(diagnostic => diagnostic.Id == diagnosticId);
            int total = diagnostics.Count(diagnostic =>
                diagnostic.Id.StartsWith(DiagnosticIdPrefix, StringComparison.Ordinal)
            );
            Assert.True(
                expectedCount == actual && total == expectedCount,
                $"{caseName}: expected {expectedCount} {diagnosticId}, found {actual} (all DxCmd: "
                    + $"{total}). All: {Describe(diagnostics)}"
            );
        }

        [Theory]
        [MemberData(nameof(CompliantCases))]
        public void AllowsCompliantPatterns(string caseName, string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Analyze(source);
            List<Diagnostic> banned = diagnostics
                .Where(diagnostic =>
                    diagnostic.Id.StartsWith(DiagnosticIdPrefix, StringComparison.Ordinal)
                )
                .ToList();
            Assert.True(
                0 == banned.Count,
                $"{caseName}: expected no diagnostics, found: {Describe(banned.ToImmutableArray())}"
            );
        }

        /*
           Internal-only contract: outside the repository's own assembly names the
           analyzer is inert, so a consumer assembly never sees these diagnostics even
           if some distribution path ever carries the binary.
        */
        [Fact]
        public void ConsumerAssembliesAreNotAnalyzed()
        {
            ImmutableArray<Diagnostic> diagnostics = AnalyzeAs(
                "Consumer.Game.Code",
                Fixture("        _target?.ToString();\n        if (_target) { }")
            );
            List<Diagnostic> banned = diagnostics
                .Where(diagnostic =>
                    diagnostic.Id.StartsWith(DiagnosticIdPrefix, StringComparison.Ordinal)
                )
                .ToList();
            Assert.True(
                0 == banned.Count,
                $"consumer assembly must not be analyzed, found: {Describe(banned.ToImmutableArray())}"
            );
        }

        [Fact]
        public void RepoAssembliesAreAnalyzed()
        {
            foreach (string name in RepoAssemblyNames)
            {
                ImmutableArray<Diagnostic> diagnostics = AnalyzeAs(
                    name,
                    Fixture("        _target?.ToString();")
                );
                int actual = diagnostics.Count(diagnostic => diagnostic.Id == "DxCmd0001");
                Assert.True(
                    1 == actual,
                    $"{name}: expected 1 DxCmd0001, found {actual}. All: {Describe(diagnostics)}"
                );
            }
        }
    }
}
