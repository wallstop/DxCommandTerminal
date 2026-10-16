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
        private const string ArgumentType = "global::WallstopStudios.DxCommandTerminal.Backend.CommandArg";

        private const string HandlerType = ArgumentType + "[]";

        private const string BinderType = "global::System.Action<" + HandlerType + ">";

        private const string EntryType = "global::WallstopStudios.DxCommandTerminal.Backend.CommandCatalogEntry";

        private const string ContextsType = "global::WallstopStudios.DxCommandTerminal.Backend.CommandExecutionContexts";

        private const string EntryListType = "global::System.Collections.Generic.List<" + EntryType + ">";

        private const string BindingFlagsType = "global::System.Reflection.BindingFlags";

        private const string MethodInfoType = "global::System.Reflection.MethodInfo";

        private const string BindingFlagsExpression = BindingFlagsType + ".Static | " + BindingFlagsType + ".Public | " + BindingFlagsType + ".NonPublic";

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
        private const int EntriesPerMethod = 64;
        private const int HandlersPerBinder = 64;

        internal static SymbolDisplayFormat FullyQualified => SymbolDisplayFormat.FullyQualifiedFormat;

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
            /* One read: emitting never adds to or removes from the list. */
            int commandCount = commands.Count;
            int capacity = BaseCapacityEstimate + PerCommandCapacityEstimate * commandCount;
            StringBuilder builder = RentBuilder(capacity);
            bool needsUnboundFinder = false;
            bool needsPackedDecoder = false;

            foreach (CommandModel command in commands)
            {
                if (!command.HasValidSignature && !command.ExactSignatureAddressable)
                {
                    needsUnboundFinder = true;
                    break;
                }
            }

            /*
                Valid entries carry their declaring type's reflection-style
                name through the shared HolderNames table: catalogs answer for
                many commands in few types, so per-entry text stays a table
                read and the emission does not grow with the namespace depth.
             */
            List<string> holderNames = new List<string>();
            Dictionary<string, int> holderIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            int[] holderIndicesByCommand = new int[commandCount];
            for (int i = 0; i < commandCount; i++)
            {
                CommandModel command = commands[i];
                string holderName = command.DeclaringTypeName;
                if (!command.HasValidSignature || holderName == null)
                {
                    holderIndicesByCommand[i] = -1;
                    continue;
                }

                if (!holderIndices.TryGetValue(holderName, out int holderIndex))
                {
                    holderIndex = holderNames.Count;
                    holderIndices.Add(holderName, holderIndex);
                    holderNames.Add(holderName);
                }

                holderIndicesByCommand[i] = holderIndex;
            }

            int?[] sharedMetadata = FindSharedMetadata(commands, holderIndicesByCommand, out int?[] indexedSlots, out List<List<int>> indexedChunks);

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
            if (0 < holderNames.Count)
            {
                builder.Append(Indent3).AppendLine("private static readonly global::System.String[] HolderNames = " + "new global::System.String[]");
                builder.Append(Indent3).AppendLine("{");
                foreach (string holderName in holderNames)
                {
                    builder.Append(Indent4).AppendLine(SymbolDisplay.FormatLiteral(holderName, quote: true) + ",");
                }

                builder.Append(Indent3).AppendLine("};");
                builder.AppendLine();
            }

            builder.Append(Indent3).Append("private static readonly ").Append(EntryListType).AppendLine(" Entries = Build();");
            builder.AppendLine();
            if (preserveCatalog)
            {
                builder.Append(Indent3).AppendLine("[global::UnityEngine.Scripting.Preserve]");
            }
            builder.Append(Indent3).Append("public static void Collect(").Append(EntryListType).AppendLine(" entries)");
            builder.Append(Indent3).AppendLine("{");
            builder.Append(Indent4).AppendLine("if (entries == null)");
            builder.Append(Indent4).AppendLine("{");
            builder.Append(Indent5).AppendLine("throw new global::System.ArgumentNullException(\"entries\");");
            builder.Append(Indent4).AppendLine("}");
            builder.AppendLine();
            builder.Append(Indent4).AppendLine("entries.AddRange(Entries);");
            builder.Append(Indent3).AppendLine("}");
            builder.AppendLine();
            builder.Append(Indent3).Append("private static ").Append(EntryListType).AppendLine(" Build()");
            builder.Append(Indent3).AppendLine("{");
            builder.Append(Indent4).Append(EntryListType).AppendLine(" entries = new " + EntryListType + "();");

            if (commandCount <= EntriesPerMethod)
            {
                for (int i = 0; i < commandCount; i++)
                {
                    EmitCommand(builder, i, commands[i], holderIndicesByCommand[i], sharedMetadata[i], indexedSlots[i]);
                }
            }
            else
            {
                for (int start = 0; start < commandCount; start += EntriesPerMethod)
                {
                    builder.Append(Indent4).Append("AddEntries").Append(start.ToString(CultureInfo.InvariantCulture)).AppendLine("(entries);");
                }
            }

            builder.AppendLine();
            builder.Append(Indent4).AppendLine("return entries;");
            builder.Append(Indent3).AppendLine("}");

            // Large methods make first-use catalog compilation expensive on Unity's Mono.
            if (EntriesPerMethod < commandCount)
            {
                for (int start = 0; start < commandCount; start += EntriesPerMethod)
                {
                    builder.AppendLine();
                    builder
                        .Append(Indent3)
                        .Append("private static void AddEntries")
                        .Append(start.ToString(CultureInfo.InvariantCulture))
                        .Append("(")
                        .Append(EntryListType)
                        .AppendLine(" entries)");
                    builder.Append(Indent3).AppendLine("{");
                    int end = Math.Min(start + EntriesPerMethod, commandCount);
                    needsPackedDecoder |= EmitCommandRange(builder, start, end, commands, holderIndicesByCommand, sharedMetadata, indexedSlots);
                    builder.Append(Indent3).AppendLine("}");
                }
            }

            if (needsUnboundFinder)
            {
                EmitUnboundMethodFinder(builder);
            }

            for (int i = 0; i < commandCount; i++)
            {
                CommandModel command = commands[i];
                if (sharedMetadata[i] == i)
                {
                    EmitSharedEntryFactory(builder, i, command, holderIndicesByCommand[i]);
                }
                if (command.HasValidSignature)
                {
                    if (command.DirectlyBindable)
                    {
                        if (!indexedSlots[i].HasValue)
                        {
                            EmitDirectBinder(builder, i, command);
                        }
                    }
                    else
                    {
                        EmitCachedBinder(builder, i, command);
                    }
                }
            }

            if (0 < indexedChunks.Count)
            {
                EmitIndexedBinders(builder, commands, indexedChunks);
            }
            if (needsPackedDecoder)
            {
                EmitPackedDecoder(builder);
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
            builder.AppendLine("//     Generated by WallstopStudios.DxCommandTerminal.SourceGenerators.");
            builder.AppendLine("//     Do not edit; changes are overwritten on every compilation.");
            builder.AppendLine("// </auto-generated>");
            builder.AppendLine("#pragma warning disable 612 // Obsolete member");
            builder.AppendLine("#pragma warning disable 618 // Obsolete member");
            builder.AppendLine("#pragma warning disable 1591 // Missing XML comment");
            builder.AppendLine();
            builder.AppendLine("namespace WallstopStudios.DxCommandTerminal.Generated");
            builder.AppendLine("{");
        }

        private static bool EmitCommandRange(
            StringBuilder builder,
            int start,
            int end,
            List<CommandModel> commands,
            int[] holderIndices,
            int?[] sharedMetadata,
            int?[] indexedSlots
        )
        {
            bool emittedPacked = false;
            int index = start;
            while (index < end)
            {
                int next = index + 1;
                if (sharedMetadata[index].HasValue && indexedSlots[index].HasValue && commands[index].Help == null && commands[index].Hint == null)
                {
                    while (
                        next < end
                        && sharedMetadata[next] == sharedMetadata[index]
                        && indexedSlots[next] == indexedSlots[index].Value + next - index
                        && commands[next].Help == null
                        && commands[next].Hint == null
                    )
                    {
                        next++;
                    }
                }
                if (16 <= next - index)
                {
                    bool canPack = true;
                    for (int item = index; item < next; item++)
                    {
                        if (0 <= commands[item].CommandName.IndexOf('\0') || 0 <= commands[item].MethodName.IndexOf('\0'))
                        {
                            canPack = false;
                            break;
                        }
                    }
                    if (canPack)
                    {
                        EmitPackedRun(builder, index, next, commands, sharedMetadata[index].Value, indexedSlots[index].Value);
                        emittedPacked = true;
                    }
                    else
                    {
                        builder.AppendLine();
                        builder.Append(Indent4).AppendLine("{");
                        builder.Append(Indent5).AppendLine("string[] metadata = new string[]");
                        builder.Append(Indent5).AppendLine("{");
                        for (int item = index; item < next; item++)
                        {
                            CommandModel command = commands[item];
                            AppendLiteral(builder, command.CommandName, Indent6);
                            builder.AppendLine(",");
                            AppendLiteral(builder, command.MethodName, Indent6);
                            builder.AppendLine(",");
                            AppendLiteral(builder, command.Help, Indent6);
                            builder.AppendLine(",");
                            AppendLiteral(builder, command.Hint, Indent6);
                            builder.AppendLine(",");
                        }
                        builder.Append(Indent5).AppendLine("};");
                        builder.Append(Indent5).AppendLine("for (int offset = 0; offset < metadata.Length; offset += 4)");
                        builder.Append(Indent5).AppendLine("{");
                        builder.Append(Indent6).Append("entries.Add(CreateEntry").Append(sharedMetadata[index].Value.ToString(CultureInfo.InvariantCulture));
                        builder.Append("(metadata[offset], metadata[offset + 1], metadata[offset + 2], metadata[offset + 3], ");
                        builder.Append(indexedSlots[index].Value.ToString(CultureInfo.InvariantCulture)).AppendLine(" + offset / 4));");
                        builder.Append(Indent5).AppendLine("}");
                        builder.Append(Indent4).AppendLine("}");
                    }
                    index = next;
                }
                else
                {
                    while (index < next)
                    {
                        EmitCommand(builder, index, commands[index], holderIndices[index], sharedMetadata[index], indexedSlots[index]);
                        index++;
                    }
                }
            }
            return emittedPacked;
        }

        private static void EmitPackedRun(StringBuilder builder, int start, int end, List<CommandModel> commands, int representative, int slot)
        {
            StringBuilder packed = new StringBuilder();
            for (int index = start; index < end; index++)
            {
                if (index != start)
                {
                    packed.Append('\0');
                }
                packed.Append(commands[index].CommandName).Append('\0').Append(commands[index].MethodName);
            }
            builder.AppendLine();
            builder.Append(Indent4).AppendLine("{");
            builder.Append(Indent5).Append("string metadata = ").Append(SymbolDisplay.FormatLiteral(packed.ToString(), quote: true)).AppendLine(";");
            builder.Append(Indent5).AppendLine("int position = 0;");
            builder.Append(Indent5).Append("for (int offset = 0; offset < ").Append((end - start).ToString(CultureInfo.InvariantCulture)).AppendLine("; offset++)");
            builder.Append(Indent5).AppendLine("{");
            builder.Append(Indent6).Append("entries.Add(CreateEntry").Append(representative.ToString(CultureInfo.InvariantCulture));
            builder.Append("(ReadPackedMetadata(metadata, ref position), ReadPackedMetadata(metadata, ref position), null, null, ");
            builder.Append(slot.ToString(CultureInfo.InvariantCulture)).AppendLine(" + offset));");
            builder.Append(Indent5).AppendLine("}");
            builder.Append(Indent4).AppendLine("}");
        }

        private static void EmitPackedDecoder(StringBuilder builder)
        {
            builder.AppendLine();
            builder.Append(Indent3).AppendLine("private static string ReadPackedMetadata(string metadata, ref int position)");
            builder.Append(Indent3).AppendLine("{");
            builder.Append(Indent4).AppendLine("int end = metadata.IndexOf('\\0', position);");
            builder.Append(Indent4).AppendLine("if (end < 0) end = metadata.Length;");
            builder.Append(Indent4).AppendLine("string value = metadata.Substring(position, end - position);");
            builder.Append(Indent4).AppendLine("position = end < metadata.Length ? end + 1 : end;");
            builder.Append(Indent4).AppendLine("return value;");
            builder.Append(Indent3).AppendLine("}");
        }

        private static void EmitCommand(StringBuilder builder, int commandIndex, CommandModel command, int holderIndex, int? sharedMetadataIndex, int? indexedSlot)
        {
            builder.AppendLine();
            builder.Append(Indent4).Append("entries.Add(");
            if (sharedMetadataIndex.HasValue)
            {
                builder.Append("CreateEntry").Append(sharedMetadataIndex.Value.ToString(CultureInfo.InvariantCulture)).AppendLine("(");
            }
            else
            {
                builder.Append("new ").Append(EntryType).AppendLine("(");
            }
            AppendLiteral(builder, command.CommandName);
            builder.AppendLine(",");
            AppendLiteral(builder, command.MethodName);
            builder.AppendLine(",");
            if (!sharedMetadataIndex.HasValue)
            {
                builder.Append(Indent4).Append("    ").Append(command.MinArgCount.ToString(CultureInfo.InvariantCulture));
                builder.Append(", ").Append(command.MaxArgCount.ToString(CultureInfo.InvariantCulture)).AppendLine(",");
            }
            AppendLiteral(builder, command.Help);
            builder.AppendLine(",");
            AppendLiteral(builder, command.Hint);
            builder.AppendLine(",");
            if (!sharedMetadataIndex.HasValue)
            {
                builder.Append(Indent4).Append("    ").AppendLine(command.AddToHistory ? "true," : "false,");
                builder.Append(Indent4).Append("    ").AppendLine(command.EditorOnly ? "true," : "false,");
                builder.Append(Indent4).Append("    ").AppendLine(command.DevelopmentOnly ? "true," : "false,");
                builder.Append(Indent4).Append("    ").AppendLine(command.IsDefault ? "true," : "false,");
            }

            if (command.HasValidSignature)
            {
                if (command.DirectlyBindable)
                {
                    if (sharedMetadataIndex.HasValue)
                    {
                        builder.Append(Indent4).Append("    ").AppendLine(indexedSlot.Value.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder
                            .Append(Indent4)
                            .Append("    new global::System.Func<")
                            .Append(BinderType)
                            .Append(">(")
                            .Append(BinderMethodPrefix)
                            .Append(commandIndex.ToString(CultureInfo.InvariantCulture))
                            .AppendLine("),");
                        builder.Append(Indent4).AppendLine("    null,");
                    }
                }
                else
                {
                    builder.Append(Indent4).Append("    ").Append(BinderMethodPrefix).Append(commandIndex).AppendLine(",");
                    builder.Append(Indent4).AppendLine("    null,");
                }
            }
            else
            {
                builder.Append(Indent4).AppendLine("    null,");
                builder.Append(Indent4).AppendLine("    delegate");
                builder.Append(Indent4).AppendLine("    {");
                if (command.ExactSignatureAddressable)
                {
                    builder.Append(Indent5).Append("return typeof(").Append(command.ContainingTypeDisplay).AppendLine(").GetMethod(");
                    AppendLiteral(builder, command.MethodName, Indent6);
                    builder.AppendLine(",");
                    builder.Append(Indent6).Append(BindingFlagsExpression).AppendLine(",");
                    builder.Append(Indent6).AppendLine("null,");
                    builder.Append(Indent6).AppendLine("new global::System.Type[]");
                    builder.Append(Indent6).AppendLine("{");
                    AppendParameterExpressions(builder, command.ParameterTypeExpressions, Indent6 + "    ");

                    builder.Append(Indent6).AppendLine("},");
                    builder.Append(Indent6).AppendLine("null");
                    builder.Append(Indent5).AppendLine(");");
                }
                else
                {
                    builder.Append(Indent5).AppendLine("return FindMethodByName(");
                    builder.Append(Indent6).Append("typeof(").Append(command.ContainingTypeDisplay).AppendLine("),");
                    AppendLiteral(builder, command.MethodName, Indent6);
                    builder.AppendLine(",");
                    builder.Append(Indent6).Append(command.ParameterTypeExpressions.Length);
                    builder.AppendLine(",");
                    builder.Append(Indent6).AppendLine(command.IsMethodGeneric ? "true" : "false");
                    builder.Append(Indent5).AppendLine(");");
                }

                builder.Append(Indent4).AppendLine("    },");
            }

            /*
                The execution-context set is emitted as a numeric cast: the
                catalog must not depend on enum member names staying stable
                across runtime versions, and the cast round-trips any
                combination the attribute carried. The declaring type rides
                the shared HolderNames table so the shell can resolve valid
                handlers by exact signature for catalog-backed discovery
                without re-scanning assembly types; rejected commands resolve
                through their accessor and carry no holder.
             */
            if (!sharedMetadataIndex.HasValue)
            {
                builder.Append(Indent4).Append("    (").Append(ContextsType).Append(")(");
                builder.Append(command.Contexts.ToString(CultureInfo.InvariantCulture)).AppendLine("),");
                builder.Append(Indent4).Append("    ").AppendLine(0 <= holderIndex ? $"HolderNames[{holderIndex}]" : "null");
            }
            builder.Append(Indent4).AppendLine("));");
        }

        private static int?[] FindSharedMetadata(List<CommandModel> commands, int[] holderIndices, out int?[] indexedSlots, out List<List<int>> indexedChunks)
        {
            int count = commands.Count;
            int?[] sharedMetadata = new int?[count];
            int?[] slots = new int?[count];
            List<List<int>> chunks = new List<List<int>>();
            if (count <= EntriesPerMethod)
            {
                indexedSlots = slots;
                indexedChunks = chunks;
                return sharedMetadata;
            }
            Dictionary<(int Min, int Max, (bool History, bool Editor, bool Development, bool Default) Flags, int Contexts, int Holder), List<int>> groups =
                new Dictionary<(int, int, (bool, bool, bool, bool), int, int), List<int>>();
            List<List<int>> orderedGroups = new List<List<int>>();
            for (int index = 0; index < count; index++)
            {
                CommandModel command = commands[index];
                if (!command.HasValidSignature || !command.DirectlyBindable)
                {
                    continue;
                }
                (int, int, (bool, bool, bool, bool), int, int) key = (
                    command.MinArgCount,
                    command.MaxArgCount,
                    (command.AddToHistory, command.EditorOnly, command.DevelopmentOnly, command.IsDefault),
                    command.Contexts,
                    holderIndices[index]
                );
                if (!groups.TryGetValue(key, out List<int> indices))
                {
                    indices = new List<int>();
                    groups.Add(key, indices);
                    orderedGroups.Add(indices);
                }
                indices.Add(index);
            }
            foreach (List<int> indices in orderedGroups)
            {
                // A helper must replace enough repeated IL to repay its own first-use compilation.
                int groupCount = indices.Count;
                if (groupCount < 4)
                {
                    continue;
                }
                int representative = indices[0];
                /* A compiled switch must not resolve handlers from a skipped holder or eligibility group. */
                for (int start = 0; start < groupCount; start += HandlersPerBinder)
                {
                    int chunkIndex = chunks.Count;
                    int chunkCount = Math.Min(HandlersPerBinder, groupCount - start);
                    List<int> chunk = new List<int>(chunkCount);
                    for (int offset = 0; offset < chunkCount; offset++)
                    {
                        int commandIndex = indices[start + offset];
                        sharedMetadata[commandIndex] = representative;
                        slots[commandIndex] = chunkIndex * HandlersPerBinder + offset;
                        chunk.Add(commandIndex);
                    }
                    chunks.Add(chunk);
                }
            }
            indexedSlots = slots;
            indexedChunks = chunks;
            return sharedMetadata;
        }

        private static void EmitSharedEntryFactory(StringBuilder builder, int index, CommandModel command, int holderIndex)
        {
            builder.AppendLine();
            builder.Append(Indent3).Append("private static ").Append(EntryType).Append(" CreateEntry").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine("(");
            builder.Append(Indent4).AppendLine("string name, string methodName, string help, string hint,");
            builder.Append(Indent4).AppendLine("int slot)");
            builder.Append(Indent3).AppendLine("{");
            builder
                .Append(Indent4)
                .Append("return new ")
                .Append(EntryType)
                .Append("(name, methodName, ")
                .Append(command.MinArgCount.ToString(CultureInfo.InvariantCulture))
                .Append(", ")
                .Append(command.MaxArgCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine(", help, hint,");
            builder
                .Append(Indent5)
                .Append(command.AddToHistory ? "true, " : "false, ")
                .Append(command.EditorOnly ? "true, " : "false, ")
                .Append(command.DevelopmentOnly ? "true, " : "false, ")
                .Append(command.IsDefault ? "true, " : "false, ")
                .Append("new global::System.Func<")
                .Append(BinderType)
                .AppendLine(">(new IndexedBinder(slot).Bind), null,");
            builder.Append(Indent5).Append('(').Append(ContextsType).Append(")(").Append(command.Contexts.ToString(CultureInfo.InvariantCulture)).Append("), ");
            builder.AppendLine(0 <= holderIndex ? $"HolderNames[{holderIndex}]);" : "null);");
            builder.Append(Indent3).AppendLine("}");
        }

        private static void EmitDirectBinder(StringBuilder builder, int index, CommandModel command)
        {
            builder.AppendLine();
            builder
                .Append(Indent3)
                .Append("private static ")
                .Append(BinderType)
                .Append(' ')
                .Append(BinderMethodPrefix)
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .AppendLine("() =>");
            builder
                .Append(Indent4)
                .Append("new ")
                .Append(BinderType)
                .Append('(')
                .Append(command.ContainingTypeDisplay)
                .Append('.')
                .Append(command.MethodNameIdentifierDisplay)
                .AppendLine(");");
        }

        private static void EmitIndexedBinders(StringBuilder builder, List<CommandModel> commands, List<List<int>> chunks)
        {
            builder.AppendLine();
            builder.Append(Indent3).Append("private static ").Append(BinderType).AppendLine(" BindIndexed(int slot)");
            builder.Append(Indent3).AppendLine("{");
            builder.Append(Indent4).Append("switch (slot / ").Append(HandlersPerBinder.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
            builder.Append(Indent4).AppendLine("{");
            int chunkCount = chunks.Count;
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                string chunkName = chunkIndex.ToString(CultureInfo.InvariantCulture);
                builder
                    .Append(Indent5)
                    .Append("case ")
                    .Append(chunkName)
                    .Append(": return BindGroup")
                    .Append(chunkName)
                    .Append("(slot % ")
                    .Append(HandlersPerBinder.ToString(CultureInfo.InvariantCulture))
                    .AppendLine(");");
            }
            builder.Append(Indent5).AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(slot));");
            builder.Append(Indent4).AppendLine("}");
            builder.Append(Indent3).AppendLine("}");
            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                builder.AppendLine();
                builder
                    .Append(Indent3)
                    .Append("private static ")
                    .Append(BinderType)
                    .Append(" BindGroup")
                    .Append(chunkIndex.ToString(CultureInfo.InvariantCulture))
                    .AppendLine("(int offset)");
                builder.Append(Indent3).AppendLine("{");
                builder.Append(Indent4).AppendLine("switch (offset)");
                builder.Append(Indent4).AppendLine("{");
                List<int> chunk = chunks[chunkIndex];
                int handlerCount = chunk.Count;
                for (int offset = 0; offset < handlerCount; offset++)
                {
                    CommandModel command = commands[chunk[offset]];
                    builder
                        .Append(Indent5)
                        .Append("case ")
                        .Append(offset.ToString(CultureInfo.InvariantCulture))
                        .Append(": return new ")
                        .Append(BinderType)
                        .Append('(')
                        .Append(command.ContainingTypeDisplay)
                        .Append('.')
                        .Append(command.MethodNameIdentifierDisplay)
                        .AppendLine(");");
                }
                builder.Append(Indent5).AppendLine("default: throw new global::System.ArgumentOutOfRangeException(nameof(offset));");
                builder.Append(Indent4).AppendLine("}");
                builder.Append(Indent3).AppendLine("}");
            }
            builder.AppendLine();
            builder.Append(Indent3).AppendLine("private sealed class IndexedBinder");
            builder.Append(Indent3).AppendLine("{");
            builder.Append(Indent4).AppendLine("private readonly int _slot;");
            builder.Append(Indent4).AppendLine("public IndexedBinder(int slot) { _slot = slot; }");
            builder.Append(Indent4).Append("public ").Append(BinderType).AppendLine(" Bind() => BindIndexed(_slot);");
            builder.Append(Indent3).AppendLine("}");
        }

        private static void EmitUnboundMethodFinder(StringBuilder builder)
        {
            builder.AppendLine();
            builder.Append(Indent3).Append("private static ").Append(MethodInfoType).AppendLine(" FindMethodByName(");
            builder.Append(Indent4).AppendLine("global::System.Type type,");
            builder.Append(Indent4).AppendLine("string name,");
            builder.Append(Indent4).AppendLine("int parameterCount,");
            builder.Append(Indent4).AppendLine("bool isGeneric");
            builder.Append(Indent3).AppendLine(")");
            builder.Append(Indent3).AppendLine("{");
            builder.Append(Indent4).Append("global::System.Reflection.MethodInfo[] methods = type.GetMethods(").AppendLine();
            builder.Append(Indent5).AppendLine(BindingFlagsExpression);
            builder.Append(Indent4).AppendLine(");");
            builder.Append(Indent4).AppendLine("for (int i = 0; i < methods.Length; i++)");
            builder.Append(Indent4).AppendLine("{");
            builder.Append(Indent5).AppendLine("global::System.Reflection.MethodInfo method = methods[i];");
            builder.Append(Indent5).AppendLine("if (");
            builder.Append(Indent6).AppendLine("method.Name == name");
            builder.Append(Indent6).AppendLine("&& method.GetParameters().Length == parameterCount");
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

        private static void EmitCachedBinder(StringBuilder builder, int binderIndex, CommandModel command)
        {
            string fieldName = BinderFieldPrefix + binderIndex.ToString(CultureInfo.InvariantCulture);
            string methodName = BinderMethodPrefix + binderIndex.ToString(CultureInfo.InvariantCulture);

            builder.AppendLine();
            builder.Append(Indent3).Append("private static ").Append(BinderType).Append(" ").Append(fieldName).AppendLine(";");
            builder.AppendLine();
            builder.Append(Indent3).Append("private static ").Append(BinderType).Append(" ").Append(methodName).AppendLine("()");
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
                builder.Append(Indent5).Append(fieldName).Append(" = (").Append(BinderType).AppendLine(")");
                builder.Append(Indent5).AppendLine("global::System.Delegate.CreateDelegate(");
                builder.Append(Indent6).Append("typeof(").Append(BinderType).AppendLine("),");
                builder.Append(Indent6).Append("typeof(").Append(command.ContainingTypeDisplay).AppendLine(").GetMethod(");
                AppendLiteral(builder, command.MethodName, Indent6 + "    ");
                builder.AppendLine(",");
                builder.Append(Indent6).Append("    ").Append(BindingFlagsExpression).AppendLine(",");
                builder.Append(Indent6).AppendLine("    null,");
                builder.Append(Indent6).AppendLine("    new global::System.Type[]");
                builder.Append(Indent6).AppendLine("    {");
                AppendParameterExpressions(builder, command.ParameterTypeExpressions, Indent6 + "        ");

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
        private static void EmitPartialCompanions(StringBuilder builder, List<CommandModel> commands)
        {
            List<CompanionNamespace> namespaces = new List<CompanionNamespace>();
            Dictionary<string, CompanionNamespace> namespaceIndex = new Dictionary<string, CompanionNamespace>(StringComparer.Ordinal);
            for (int i = 0; i < commands.Count; ++i)
            {
                CommandModel command = commands[i];
                if (!command.PartialBindable)
                {
                    continue;
                }

                string namespaceKey = command.PartialNamespace ?? string.Empty;
                if (!namespaceIndex.TryGetValue(namespaceKey, out CompanionNamespace namespaceGroup))
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

                chainGroup.Commands.Add(new CompanionCommand { CommandIndex = i, MethodIdentifier = command.MethodNameIdentifierDisplay });
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
                        builder.Append(indent).Append("partial ").Append(level.Keyword).Append(' ').AppendLine(level.Name);
                        builder.Append(indent).AppendLine("{");
                        indent += Indent1;
                    }

                    builder.Append(indent).AppendLine("internal static class DxCommandTerminalBinder");
                    builder.Append(indent).AppendLine("{");
                    string methodIndent = indent + Indent1;
                    foreach (CompanionCommand companionCommand in chainGroup.Commands)
                    {
                        builder.Append(methodIndent).Append("internal static ").Append(BinderType).Append(" Bind").Append(companionCommand.CommandIndex).AppendLine("()");
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
                    for (int levelIndex = chainGroup.Chain.Length - 1; 0 <= levelIndex; --levelIndex)
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
        private static void AppendParameterExpressions(StringBuilder builder, string[] parameterTypeExpressions, string indent)
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
            builder.Append(value == null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true));
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
            public Dictionary<string, CompanionChain> ChainIndex = new Dictionary<string, CompanionChain>(StringComparer.Ordinal);
        }
    }
}
