namespace WallstopStudios.DxCommandTerminal.SourceGenerators
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;

    internal static class CatalogEmitter
    {
        /*
            Generated code references every type through fully qualified
            global:: names so a consumer type declared in an enclosing
            namespace (for example `WallstopStudios.DxCommandTerminal.Func`)
            cannot hijack resolution; no using directives are emitted.
         */
        private const string ArgumentType =
            "global::WallstopStudios.DxCommandTerminal.Backend.CommandArg";

        private const string HandlerType = ArgumentType + "[]";

        private const string BinderType = "global::System.Action<" + HandlerType + ">";

        private const string BinderFactoryType = "global::System.Func<" + BinderType + ">";

        private const string EntryType =
            "global::WallstopStudios.DxCommandTerminal.Backend.CommandCatalogEntry";

        private const string ContextsType =
            "global::WallstopStudios.DxCommandTerminal.Backend.CommandExecutionContexts";

        private const string EntryListType =
            "global::System.Collections.Generic.List<" + EntryType + ">";

        private const string BindingFlagsType = "global::System.Reflection.BindingFlags";

        private const string MethodInfoType = "global::System.Reflection.MethodInfo";

        private const string BindingFlagsExpression =
            BindingFlagsType
            + ".Static | "
            + BindingFlagsType
            + ".Public | "
            + BindingFlagsType
            + ".NonPublic";

        private const string Indent1 = "    ";
        private const string Indent2 = Indent1 + Indent1;
        private const string Indent3 = Indent2 + Indent1;
        private const string Indent4 = Indent2 + Indent2;
        private const string Indent5 = Indent4 + Indent1;
        private const string Indent6 = Indent3 + Indent3;

        private const string BinderFieldPrefix = "_boundHandler";
        private const string BinderMethodPrefix = "BindHandler";

        /*
            Rough per-command emission budget (entry arguments plus a cached
            binder block when one is needed), used only to size the buffer in
            one shot; EnsureCapacity covers any underestimate. A compilation
            of N commands then costs one large allocation instead of the
            doubling chain from a fixed seed.
         */
        private const int PerCommandCapacityEstimate = 512;
        private const int BaseCapacityEstimate = 4096;

        internal static SymbolDisplayFormat FullyQualified =>
            SymbolDisplayFormat.FullyQualifiedFormat;

        /*
            One builder per thread, reused across compilations. Roslyn may run
            this generator for several compilations concurrently, so a plain
            static would race; ThreadStatic keeps each thread's buffer private.
            Large catalogs (thousands of commands) allocate hundreds of
            kilobytes per compilation otherwise.
         */
        [ThreadStatic]
        private static StringBuilder CachedBuilder;

        internal static string Emit(List<CommandModel> commands, bool preserveCatalog)
        {
            int capacity = BaseCapacityEstimate + PerCommandCapacityEstimate * commands.Count;
            StringBuilder builder = RentBuilder(capacity);
            bool needsUnboundFinder = false;

            foreach (CommandModel command in commands)
            {
                if (!command.HasValidSignature && !command.ExactSignatureAddressable)
                {
                    needsUnboundFinder = true;
                    break;
                }
            }

            AppendHeader(builder);
            if (preserveCatalog)
            {
                /*
                    Unity's linker honors [Preserve] as a root annotation. A
                    type-level preserve keeps only the type and its default
                    constructor, so the class attribute exists to keep the
                    type resolvable for the shell's GetType probe, and the
                    method attribute on Collect makes that entry method a
                    root; the linker's reachability walk from a root keeps
                    Build, the binder factories, and every directly created
                    handler delegate. Private handlers stay bound by name and
                    keep their per-site requirements.
                 */
                builder.Append(Indent2).AppendLine("[global::UnityEngine.Scripting.Preserve]");
            }
            builder.Append(Indent2).AppendLine("internal static class CommandCatalog");
            builder.Append(Indent2).AppendLine("{");
            builder
                .Append(Indent3)
                .Append("private static readonly ")
                .Append(EntryListType)
                .AppendLine(" Entries = Build();");
            builder.AppendLine();
            if (preserveCatalog)
            {
                builder.Append(Indent3).AppendLine("[global::UnityEngine.Scripting.Preserve]");
            }
            builder
                .Append(Indent3)
                .Append("public static void Collect(")
                .Append(EntryListType)
                .AppendLine(" entries)");
            builder.Append(Indent3).AppendLine("{");
            builder.Append(Indent4).AppendLine("if (entries == null)");
            builder.Append(Indent4).AppendLine("{");
            builder
                .Append(Indent5)
                .AppendLine("throw new global::System.ArgumentNullException(\"entries\");");
            builder.Append(Indent4).AppendLine("}");
            builder.AppendLine();
            builder.Append(Indent4).AppendLine("entries.AddRange(Entries);");
            builder.Append(Indent3).AppendLine("}");
            builder.AppendLine();
            builder
                .Append(Indent3)
                .Append("private static ")
                .Append(EntryListType)
                .AppendLine(" Build()");
            builder.Append(Indent3).AppendLine("{");
            builder
                .Append(Indent4)
                .Append(EntryListType)
                .AppendLine(" entries = new " + EntryListType + "();");

            for (int i = 0; i < commands.Count; i++)
            {
                EmitCommand(builder, i, commands[i]);
            }

            builder.AppendLine();
            builder.Append(Indent4).AppendLine("return entries;");
            builder.Append(Indent3).AppendLine("}");

            if (needsUnboundFinder)
            {
                EmitUnboundMethodFinder(builder);
            }

            for (int i = 0; i < commands.Count; i++)
            {
                CommandModel command = commands[i];
                if (command.HasValidSignature && !command.DirectlyBindable)
                {
                    EmitCachedBinder(builder, i, command);
                }
            }

            builder.Append(Indent2).AppendLine("}");
            builder.AppendLine("}");
            EmitPartialCompanions(builder, commands);
            string source = builder.ToString();
            ReturnBuilder(builder);
            return source;
        }

        private static StringBuilder RentBuilder(int minimumCapacity)
        {
            StringBuilder builder = CachedBuilder;
            CachedBuilder = null;
            if (builder == null)
            {
                return new StringBuilder(minimumCapacity);
            }

            builder.EnsureCapacity(minimumCapacity);
            return builder;
        }

        private static void ReturnBuilder(StringBuilder builder)
        {
            builder.Clear();
            CachedBuilder = builder;
        }

        private static void AppendHeader(StringBuilder builder)
        {
            builder.AppendLine("// <auto-generated>");
            builder.AppendLine(
                "//     Generated by WallstopStudios.DxCommandTerminal.SourceGenerators."
            );
            builder.AppendLine("//     Do not edit; changes are overwritten on every compilation.");
            builder.AppendLine("// </auto-generated>");
            builder.AppendLine("#pragma warning disable 612 // Obsolete member");
            builder.AppendLine("#pragma warning disable 618 // Obsolete member");
            builder.AppendLine("#pragma warning disable 1591 // Missing XML comment");
            builder.AppendLine();
            builder.AppendLine("namespace WallstopStudios.DxCommandTerminal.Generated");
            builder.AppendLine("{");
        }

        private static void EmitCommand(
            StringBuilder builder,
            int commandIndex,
            CommandModel command
        )
        {
            builder.AppendLine();
            builder.Append(Indent4).Append("// ").Append(commandIndex);
            builder.Append(": command '");
            foreach (char c in command.CommandName)
            {
                if (c != '\r' && c != '\n')
                {
                    builder.Append(c);
                }
            }

            builder.Append("' from method ").AppendLine(command.MethodName);
            builder.Append(Indent4).Append("entries.Add(new ").Append(EntryType).AppendLine("(");
            AppendLiteral(builder, command.CommandName);
            builder.AppendLine(",");
            AppendLiteral(builder, command.MethodName);
            builder.AppendLine(",");
            builder.Append(Indent4).Append("    ").Append(command.MinArgCount);
            builder.Append(", ").Append(command.MaxArgCount).AppendLine(",");
            AppendLiteral(builder, command.Help);
            builder.AppendLine(",");
            AppendLiteral(builder, command.Hint);
            builder.AppendLine(",");
            builder
                .Append(Indent4)
                .Append("    ")
                .AppendLine(command.AddToHistory ? "true," : "false,");
            builder
                .Append(Indent4)
                .Append("    ")
                .AppendLine(command.EditorOnly ? "true," : "false,");
            builder
                .Append(Indent4)
                .Append("    ")
                .AppendLine(command.DevelopmentOnly ? "true," : "false,");
            builder
                .Append(Indent4)
                .Append("    ")
                .AppendLine(command.IsDefault ? "true," : "false,");

            if (command.HasValidSignature)
            {
                if (command.DirectlyBindable)
                {
                    builder
                        .Append(Indent4)
                        .Append("    new ")
                        .Append(BinderFactoryType)
                        .AppendLine("(delegate");
                    builder.Append(Indent4).AppendLine("    {");
                    builder
                        .Append(Indent5)
                        .Append("return new ")
                        .Append(BinderType)
                        .Append("(")
                        .Append(command.ContainingTypeDisplay)
                        .Append(".")
                        .Append(command.MethodNameIdentifierDisplay)
                        .AppendLine(");");
                    builder.Append(Indent4).AppendLine("    }),");
                    builder.Append(Indent4).AppendLine("    null,");
                }
                else
                {
                    builder
                        .Append(Indent4)
                        .Append("    new ")
                        .Append(BinderFactoryType)
                        .Append("(")
                        .Append(BinderMethodPrefix)
                        .Append(commandIndex)
                        .AppendLine("),");
                    builder.Append(Indent4).AppendLine("    null,");
                }
            }
            else
            {
                builder.Append(Indent4).AppendLine("    null,");
                builder
                    .Append(Indent4)
                    .Append("    new global::System.Func<")
                    .Append(MethodInfoType)
                    .AppendLine(">(delegate");
                builder.Append(Indent4).AppendLine("    {");
                if (command.ExactSignatureAddressable)
                {
                    builder
                        .Append(Indent5)
                        .Append("return typeof(")
                        .Append(command.ContainingTypeDisplay)
                        .AppendLine(").GetMethod(");
                    AppendLiteral(builder, command.MethodName, Indent6);
                    builder.AppendLine(",");
                    builder.Append(Indent6).Append(BindingFlagsExpression).AppendLine(",");
                    builder.Append(Indent6).AppendLine("null,");
                    builder.Append(Indent6).AppendLine("new global::System.Type[]");
                    builder.Append(Indent6).AppendLine("{");
                    AppendParameterExpressions(
                        builder,
                        command.ParameterTypeExpressions,
                        Indent6 + "    "
                    );

                    builder.Append(Indent6).AppendLine("},");
                    builder.Append(Indent6).AppendLine("null");
                    builder.Append(Indent5).AppendLine(");");
                }
                else
                {
                    builder.Append(Indent5).AppendLine("return FindMethodByName(");
                    builder
                        .Append(Indent6)
                        .Append("typeof(")
                        .Append(command.ContainingTypeDisplay)
                        .AppendLine("),");
                    AppendLiteral(builder, command.MethodName, Indent6);
                    builder.AppendLine(",");
                    builder.Append(Indent6).Append(command.ParameterTypeExpressions.Length);
                    builder.AppendLine(",");
                    builder.Append(Indent6).AppendLine(command.IsMethodGeneric ? "true" : "false");
                    builder.Append(Indent5).AppendLine(");");
                }

                builder.Append(Indent4).AppendLine("    }),");
            }

            /*
                The execution-context set is emitted as a numeric cast: the
                catalog must not depend on enum member names staying stable
                across runtime versions, and the cast round-trips any
                combination the attribute carried.
             */
            builder.Append(Indent4).Append("    (").Append(ContextsType).Append(")");
            builder.Append(command.Contexts).AppendLine();
            builder.Append(Indent4).AppendLine("));");
        }

        private static void EmitUnboundMethodFinder(StringBuilder builder)
        {
            builder.AppendLine();
            builder
                .Append(Indent3)
                .Append("private static ")
                .Append(MethodInfoType)
                .AppendLine(" FindMethodByName(");
            builder.Append(Indent4).AppendLine("global::System.Type type,");
            builder.Append(Indent4).AppendLine("string name,");
            builder.Append(Indent4).AppendLine("int parameterCount,");
            builder.Append(Indent4).AppendLine("bool isGeneric");
            builder.Append(Indent3).AppendLine(")");
            builder.Append(Indent3).AppendLine("{");
            builder
                .Append(Indent4)
                .Append("global::System.Reflection.MethodInfo[] methods = type.GetMethods(")
                .AppendLine();
            builder.Append(Indent5).AppendLine(BindingFlagsExpression);
            builder.Append(Indent4).AppendLine(");");
            builder.Append(Indent4).AppendLine("for (int i = 0; i < methods.Length; i++)");
            builder.Append(Indent4).AppendLine("{");
            builder
                .Append(Indent5)
                .AppendLine("global::System.Reflection.MethodInfo method = methods[i];");
            builder.Append(Indent5).AppendLine("if (");
            builder.Append(Indent6).AppendLine("method.Name == name");
            builder
                .Append(Indent6)
                .AppendLine("&& method.GetParameters().Length == parameterCount");
            builder.Append(Indent6).AppendLine("&& method.IsGenericMethodDefinition == isGeneric");
            builder.Append(Indent5).AppendLine(")");
            builder.Append(Indent5).AppendLine("{");
            builder.Append(Indent6).AppendLine("return method;");
            builder.Append(Indent5).AppendLine("}");
            builder.Append(Indent4).AppendLine("}");
            builder.AppendLine();
            builder.Append(Indent4).AppendLine("return null;");
            builder.Append(Indent3).AppendLine("}");
        }

        private static void EmitCachedBinder(
            StringBuilder builder,
            int binderIndex,
            CommandModel command
        )
        {
            string fieldName =
                BinderFieldPrefix + binderIndex.ToString(CultureInfo.InvariantCulture);
            string methodName =
                BinderMethodPrefix + binderIndex.ToString(CultureInfo.InvariantCulture);

            builder.AppendLine();
            builder
                .Append(Indent3)
                .Append("private static ")
                .Append(BinderType)
                .Append(" ")
                .Append(fieldName)
                .AppendLine(";");
            builder.AppendLine();
            builder
                .Append(Indent3)
                .Append("private static ")
                .Append(BinderType)
                .Append(" ")
                .Append(methodName)
                .AppendLine("()");
            builder.Append(Indent3).AppendLine("{");
            builder.Append(Indent4).Append("if (").Append(fieldName).AppendLine(" == null)");
            builder.Append(Indent4).AppendLine("{");
            if (command.PartialBindable)
            {
                /*
                    The companion was emitted beside the catalog; the binder
                    call keeps the cached-field shape so repeated discovery
                    cycles reuse one delegate.
                */
                builder
                    .Append(Indent5)
                    .Append(fieldName)
                    .Append(" = ")
                    .Append(command.ContainingTypeDisplay)
                    .Append(".DxCommandTerminalBinder.Bind")
                    .Append(binderIndex)
                    .AppendLine("();");
            }
            else
            {
                builder
                    .Append(Indent5)
                    .Append(fieldName)
                    .Append(" = (")
                    .Append(BinderType)
                    .AppendLine(")");
                builder.Append(Indent5).AppendLine("global::System.Delegate.CreateDelegate(");
                builder.Append(Indent6).Append("typeof(").Append(BinderType).AppendLine("),");
                builder
                    .Append(Indent6)
                    .Append("typeof(")
                    .Append(command.ContainingTypeDisplay)
                    .AppendLine(").GetMethod(");
                AppendLiteral(builder, command.MethodName, Indent6 + "    ");
                builder.AppendLine(",");
                builder
                    .Append(Indent6)
                    .Append("    ")
                    .Append(BindingFlagsExpression)
                    .AppendLine(",");
                builder.Append(Indent6).AppendLine("    null,");
                builder.Append(Indent6).AppendLine("    new global::System.Type[]");
                builder.Append(Indent6).AppendLine("    {");
                AppendParameterExpressions(
                    builder,
                    command.ParameterTypeExpressions,
                    Indent6 + "        "
                );

                builder.Append(Indent6).AppendLine("    },");
                builder.Append(Indent6).AppendLine("    null");
                builder.Append(Indent5).AppendLine(")");
                builder.Append(Indent5).AppendLine(");");
            }

            builder.Append(Indent4).AppendLine("}");
            builder.AppendLine();
            builder.Append(Indent4).Append("return ").Append(fieldName).AppendLine(";");
            builder.Append(Indent3).AppendLine("}");
        }

        /*
            Emits the partial companions that expose inaccessible command
            handlers to the catalog without reflection. Commands arrive in the
            receiver's declaration order, which is the same order the catalog
            body and binder indices use, so grouping in first-appearance order
            keeps the output deterministic for a given compilation.
        */
        private static void EmitPartialCompanions(
            StringBuilder builder,
            List<CommandModel> commands
        )
        {
            List<CompanionNamespace> namespaces = new List<CompanionNamespace>();
            Dictionary<string, CompanionNamespace> namespaceIndex = new Dictionary<
                string,
                CompanionNamespace
            >(StringComparer.Ordinal);
            for (int i = 0; i < commands.Count; ++i)
            {
                CommandModel command = commands[i];
                if (!command.PartialBindable)
                {
                    continue;
                }

                string namespaceKey = command.PartialNamespace ?? string.Empty;
                if (
                    !namespaceIndex.TryGetValue(namespaceKey, out CompanionNamespace namespaceGroup)
                )
                {
                    namespaceGroup = new CompanionNamespace { Name = command.PartialNamespace };
                    namespaceIndex.Add(namespaceKey, namespaceGroup);
                    namespaces.Add(namespaceGroup);
                }

                string chainKey = JoinChainNames(command.PartialChain);
                if (!namespaceGroup.ChainIndex.TryGetValue(chainKey, out CompanionChain chainGroup))
                {
                    chainGroup = new CompanionChain { Chain = command.PartialChain };
                    namespaceGroup.ChainIndex.Add(chainKey, chainGroup);
                    namespaceGroup.Chains.Add(chainGroup);
                }

                chainGroup.Commands.Add(
                    new CompanionCommand
                    {
                        CommandIndex = i,
                        MethodIdentifier = command.MethodNameIdentifierDisplay,
                    }
                );
            }

            if (namespaces.Count == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("// Partial companions for inaccessible command handlers; the");
            builder.AppendLine("// catalog binds them without reflection. Member names are");
            builder.AppendLine("// generator-owned: a partial holder declaring its own");
            builder.AppendLine("// DxCommandTerminalBinder member would collide at compile time.");
            foreach (CompanionNamespace namespaceGroup in namespaces)
            {
                bool hasNamespace = namespaceGroup.Name != null;
                if (hasNamespace)
                {
                    builder.Append("namespace ").Append(namespaceGroup.Name).AppendLine();
                    builder.AppendLine("{");
                }

                string indent = hasNamespace ? Indent1 : string.Empty;
                foreach (CompanionChain chainGroup in namespaceGroup.Chains)
                {
                    foreach (PartialChainLevel level in chainGroup.Chain)
                    {
                        builder
                            .Append(indent)
                            .Append("partial ")
                            .Append(level.Keyword)
                            .Append(' ')
                            .AppendLine(level.Name);
                        builder.Append(indent).AppendLine("{");
                        indent += Indent1;
                    }

                    builder
                        .Append(indent)
                        .AppendLine("internal static class DxCommandTerminalBinder");
                    builder.Append(indent).AppendLine("{");
                    string methodIndent = indent + Indent1;
                    foreach (CompanionCommand companionCommand in chainGroup.Commands)
                    {
                        builder
                            .Append(methodIndent)
                            .Append("internal static ")
                            .Append(BinderType)
                            .Append(" Bind")
                            .Append(companionCommand.CommandIndex)
                            .AppendLine("()");
                        builder.Append(methodIndent).AppendLine("{");
                        builder
                            .Append(methodIndent)
                            .Append(Indent1)
                            .Append("return new ")
                            .Append(BinderType)
                            .Append("(")
                            .Append(companionCommand.MethodIdentifier)
                            .AppendLine(");");
                        builder.Append(methodIndent).AppendLine("}");
                    }

                    builder.Append(indent).AppendLine("}");
                    for (
                        int levelIndex = chainGroup.Chain.Length - 1;
                        0 <= levelIndex;
                        --levelIndex
                    )
                    {
                        indent = indent.Substring(0, indent.Length - Indent1.Length);
                        builder.Append(indent).AppendLine("}");
                    }
                }

                if (hasNamespace)
                {
                    builder.AppendLine("}");
                }
            }
        }

        private static string JoinChainNames(PartialChainLevel[] chain)
        {
            string joined = chain[0].Name;
            for (int i = 1; i < chain.Length; ++i)
            {
                joined += "." + chain[i].Name;
            }

            return joined;
        }

        /*
            Emits one parameter typeof-expression per line. The separator is
            a comma prefix (all but the first element) so the loop needs no
            index.
         */
        private static void AppendParameterExpressions(
            StringBuilder builder,
            string[] parameterTypeExpressions,
            string indent
        )
        {
            bool first = true;
            foreach (string expression in parameterTypeExpressions)
            {
                builder.Append(indent);
                if (!first)
                {
                    builder.Append(",");
                }

                builder.AppendLine(expression);
                first = false;
            }
        }

        private static void AppendLiteral(StringBuilder builder, string value)
        {
            AppendLiteral(builder, value, Indent4 + "    ");
        }

        private static void AppendLiteral(StringBuilder builder, string value, string indent)
        {
            builder.Append(indent);
            builder.Append(
                value == null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true)
            );
        }

        private sealed class CompanionCommand
        {
            public int CommandIndex;
            public string MethodIdentifier;
        }

        private sealed class CompanionChain
        {
            public PartialChainLevel[] Chain;
            public List<CompanionCommand> Commands = new List<CompanionCommand>();
        }

        private sealed class CompanionNamespace
        {
            public string Name;
            public List<CompanionChain> Chains = new List<CompanionChain>();
            public Dictionary<string, CompanionChain> ChainIndex = new Dictionary<
                string,
                CompanionChain
            >(StringComparer.Ordinal);
        }
    }
}
