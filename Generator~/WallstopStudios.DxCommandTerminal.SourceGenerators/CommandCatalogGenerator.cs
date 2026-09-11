namespace WallstopStudios.DxCommandTerminal.SourceGenerators
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.Threading;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    [Generator]
    public sealed class CommandCatalogGenerator : ISourceGenerator
    {
        internal const string ArgumentMetadataName =
            "WallstopStudios.DxCommandTerminal.Backend.CommandArg";

        // Matches RegisterCommandAttribute's Contexts default so attributed
        // commands that omit Contexts keep unrestricted eligibility.
        internal const int AllExecutionContexts = 7;

        private const string AttributeMetadataName =
            "WallstopStudios.DxCommandTerminal.Attributes.RegisterCommandAttribute";

        private const string EntryMetadataName =
            "WallstopStudios.DxCommandTerminal.Backend.CommandCatalogEntry";

        private const string ContextsMetadataName =
            "WallstopStudios.DxCommandTerminal.Backend.CommandExecutionContexts";

        private const string GeneratedHintName = "DxCommandTerminalCommandCatalog.g.cs";

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

                /*
                    AllowMultiple defaults to false on the attribute, so a
                    method carries at most one match and the first is the only
                    one. Named arguments always win over constructor arguments,
                    matching C# application order.
                 */
                attribute = new CommandAttributeData();
                INamedTypeSymbol attributeClass = attributeData.AttributeClass;
                IMethodSymbol attributeConstructor = attributeData.AttributeConstructor;
                ImmutableArray<IParameterSymbol> constructorParameters =
                    attributeConstructor.Parameters;
                ImmutableArray<TypedConstant> constructorArguments =
                    attributeData.ConstructorArguments;
                for (int i = 0; i < constructorArguments.Length; i++)
                {
                    // Resolve constructor arguments by parameter name instead
                    // of type, so overload or order changes in the attribute
                    // cannot silently bind to the wrong field.
                    ApplyArgument(
                        attribute,
                        constructorParameters[i].Name,
                        constructorArguments[i]
                    );
                }

                foreach (
                    KeyValuePair<
                        string,
                        TypedConstant
                    > namedArgument in attributeData.NamedArguments
                )
                {
                    ApplyArgument(attribute, namedArgument.Key, namedArgument.Value);
                }

                return true;
            }

            return false;
        }

        private static void ApplyArgument(
            CommandAttributeData attribute,
            string argumentName,
            TypedConstant argument
        )
        {
            switch (argumentName)
            {
                case "commandName":
                case "Name":
                    attribute.ExplicitName = argument.Value as string;
                    break;
                case "isDefault":
                case "Default":
                    attribute.IsDefault = argument.Value is bool isDefault && isDefault;
                    break;
                case "MinArgCount":
                    attribute.MinArgCount = argument.Value is int minArgCount ? minArgCount : 0;
                    break;
                case "MaxArgCount":
                    attribute.MaxArgCount = argument.Value is int maxArgCount ? maxArgCount : -1;
                    break;
                case "Help":
                    attribute.Help = argument.Value as string;
                    break;
                case "Hint":
                    attribute.Hint = argument.Value as string;
                    break;
                case "AddToHistory":
                    attribute.AddToHistory = argument.Value is bool addToHistory && addToHistory;
                    break;
                case "EditorOnly":
                    attribute.EditorOnly = argument.Value is bool editorOnly && editorOnly;
                    break;
                case "DevelopmentOnly":
                    attribute.DevelopmentOnly =
                        argument.Value is bool developmentOnly && developmentOnly;
                    break;
                case "Contexts":
                    attribute.Contexts = argument.Value is int contexts
                        ? contexts
                        : AllExecutionContexts;
                    break;
            }
        }

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
            ITypeSymbol entryType = compilation.GetTypeByMetadataName(EntryMetadataName);
            ITypeSymbol commandArgumentType = compilation.GetTypeByMetadataName(
                ArgumentMetadataName
            );
            ITypeSymbol contextsType = compilation.GetTypeByMetadataName(ContextsMetadataName);
            if (entryType == null || commandArgumentType == null || contextsType == null)
            {
                return;
            }

            List<CommandModel> commands = new List<CommandModel>();
            HashSet<IMethodSymbol> seenMethods = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (MethodDeclarationSyntax candidate in receiver.Candidates)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
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

            /*
                The generated catalog can only name types whose declaration
                chain is accessible from same-assembly code. For a handler in
                a private nested or file-local class, every emission path
                (method-group bind, cached binder, rejected-command accessor)
                would emit an illegal typeof(...). Dropping just that command
                would silently lose it, so the assembly gets no catalog at all
                and the shell falls back to reflection, which finds everything
                the compatibility path has always found.
             */
            foreach (CommandModel command in commands)
            {
                if (!command.TypeChainNameable)
                {
                    return;
                }
            }

            string source = CatalogEmitter.Emit(commands);
            context.AddSource(GeneratedHintName, source);
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
        public int Contexts = CommandCatalogGenerator.AllExecutionContexts;
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
        public int Contexts;

        // Non-null for valid (CommandArg[]) signatures.
        public bool HasValidSignature;

        /*
            Valid signatures only. True when the generated catalog can bind
            the handler without reflection: accessible method, accessible
            declaration chain, and a void return (the shell dispatches
            Action<CommandArg[]>; any other return type fails delegate
            creation exactly as the reflection path does).
         */
        public bool DirectlyBindable;

        /*
            Whether every declaration in the containing-type chain can be
            named from same-assembly code. False for private nested and
            file-local holders; such commands force the whole assembly to the
            reflection path because every emission form needs a legal
            typeof(...) for the chain.
         */
        public bool TypeChainNameable;

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

        /*
            Full typeof-able expressions per parameter, in declaration order.
            Managed references (ref/out/in) are emitted as
            typeof(T).MakeByRefType() because typeof(T&) is not legal C#.
            Null when nothing needs them (valid signatures that bind directly);
            the unbound finder only consumes the length.
         */
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
                Contexts = attribute.Contexts,
                MethodNameIdentifierDisplay = EscapeIdentifier(method.Name),
                IsMethodGeneric = method.IsGenericMethod,
                ContainingTypeDisplay = BuildContainingTypeDisplay(
                    method.ContainingType,
                    out bool containingTypeIsUnbound
                ),
                ContainingTypeIsUnbound = containingTypeIsUnbound,
            };

            BuildSignature(method, commandArgumentType, containingTypeIsUnbound, model);

            if (model.HasValidSignature && model.DirectlyBindable)
            {
                // Nothing else is needed; the emitter binds the method group
                // without parameter expressions.
                return model;
            }

            BuildParameterExpressions(method, model);
            return model;
        }

        /*
            Mirrors RegisterCommandAttribute.NormalizeName exactly: explicit
            names are stripped of spaces at construction, blank names fall
            back to inference, and the final result is space-stripped again.
            The guards make the mirror allocation-free when there is nothing
            to strip (string.Replace and string.Trim return the original
            instance unchanged, but only after a search).
         */
        public static string NormalizeName(string explicitName, string methodName)
        {
            string name = explicitName;
            if (name != null && name.Contains(" "))
            {
                name = name.Replace(" ", string.Empty);
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = InferCommandName(methodName);
            }

            // Method names are identifiers, so inference cannot produce
            // spaces or surrounding whitespace; the guard keeps this a
            // no-op pass for the common case while preserving the runtime
            // attribute's exact semantics.
            if (name.Contains(" "))
            {
                name = name.Replace(" ", string.Empty);
            }

            return name.Trim();
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
            bool signatureAddressable = !methodIsOpen && !containingTypeIsUnbound;

            List<IParameterSymbol> parameterList = new List<IParameterSymbol>(method.Parameters);

            /*
                Validity first, without building any strings: one value
                parameter of type CommandArg[], mirroring the reflection
                signature check exactly (managed references make the CLR
                parameter type CommandArg[]&).
             */
            model.HasValidSignature =
                signatureAddressable
                && parameterList.Count == 1
                && parameterList[0].RefKind == RefKind.None
                && IsCommandArgArray(parameterList[0].Type, commandArgumentType);

            model.DirectlyBindable =
                model.HasValidSignature && method.ReturnsVoid && IsAccessibleFromCatalog(method);

            /*
                Exact-signature addressability drives the rejected-command
                accessor form; parameter types that typeof cannot name (open
                generics, dynamic) fall back to the name-based finder.
             */
            bool exactSignatureAddressable = signatureAddressable;
            foreach (IParameterSymbol parameter in parameterList)
            {
                if (!IsAddressableType(parameter.Type))
                {
                    exactSignatureAddressable = false;
                    break;
                }
            }

            model.ExactSignatureAddressable = exactSignatureAddressable;
            model.TypeChainNameable = IsTypeChainNameable(method.ContainingType);
        }

        /*
            Builds the parameter typeof-expressions used by cached reflection
            binders and rejected-command accessors. Skipped for handlers the
            emitter binds directly, which is the common all-public case.
         */
        private static void BuildParameterExpressions(IMethodSymbol method, CommandModel model)
        {
            List<string> parameterExpressions = new List<string>(method.Parameters.Length);
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                string display = parameter.Type.ToDisplayString(CatalogEmitter.FullyQualified);
                parameterExpressions.Add(
                    parameter.RefKind == RefKind.None
                        ? "typeof(" + display + ")"
                        : "typeof(" + display + ").MakeByRefType()"
                );
            }

            model.ParameterTypeExpressions = parameterExpressions.ToArray();
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
                if (0 < namedType.TypeParameters.Length && namedType.TypeArguments.Length == 0)
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

        /*
            Whether the method itself can be named from same-assembly code.
            The containing-type chain is judged separately by
            IsTypeChainNameable.
         */
        private static bool IsAccessibleFromCatalog(IMethodSymbol method)
        {
            return IsAccessibleDeclaration(method.DeclaredAccessibility);
        }

        /*
            A type can be written in the generated catalog only when every
            declaration in its chain is visible to same-assembly code. Note
            `protected internal` qualifies (the internal half applies inside
            the assembly), but plain `protected` and `private` do not.
         */
        private static bool IsTypeChainNameable(INamedTypeSymbol containingType)
        {
            for (
                INamedTypeSymbol current = containingType;
                current != null;
                current = current.ContainingType
            )
            {
                if (!IsAccessibleDeclaration(current.DeclaredAccessibility))
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
                return 0 < namedType.TypeParameters.Length;
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

            if (0 < arity)
            {
                // Generator-time formatting only; string.Concat keeps the
                // arity suffix a single allocation.
                return string.Concat(name, "<", new string(',', arity - 1), ">");
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
                if (0 < attributeList.Attributes.Count)
                {
                    Candidates.Add(method);
                    return;
                }
            }
        }
    }
}
