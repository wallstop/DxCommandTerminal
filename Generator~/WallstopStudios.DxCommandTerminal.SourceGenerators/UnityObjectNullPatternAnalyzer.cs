namespace WallstopStudios.DxCommandTerminal.SourceGenerators
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /*
        Type-checked enforcement for context rule 25: Unity fake null defeats ?., ??,
        ??=, truthiness, `is null` / `is not null`, and object.ReferenceEquals, so all of
        them are banned on UnityEngine.Object receivers. GUIStyle is the other
        IntPtr-backed fake-null type and is covered by the same rules. The analyzer ships
        inside the generator payload scoped through the Runtime assembly, so every
        compilation that references the package - including consumers' - gets these
        diagnostics, and the package's own -warnaserror csc.rsp turns any violation into
        a compile failure. Syntax and semantic-model APIs only, so the same binary runs
        on every Roslyn host from Unity 2021.3 (3.9) through current.
    */
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnityObjectNullPatternAnalyzer : DiagnosticAnalyzer
    {
        private const string Category = "Usage";

        private const string UnityObjectDisplayFullName = "global::UnityEngine.Object";

        private const string GUIStyleDisplayFullName = "global::UnityEngine.GUIStyle";

        private const string ReferenceEqualsName = "ReferenceEquals";

        private static readonly DiagnosticDescriptor ConditionalAccessDescriptor =
            new DiagnosticDescriptor(
                "DxCmd0001",
                "Conditional access on a Unity fake-null receiver",
                "'?.' bypasses Unity fake-null checks on '{0}'; use explicit == null / != null",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        private static readonly DiagnosticDescriptor CoalesceDescriptor = new DiagnosticDescriptor(
            "DxCmd0002",
            "Null coalescing on a Unity fake-null operand",
            "'??' keeps destroyed Unity objects because fake null is not managed null; "
                + "use explicit == null / != null",
            Category,
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        private static readonly DiagnosticDescriptor TruthinessDescriptor =
            new DiagnosticDescriptor(
                "DxCmd0003",
                "Unity fake-null receiver used as a bool",
                "'{0}' used as a bool bypasses Unity fake-null checks; use explicit == null / != null",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        private static readonly DiagnosticDescriptor NullPatternDescriptor =
            new DiagnosticDescriptor(
                "DxCmd0004",
                "Null pattern on a Unity fake-null receiver",
                "'is null' / 'is not null' uses reference equality and bypasses Unity fake-null "
                    + "checks; use == null / != null",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        private static readonly DiagnosticDescriptor ReferenceEqualsDescriptor =
            new DiagnosticDescriptor(
                "DxCmd0005",
                "ReferenceEquals on a Unity fake-null argument",
                "ReferenceEquals bypasses Unity fake-null checks; use == null / != null",
                Category,
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                ConditionalAccessDescriptor,
                CoalesceDescriptor,
                TruthinessDescriptor,
                NullPatternDescriptor,
                ReferenceEqualsDescriptor
            );

        private static void AnalyzeConditionalAccess(SyntaxNodeAnalysisContext context)
        {
            ConditionalAccessExpressionSyntax expression = (ConditionalAccessExpressionSyntax)
                context.Node;
            ITypeSymbol receiverType = GetTypeOfTypeExpression(context, expression.Expression);
            if (!IsFakeNullType(receiverType))
            {
                return;
            }

            Report(context, ConditionalAccessDescriptor, expression, receiverType);
        }

        private static void AnalyzeCoalesce(SyntaxNodeAnalysisContext context)
        {
            BinaryExpressionSyntax expression = (BinaryExpressionSyntax)context.Node;
            ITypeSymbol operandType = GetTypeOfTypeExpression(context, expression.Left);
            if (!IsFakeNullType(operandType))
            {
                return;
            }

            Report(context, CoalesceDescriptor, expression, operandType);
        }

        private static void AnalyzeCoalesceAssignment(SyntaxNodeAnalysisContext context)
        {
            AssignmentExpressionSyntax expression = (AssignmentExpressionSyntax)context.Node;
            ITypeSymbol targetType = GetTypeOfTypeExpression(context, expression.Left);
            if (!IsFakeNullType(targetType))
            {
                return;
            }

            Report(context, CoalesceDescriptor, expression, targetType);
        }

        private static void AnalyzeConversion(OperationAnalysisContext context)
        {
            IConversionOperation operation = (IConversionOperation)context.Operation;
            CommonConversion conversion = operation.Conversion;
            if (!conversion.IsUserDefined || conversion.MethodSymbol == null)
            {
                return;
            }

            if (operation.Type?.SpecialType != SpecialType.System_Boolean)
            {
                return;
            }

            ITypeSymbol operandType = operation.Operand?.Type;
            if (!IsFakeNullType(operandType))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    TruthinessDescriptor,
                    operation.Syntax.GetLocation(),
                    operandType.ToDisplayString()
                )
            );
        }

        private static void AnalyzeReferenceEquals(SyntaxNodeAnalysisContext context)
        {
            InvocationExpressionSyntax invocation = (InvocationExpressionSyntax)context.Node;
            SymbolInfo symbolInfo = context.SemanticModel.GetSymbolInfo(
                invocation.Expression,
                context.CancellationToken
            );
            if (
                !(symbolInfo.Symbol is IMethodSymbol method)
                || !string.Equals(method.Name, ReferenceEqualsName, StringComparison.Ordinal)
                || method.ContainingType?.SpecialType != SpecialType.System_Object
            )
            {
                return;
            }

            SeparatedSyntaxList<ArgumentSyntax> arguments = invocation.ArgumentList.Arguments;
            for (int i = 0; i < arguments.Count; i++)
            {
                if (IsFakeNullType(GetTypeOfTypeExpression(context, arguments[i].Expression)))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(ReferenceEqualsDescriptor, invocation.GetLocation())
                    );
                    return;
                }
            }
        }

        private static void AnalyzeNullPattern(SyntaxNodeAnalysisContext context)
        {
            IsPatternExpressionSyntax expression = (IsPatternExpressionSyntax)context.Node;
            if (!ContainsNullLiteral(expression.Pattern))
            {
                return;
            }

            ITypeSymbol operandType = GetTypeOfTypeExpression(context, expression.Expression);
            if (!IsFakeNullType(operandType))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(NullPatternDescriptor, expression.GetLocation())
            );
        }

        private static bool ContainsNullLiteral(SyntaxNode node)
        {
            foreach (SyntaxToken token in node.DescendantTokens())
            {
                if (token.IsKind(SyntaxKind.NullKeyword))
                {
                    return true;
                }
            }

            return false;
        }

        private static ITypeSymbol GetTypeOfTypeExpression(
            SyntaxNodeAnalysisContext context,
            ExpressionSyntax expression
        )
        {
            return context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        }

        private static bool IsFakeNullType(ITypeSymbol type)
        {
            if (type == null || type.TypeKind == TypeKind.Error)
            {
                return false;
            }

            return HasFullName(type, GUIStyleDisplayFullName) || IsUnityEngineObjectDerived(type);
        }

        private static bool IsUnityEngineObjectDerived(ITypeSymbol type)
        {
            ITypeSymbol current = type;
            while (current != null)
            {
                if (HasFullName(current, UnityObjectDisplayFullName))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static bool HasFullName(ITypeSymbol type, string displayFullName)
        {
            return string.Equals(
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                displayFullName,
                StringComparison.Ordinal
            );
        }

        private static void Report(
            SyntaxNodeAnalysisContext context,
            DiagnosticDescriptor descriptor,
            SyntaxNode node,
            ITypeSymbol receiverType
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(descriptor, node.GetLocation(), receiverType.ToDisplayString())
            );
        }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                AnalyzeConditionalAccess,
                SyntaxKind.ConditionalAccessExpression
            );
            context.RegisterSyntaxNodeAction(AnalyzeCoalesce, SyntaxKind.CoalesceExpression);
            context.RegisterSyntaxNodeAction(
                AnalyzeCoalesceAssignment,
                SyntaxKind.CoalesceAssignmentExpression
            );
            context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
            context.RegisterSyntaxNodeAction(
                AnalyzeReferenceEquals,
                SyntaxKind.InvocationExpression
            );
            context.RegisterSyntaxNodeAction(AnalyzeNullPattern, SyntaxKind.IsPatternExpression);
        }
    }
}
