namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Attributes;
    using Backend;
    using DataStructures;
    using NUnit.Framework;

    public sealed class CommandCatalogTests
    {
        private const string CatalogTypeName = "WallstopStudios.DxCommandTerminal.Generated.CommandCatalog";

        /*
            The pre-#223 discovery oracle: a direct reflection sweep over the
            assemblies this lane knows carry commands. RegisteredCommands is
            catalog-first now, so the sweep is what pins "lists the same
            commands" without the surface re-scanning those assemblies.
        */
        private static Dictionary<string, MethodInfo> ReflectDeclaredCommands(params Type[] anchors)
        {
            const BindingFlags discoveryFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Dictionary<string, MethodInfo> declared = new(StringComparer.OrdinalIgnoreCase);
            foreach (Type anchor in anchors)
            {
                Assembly assembly = anchor.Assembly;
                foreach (Type type in assembly.GetTypes())
                {
                    foreach (MethodInfo method in type.GetMethods(discoveryFlags))
                    {
                        if (Attribute.GetCustomAttribute(method, typeof(RegisterCommandAttribute)) is not RegisterCommandAttribute attribute)
                        {
                            continue;
                        }

                        attribute.NormalizeName(method);
                        declared[attribute.Name] = method;
                    }
                }
            }

            return declared;
        }

        [Test]
        public void GeneratedCatalogIsPresentInTestAssembly()
        {
            Type catalogType = typeof(CommandCatalogTests).Assembly.GetType(CatalogTypeName, false);
            Assert.That(catalogType != null, "The source generator did not emit a command catalog for the test " + "assembly; shell discovery fell back to reflection for it.");
        }

        [Test]
        public void GeneratedCatalogIsPresentInRuntimeAssembly()
        {
            Type catalogType = typeof(BuiltInCommands).Assembly.GetType(CatalogTypeName, false);
            Assert.That(
                catalogType != null,
                "The source generator did not emit a command catalog for the runtime " + "assembly; built-in commands fell back to reflection discovery."
            );
        }

        [Test]
        public void CatalogDiscoveryRegistersEveryLegacyCommandWithTheSameMetadata()
        {
            /*
                Registration parity: RegisteredCommands is catalog-first and so
                is the registration path, so this pins that the discovered
                surface and the registered surface stay the same set with the
                same metadata. Reflection parity is pinned separately by
                RegisteredCommandsListsTheSameCommandsReflectionFinds.
            */
            (MethodInfo method, RegisterCommandAttribute attribute)[] legacy = CommandShell.RegisteredCommands.Value;

            Assert.IsNotEmpty(legacy, "Sanity: expected the oracle to find declared commands");

            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands();

            List<string> legacyNames = legacy.Select(tuple => tuple.attribute.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach ((MethodInfo method, RegisterCommandAttribute attribute) in legacy)
            {
                string commandName = attribute.Name;
                bool registered = shell.AutoRegisteredCommands.Contains(commandName);
                Assert.IsTrue(
                    registered,
                    $"Command '{commandName}' (method {method.Name}) was discovered by the " + "compatibility surface but not registered by the catalog path"
                );

                if (!shell.Commands.TryGetValue(commandName, out CommandInfo info))
                {
                    Assert.Fail($"Command '{commandName}' is auto-registered but missing from the shell");
                    continue;
                }

                Assert.AreEqual(attribute.MinArgCount, info.minArgCount, $"Command '{commandName}' has mismatched min bounds");
                int? expectedMaxArgCount = attribute.MaxArgCount < 0 ? null : attribute.MaxArgCount;
                Assert.AreEqual(expectedMaxArgCount, info.maxArgCount, $"Command '{commandName}' has mismatched max bounds");
                Assert.AreEqual(attribute.Help ?? string.Empty, info.help ?? string.Empty, $"Command '{commandName}' has mismatched help text");
                Assert.AreEqual(attribute.Hint, info.hint, $"Command '{commandName}' has mismatched hint text");
                Assert.AreEqual(attribute.AddToHistory, info.addToHistory, $"Command '{commandName}' has mismatched history policy");
            }

            Assert.AreEqual(legacyNames.Count, shell.AutoRegisteredCommands.Count, "The catalog path must register exactly the declared command set");
        }

        [Test]
        public void CatalogEntryGetHandlerReturnsTheSameDelegateOnASecondCall()
        {
            Type catalogType = typeof(CommandCatalogTests).Assembly.GetType(CatalogTypeName, false);
            Assert.That(catalogType != null, "Sanity: the test assembly carries a catalog");

            MethodInfo collect = catalogType.GetMethod("Collect", BindingFlags.Public | BindingFlags.Static);
            List<CommandCatalogEntry> entries = new();
            collect.Invoke(null, new object[] { entries });
            CommandCatalogEntry entry = entries.Find(candidate => candidate.IsValid && 0 < candidate.Name.Length);
            Assert.That(entry != null, "Sanity: the catalog carries a valid command");

            Action<CommandArg[]> first = entry.GetHandler();
            Action<CommandArg[]> second = entry.GetHandler();
            Assert.That(ReferenceEquals(first, second), "A second bind re-allocated the handler delegate");

            /*
                The memoized delegate is the one registration installs: a full
                registration cycle re-binds every catalog command, and the
                handler the shell stores must stay the same instance.
            */
            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands();
            Assert.IsTrue(shell.Commands.TryGetValue(entry.Name, out CommandInfo registered), $"Command '{entry.Name}' was not registered");
            shell.ClearAutoRegisteredCommands();
            shell.InitializeAutoRegisteredCommands();
            Assert.That(ReferenceEquals(registered.handler, shell.Commands[entry.Name].handler), "A second registration cycle re-allocated the handler delegate");
        }

        [Test]
        public void RegisteredCommandsListsTheSameCommandsReflectionFinds()
        {
            Dictionary<string, MethodInfo> expected = ReflectDeclaredCommands(typeof(CommandCatalogTests), typeof(BuiltInCommands));
            Assert.That(0 < expected.Count, "Sanity: the swept assemblies declare commands");

            Dictionary<string, MethodInfo> actualMethods = new(StringComparer.OrdinalIgnoreCase);
            (MethodInfo method, RegisterCommandAttribute attribute)[] discovered = CommandShell.RegisteredCommands.Value;
            foreach ((MethodInfo method, RegisterCommandAttribute attribute) in discovered)
            {
                actualMethods[attribute.Name] = method;
            }

            foreach (KeyValuePair<string, MethodInfo> pair in expected)
            {
                Assert.IsTrue(
                    actualMethods.TryGetValue(pair.Key, out MethodInfo actualMethod),
                    $"Command '{pair.Key}' was found by reflection but is missing from " + "RegisteredCommands"
                );
                Assert.AreEqual(pair.Value, actualMethod, $"Command '{pair.Key}' resolved to a different handler method");
            }
        }
    }
}
