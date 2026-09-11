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
        internal static SymbolDisplayFormat FullyQualified =>
            SymbolDisplayFormat.FullyQualifiedFormat;

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

        private const string BinderFieldNameFormat = "_boundHandler{0}";
        private const string BinderMethodNameFormat = "BindHandler{0}";

        internal static string Emit(List<CommandModel> commands)
        {
            StringBuilder builder = new StringBuilder(4096);
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
            builder.AppendLine(Indent2 + "internal static class CommandCatalog");
            builder.AppendLine(Indent2 + "{");
            builder.AppendLine(
                Indent3 + "private static readonly " + EntryListType + " Entries = Build();"
            );
            builder.AppendLine();
            builder.AppendLine(
                Indent3 + "public static void Collect(" + EntryListType + " entries)"
            );
            builder.AppendLine(Indent3 + "{");
            builder.AppendLine(Indent4 + "if (entries == null)");
            builder.AppendLine(Indent4 + "{");
            builder.AppendLine(
                Indent5 + "throw new global::System.ArgumentNullException(\"entries\");"
            );
            builder.AppendLine(Indent4 + "}");
            builder.AppendLine();
            builder.AppendLine(Indent4 + "entries.AddRange(Entries);");
            builder.AppendLine(Indent3 + "}");
            builder.AppendLine();
            builder.AppendLine(Indent3 + "private static " + EntryListType + " Build()");
            builder.AppendLine(Indent3 + "{");
            builder.AppendLine(Indent4 + EntryListType + " entries = new " + EntryListType + "();");

            for (int i = 0; i < commands.Count; i++)
            {
                EmitCommand(builder, i, commands[i]);
            }

            builder.AppendLine();
            builder.AppendLine(Indent4 + "return entries;");
            builder.AppendLine(Indent3 + "}");

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

            builder.AppendLine(Indent2 + "}");
            builder.AppendLine("}");
            return builder.ToString();
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
            builder.AppendLine(
                Indent4
                    + "// "
                    + commandIndex.ToString(CultureInfo.InvariantCulture)
                    + ": command '"
                    + command.CommandName.Replace("\r", string.Empty).Replace("\n", string.Empty)
                    + "' from method "
                    + command.MethodName
            );
            builder.AppendLine(Indent4 + "entries.Add(new " + EntryType + "(");
            AppendLiteral(builder, command.CommandName);
            builder.AppendLine(",");
            AppendLiteral(builder, command.MethodName);
            builder.AppendLine(",");
            builder.AppendLine(
                Indent4
                    + "    "
                    + command.MinArgCount.ToString(CultureInfo.InvariantCulture)
                    + ", "
                    + command.MaxArgCount.ToString(CultureInfo.InvariantCulture)
                    + ","
            );
            AppendLiteral(builder, command.Help);
            builder.AppendLine(",");
            AppendLiteral(builder, command.Hint);
            builder.AppendLine(",");
            builder.AppendLine(Indent4 + "    " + (command.AddToHistory ? "true" : "false") + ",");
            builder.AppendLine(Indent4 + "    " + (command.EditorOnly ? "true" : "false") + ",");
            builder.AppendLine(
                Indent4 + "    " + (command.DevelopmentOnly ? "true" : "false") + ","
            );
            builder.AppendLine(Indent4 + "    " + (command.IsDefault ? "true" : "false") + ",");

            if (command.HasValidSignature)
            {
                if (command.DirectlyBindable)
                {
                    builder.AppendLine(Indent4 + "    new " + BinderFactoryType + "(delegate");
                    builder.AppendLine(Indent4 + "    {");
                    builder.AppendLine(
                        Indent5
                            + "return new "
                            + BinderType
                            + "("
                            + command.ContainingTypeDisplay
                            + "."
                            + command.MethodNameIdentifierDisplay
                            + ");"
                    );
                    builder.AppendLine(Indent4 + "    }),");
                    builder.AppendLine(Indent4 + "    null,");
                }
                else
                {
                    builder.AppendLine(
                        Indent4
                            + "    new "
                            + BinderFactoryType
                            + "("
                            + Format(BinderMethodNameFormat, commandIndex)
                            + "),"
                    );
                    builder.AppendLine(Indent4 + "    null,");
                }
            }
            else
            {
                builder.AppendLine(Indent4 + "    null,");
                builder.AppendLine(
                    Indent4 + "    new global::System.Func<" + MethodInfoType + ">(delegate"
                );
                builder.AppendLine(Indent4 + "    {");
                if (command.ExactSignatureAddressable)
                {
                    builder.AppendLine(
                        Indent5 + "return typeof(" + command.ContainingTypeDisplay + ").GetMethod("
                    );
                    AppendLiteral(builder, command.MethodName, Indent6);
                    builder.AppendLine(",");
                    builder.AppendLine(Indent6 + BindingFlagsExpression + ",");
                    builder.AppendLine(Indent6 + "null,");
                    builder.AppendLine(Indent6 + "new global::System.Type[]");
                    builder.AppendLine(Indent6 + "{");
                    AppendParameterExpressions(
                        builder,
                        command.ParameterTypeExpressions,
                        Indent6 + "    "
                    );

                    builder.AppendLine(Indent6 + "},");
                    builder.AppendLine(Indent6 + "null");
                    builder.AppendLine(Indent5 + ");");
                }
                else
                {
                    builder.AppendLine(Indent5 + "return FindMethodByName(");
                    builder.AppendLine(Indent6 + "typeof(" + command.ContainingTypeDisplay + "),");
                    AppendLiteral(builder, command.MethodName, Indent6);
                    builder.AppendLine(",");
                    builder.AppendLine(
                        Indent6
                            + command.ParameterTypeExpressions.Length.ToString(
                                CultureInfo.InvariantCulture
                            )
                            + ","
                    );
                    builder.AppendLine(Indent6 + (command.IsMethodGeneric ? "true" : "false"));
                    builder.AppendLine(Indent5 + ");");
                }

                builder.AppendLine(Indent4 + "    }),");
            }

            /*
                The execution-context set is emitted as a numeric cast: the
                catalog must not depend on enum member names staying stable
                across runtime versions, and the cast round-trips any
                combination the attribute carried.
             */
            builder.AppendLine(
                Indent4
                    + "    ("
                    + ContextsType
                    + ")"
                    + command.Contexts.ToString(CultureInfo.InvariantCulture)
            );
            builder.AppendLine(Indent4 + "));");
        }

        private static void EmitUnboundMethodFinder(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine(Indent3 + "private static " + MethodInfoType + " FindMethodByName(");
            builder.AppendLine(Indent4 + "global::System.Type type,");
            builder.AppendLine(Indent4 + "string name,");
            builder.AppendLine(Indent4 + "int parameterCount,");
            builder.AppendLine(Indent4 + "bool isGeneric");
            builder.AppendLine(Indent3 + ")");
            builder.AppendLine(Indent3 + "{");
            builder.AppendLine(
                Indent4 + "global::System.Reflection.MethodInfo[] methods = type.GetMethods("
            );
            builder.AppendLine(Indent5 + BindingFlagsExpression);
            builder.AppendLine(Indent4 + ");");
            builder.AppendLine(Indent4 + "for (int i = 0; i < methods.Length; i++)");
            builder.AppendLine(Indent4 + "{");
            builder.AppendLine(
                Indent5 + "global::System.Reflection.MethodInfo method = methods[i];"
            );
            builder.AppendLine(Indent5 + "if (");
            builder.AppendLine(Indent6 + "method.Name == name");
            builder.AppendLine(Indent6 + "&& method.GetParameters().Length == parameterCount");
            builder.AppendLine(Indent6 + "&& method.IsGenericMethodDefinition == isGeneric");
            builder.AppendLine(Indent5 + ")");
            builder.AppendLine(Indent5 + "{");
            builder.AppendLine(Indent6 + "return method;");
            builder.AppendLine(Indent5 + "}");
            builder.AppendLine(Indent4 + "}");
            builder.AppendLine();
            builder.AppendLine(Indent4 + "return null;");
            builder.AppendLine(Indent3 + "}");
        }

        private static void EmitCachedBinder(
            StringBuilder builder,
            int binderIndex,
            CommandModel command
        )
        {
            string fieldName = Format(BinderFieldNameFormat, binderIndex);
            string methodName = Format(BinderMethodNameFormat, binderIndex);

            builder.AppendLine();
            builder.AppendLine(Indent3 + "private static " + BinderType + " " + fieldName + ";");
            builder.AppendLine();
            builder.AppendLine(Indent3 + "private static " + BinderType + " " + methodName + "()");
            builder.AppendLine(Indent3 + "{");
            builder.AppendLine(Indent4 + "if (" + fieldName + " == null)");
            builder.AppendLine(Indent4 + "{");
            builder.AppendLine(Indent5 + fieldName + " = (" + BinderType + ")");
            builder.AppendLine(Indent5 + "global::System.Delegate.CreateDelegate(");
            builder.AppendLine(Indent6 + "typeof(" + BinderType + "),");
            builder.AppendLine(
                Indent6 + "typeof(" + command.ContainingTypeDisplay + ").GetMethod("
            );
            AppendLiteral(builder, command.MethodName, Indent6 + "    ");
            builder.AppendLine(",");
            builder.AppendLine(Indent6 + "    " + BindingFlagsExpression + ",");
            builder.AppendLine(Indent6 + "    null,");
            builder.AppendLine(Indent6 + "    new global::System.Type[]");
            builder.AppendLine(Indent6 + "    {");
            AppendParameterExpressions(
                builder,
                command.ParameterTypeExpressions,
                Indent6 + "        "
            );

            builder.AppendLine(Indent6 + "    },");
            builder.AppendLine(Indent6 + "    null");
            builder.AppendLine(Indent5 + ")");
            builder.AppendLine(Indent5 + ");");
            builder.AppendLine(Indent4 + "}");
            builder.AppendLine();
            builder.AppendLine(Indent4 + "return " + fieldName + ";");
            builder.AppendLine(Indent3 + "}");
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
                builder.AppendLine(indent + (first ? "" : ",") + expression);
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

        private static string Format(string format, int index)
        {
            return string.Format(CultureInfo.InvariantCulture, format, index);
        }
    }
}
