namespace WallstopStudios.DxCommandTerminal.SourceGenerators
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    [Generator]
    public sealed class CommandCatalogGenerator : ISourceGenerator
    {
        private const string AttributeMetadataName =
            "WallstopStudios.DxCommandTerminal.Attributes.RegisterCommandAttribute";

        private const string EntryMetadataName =
            "WallstopStudios.DxCommandTerminal.Backend.CommandCatalogEntry";

        internal const string ArgumentMetadataName =
            "WallstopStudios.DxCommandTerminal.Backend.CommandArg";

        private const string GeneratedHintName = "DxCommandTerminalCommandCatalog.g.cs";

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new CommandSyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not CommandSyntaxReceiver receiver)
            {
                return;
            }

            Compilation compilation = context.Compilation;

            /*
                Version-skew guard: the generated code compiles against the
                runtime contract types. When the consuming compilation carries
                an older runtime assembly that predates the catalog contract,
                emit nothing so discovery falls back to reflection instead of
                breaking the consumer build.
             */
            if (compilation.GetTypeByMetadataName(EntryMetadataName) == null)
            {
                return;
            }

            if (compilation.GetTypeByMetadataName(ArgumentMetadataName) == null)
            {
                return;
            }

            ITypeSymbol commandArgumentType = compilation.GetTypeByMetadataName(
                ArgumentMetadataName
            );

            List<CommandModel> commands = new List<CommandModel>();
            HashSet<IMethodSymbol> seenMethods = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (MethodDeclarationSyntax candidate in receiver.Candidates)
            {
                SemanticModel semanticModel = compilation.GetSemanticModel(candidate.SyntaxTree);
                IMethodSymbol method = semanticModel.GetDeclaredSymbol(
                    candidate,
                    context.CancellationToken
                );
                if (method == null)
                {
                    // Declarations inside inactive preprocessor branches and
                    // other exotic syntax contribute no symbol; legacy
                    // reflection discovery would not see them either.
                    continue;
                }

                /*
                    Partial methods surface as two distinct symbols (the
                    definition part and the implementation part); canonicalize
                    on the definition so the merged symbol is emitted once,
                    exactly as reflection's type.GetMethods sees it.
                 */
                IMethodSymbol canonicalMethod = method.PartialDefinitionPart ?? method;
                if (!seenMethods.Add(canonicalMethod))
                {
                    continue;
                }
                if (!TryParseAttribute(method, out CommandAttributeData attribute))
                {
                    continue;
                }

                CommandModel model = CommandModelBuilder.Build(
                    method,
                    attribute,
                    commandArgumentType
                );
                if (model != null)
                {
                    commands.Add(model);
                }
            }

            if (commands.Count == 0)
            {
                return;
            }

            string source = CatalogEmitter.Emit(commands);
            context.AddSource(GeneratedHintName, source);
        }

        private static bool IsRegisterCommand(INamedTypeSymbol attributeClass)
        {
            if (attributeClass == null)
            {
                return false;
            }

            return string.Equals(
                attributeClass.ToDisplayString(),
                AttributeMetadataName,
                StringComparison.Ordinal
            );
        }

        private static bool TryParseAttribute(
            IMethodSymbol method,
            out CommandAttributeData attribute
        )
        {
            attribute = null;
            foreach (AttributeData attributeData in method.GetAttributes())
            {
                if (!IsRegisterCommand(attributeData.AttributeClass))
                {
                    continue;
                }

                attribute = new CommandAttributeData();
                foreach (TypedConstant constructorArgument in attributeData.ConstructorArguments)
                {
                    if (constructorArgument.Value is string explicitName)
                    {
                        attribute.ExplicitName = explicitName;
                    }
                    else if (constructorArgument.Value is bool isDefault)
                    {
                        attribute.IsDefault = isDefault;
                    }
                }

                foreach (
                    KeyValuePair<
                        string,
                        TypedConstant
                    > namedArgument in attributeData.NamedArguments
                )
                {
                    switch (namedArgument.Key)
                    {
                        case "Name":
                            attribute.ExplicitName = namedArgument.Value.Value as string;
                            break;
                        case "MinArgCount":
                            if (namedArgument.Value.Value is int minArgCount)
                            {
                                attribute.MinArgCount = minArgCount;
                            }
                            break;
                        case "MaxArgCount":
                            if (namedArgument.Value.Value is int maxArgCount)
                            {
                                attribute.MaxArgCount = maxArgCount;
                            }
                            break;
                        case "Help":
                            attribute.Help = namedArgument.Value.Value as string;
                            break;
                        case "Hint":
                            attribute.Hint = namedArgument.Value.Value as string;
                            break;
                        case "AddToHistory":
                            if (namedArgument.Value.Value is bool addToHistory)
                            {
                                attribute.AddToHistory = addToHistory;
                            }
                            break;
                        case "EditorOnly":
                            if (namedArgument.Value.Value is bool editorOnly)
                            {
                                attribute.EditorOnly = editorOnly;
                            }
                            break;
                        case "DevelopmentOnly":
                            if (namedArgument.Value.Value is bool developmentOnly)
                            {
                                attribute.DevelopmentOnly = developmentOnly;
                            }
                            break;
                        case "Default":
                            if (namedArgument.Value.Value is bool isDefault)
                            {
                                attribute.IsDefault = isDefault;
                            }
                            break;
                    }
                }

                return true;
            }

            return false;
        }
    }

    internal sealed class CommandAttributeData
    {
        public string ExplicitName;
        public bool IsDefault;
        public int MinArgCount;
        public int MaxArgCount = -1;
        public string Help;
        public string Hint;
        public bool AddToHistory = true;
        public bool EditorOnly;
        public bool DevelopmentOnly;
    }

    internal sealed class CommandModel
    {
        public string CommandName;
        public string MethodName;
        public int MinArgCount;
        public int MaxArgCount;
        public string Help;
        public string Hint;
        public bool AddToHistory;
        public bool EditorOnly;
        public bool DevelopmentOnly;
        public bool IsDefault;

        // Non-null for valid (CommandArg[]) signatures.
        public bool HasValidSignature;

        // Valid signatures only: accessible handlers bind by direct delegate
        // creation; inaccessible ones by a cached exact-identity reflection
        // binding.
        public bool DirectlyBindable;

        public string ContainingTypeDisplay;

        public bool ContainingTypeIsUnbound;

        // The handler method name escaped for use as a C# identifier
        // (keyword names such as `@params`).
        public string MethodNameIdentifierDisplay;

        // Whether the handler method itself is a generic method definition.
        public bool IsMethodGeneric;

        // False when a parameter type or the method itself is open, dynamic,
        // or otherwise not addressable by an exact typeof signature.
        public bool ExactSignatureAddressable;

        // Full typeof-able expressions per parameter, in declaration order.
        // Managed references (ref/out/in) are emitted as
        // typeof(T).MakeByRefType() because typeof(T&) is not legal C#.
        public string[] ParameterTypeExpressions;
    }

    internal static class CommandModelBuilder
    {
        public static CommandModel Build(
            IMethodSymbol method,
            CommandAttributeData attribute,
            ITypeSymbol commandArgumentType
        )
        {
            // Legacy discovery walks static methods only (BindingFlags.Static);
            // instance methods attributed with RegisterCommandAttribute were
            // never registered and must stay unregistered.
            if (!method.IsStatic)
            {
                return null;
            }

            CommandModel model = new CommandModel
            {
                CommandName = NormalizeName(attribute.ExplicitName, method.Name),
                MethodName = method.Name,
                MinArgCount = attribute.MinArgCount,
                MaxArgCount = attribute.MaxArgCount,
                Help = attribute.Help,
                Hint = attribute.Hint,
                AddToHistory = attribute.AddToHistory,
                EditorOnly = attribute.EditorOnly,
                DevelopmentOnly = attribute.DevelopmentOnly,
                IsDefault = attribute.IsDefault,
                MethodNameIdentifierDisplay = EscapeIdentifier(method.Name),
                IsMethodGeneric = method.IsGenericMethod,
                ContainingTypeDisplay = BuildContainingTypeDisplay(
                    method.ContainingType,
                    out bool containingTypeIsUnbound
                ),
                ContainingTypeIsUnbound = containingTypeIsUnbound,
            };

            BuildSignature(method, commandArgumentType, containingTypeIsUnbound, model);

            if (model.HasValidSignature)
            {
                model.DirectlyBindable = IsAccessibleFromCatalog(method);
            }

            return model;
        }

        private static void BuildSignature(
            IMethodSymbol method,
            ITypeSymbol commandArgumentType,
            bool containingTypeIsUnbound,
            CommandModel model
        )
        {
            // Methods inside open generic types (or open generic methods
            // themselves) can never be bound to a closed delegate; legacy
            // discovery would fail inside Delegate.CreateDelegate for them.
            bool methodIsOpen = method.IsGenericMethod;

            List<IParameterSymbol> parameterList = new List<IParameterSymbol>(method.Parameters);

            List<string> parameterExpressions = new List<string>();
            bool exactSignatureAddressable = !methodIsOpen && !containingTypeIsUnbound;

            foreach (IParameterSymbol parameter in parameterList)
            {
                ITypeSymbol parameterType = parameter.Type;
                if (!IsAddressableType(parameterType))
                {
                    exactSignatureAddressable = false;
                }

                string display = parameterType.ToDisplayString(CatalogEmitter.FullyQualified);
                parameterExpressions.Add(
                    parameter.RefKind == RefKind.None
                        ? "typeof(" + display + ")"
                        : "typeof(" + display + ").MakeByRefType()"
                );
            }

            model.ParameterTypeExpressions = parameterExpressions.ToArray();
            model.ExactSignatureAddressable = exactSignatureAddressable;

            if (methodIsOpen || containingTypeIsUnbound)
            {
                model.HasValidSignature = false;
                return;
            }

            /*
                Mirrors the reflection signature check exactly: one parameter,
                passed by value (managed references make the CLR parameter type
                CommandArg[]&), of type CommandArg[].
             */
            model.HasValidSignature =
                parameterList.Count == 1
                && parameterList[0].RefKind == RefKind.None
                && IsCommandArgArray(parameterList[0].Type, commandArgumentType);
        }

        private static bool IsCommandArgArray(ITypeSymbol type, ITypeSymbol commandArgumentType)
        {
            if (type is not IArrayTypeSymbol arrayType)
            {
                return false;
            }

            if (
                commandArgumentType != null
                && SymbolEqualityComparer.Default.Equals(arrayType.ElementType, commandArgumentType)
            )
            {
                return true;
            }

            return string.Equals(
                arrayType.ElementType.ToDisplayString(),
                CommandCatalogGenerator.ArgumentMetadataName,
                StringComparison.Ordinal
            );
        }

        private static bool IsAddressableType(ITypeSymbol type)
        {
            switch (type.TypeKind)
            {
                case TypeKind.Dynamic:
                case TypeKind.Error:
                    return false;
                case TypeKind.TypeParameter:
                    return false;
                case TypeKind.FunctionPointer:
                    return false;
            }

            if (type is IArrayTypeSymbol arrayType)
            {
                return IsAddressableType(arrayType.ElementType);
            }

            if (type is IPointerTypeSymbol pointerType)
            {
                // Unmanaged pointers only; managed references (ref/out/in) are
                // modeled on IParameterSymbol.RefKind, not on the type symbol.
                return IsAddressableType(pointerType.PointedAtType);
            }

            if (type is INamedTypeSymbol namedType)
            {
                if (namedType.TypeParameters.Length > 0 && namedType.TypeArguments.Length == 0)
                {
                    // Generic definition; addressable only in unbound form,
                    // which the caller handles via ContainingTypeIsUnbound.
                    return false;
                }

                if (namedType.TypeArguments.Length == 0 && namedType.IsUnboundGenericType)
                {
                    return false;
                }

                foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
                {
                    if (!IsAddressableType(typeArgument))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        private static bool IsAccessibleFromCatalog(IMethodSymbol method)
        {
            if (!IsAccessibleDeclaration(method.DeclaredAccessibility))
            {
                return false;
            }

            for (
                INamedTypeSymbol containingType = method.ContainingType;
                containingType != null;
                containingType = containingType.ContainingType
            )
            {
                if (!IsAccessibleDeclaration(containingType.DeclaredAccessibility))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAccessibleDeclaration(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Public:
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    return true;
                default:
                    return false;
            }
        }

        /*
            Mirrors RegisterCommandAttribute.NormalizeName exactly: explicit
            names are stripped of spaces at construction, blank names fall
            back to inference, and the final name is space-stripped again.
         */
        public static string NormalizeName(string explicitName, string methodName)
        {
            string name = explicitName?.Replace(" ", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = InferCommandName(methodName);
            }

            return name.Replace(" ", string.Empty).Trim();
        }

        private static string InferCommandName(string methodName)
        {
            const string commandId = "COMMAND";
            int index = methodName.IndexOf(commandId, StringComparison.OrdinalIgnoreCase);

            // Method is prefixed, suffixed with, or contains "COMMAND".
            return 0 <= index ? methodName.Remove(index, commandId.Length) : methodName;
        }

        private static string BuildContainingTypeDisplay(
            INamedTypeSymbol containingType,
            out bool isUnbound
        )
        {
            isUnbound = HasOpenTypeChain(containingType);
            if (!isUnbound)
            {
                return containingType.ToDisplayString(CatalogEmitter.FullyQualified);
            }

            return BuildUnboundDisplay(containingType);
        }

        private static bool HasOpenTypeChain(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                if (ContainsTypeParameter(current))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsTypeParameter(ITypeSymbol type)
        {
            if (type is ITypeParameterSymbol)
            {
                return true;
            }

            if (type is not INamedTypeSymbol namedType)
            {
                return false;
            }

            if (namedType.IsUnboundGenericType)
            {
                return true;
            }

            if (namedType.TypeArguments.Length == 0)
            {
                return namedType.TypeParameters.Length > 0;
            }

            foreach (ITypeSymbol typeArgument in namedType.TypeArguments)
            {
                if (ContainsTypeParameter(typeArgument))
                {
                    return true;
                }
            }

            return false;
        }

        private static string EscapeIdentifier(string identifier)
        {
            if (SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None)
            {
                return "@" + identifier;
            }

            return identifier;
        }

        /*
            Renders `Ns.Outer<,>.Inner` so the emitter can address the
            declaration chain of an open generic type with a legal
            typeof(...) expression.
         */
        private static string BuildUnboundDisplay(INamedTypeSymbol type)
        {
            List<string> nestedNames = new List<string>();
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                nestedNames.Add(FormatName(current.Name, current.TypeParameters.Length));
            }

            INamedTypeSymbol outermost = type;
            while (outermost.ContainingType != null)
            {
                outermost = outermost.ContainingType;
            }

            INamespaceSymbol containingNamespace = outermost.ContainingNamespace;
            string namespacePrefix = string.Empty;
            if (containingNamespace != null && !containingNamespace.IsGlobalNamespace)
            {
                namespacePrefix = "global::" + containingNamespace.ToDisplayString() + ".";
            }

            nestedNames.Reverse();
            return namespacePrefix + string.Join(".", nestedNames);
        }

        private static string FormatName(string name, int arity)
        {
            if (SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None)
            {
                name = "@" + name;
            }

            if (arity > 0)
            {
                return name + "<" + new string(',', arity - 1) + ">";
            }

            return name;
        }
    }

    /*
        Collects plausible attributed method syntax first (any method with an
        attribute list); the exact attribute symbol is resolved later through
        the semantic model, which keeps qualified names, aliases, and
        `RegisterCommandAttribute` spellings supported.
     */
    internal sealed class CommandSyntaxReceiver : ISyntaxReceiver
    {
        public readonly List<MethodDeclarationSyntax> Candidates =
            new List<MethodDeclarationSyntax>();

        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is not MethodDeclarationSyntax method)
            {
                return;
            }

            foreach (AttributeListSyntax attributeList in method.AttributeLists)
            {
                if (attributeList.Attributes.Count > 0)
                {
                    Candidates.Add(method);
                    return;
                }
            }
        }
    }
}
