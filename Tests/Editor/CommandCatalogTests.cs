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
        private const string CatalogTypeName =
            "WallstopStudios.DxCommandTerminal.Generated.CommandCatalog";

        [Test]
        public void GeneratedCatalogIsPresentInTestAssembly()
        {
            Type catalogType = typeof(CommandCatalogTests).Assembly.GetType(CatalogTypeName, false);
            Assert.That(
                catalogType != null,
                "The source generator did not emit a command catalog for the test "
                    + "assembly; shell discovery fell back to reflection for it."
            );
        }

        [Test]
        public void GeneratedCatalogIsPresentInRuntimeAssembly()
        {
            Type catalogType = typeof(BuiltInCommands).Assembly.GetType(CatalogTypeName, false);
            Assert.That(
                catalogType != null,
                "The source generator did not emit a command catalog for the runtime "
                    + "assembly; built-in commands fell back to reflection discovery."
            );
        }

        [Test]
        public void CatalogDiscoveryRegistersEveryLegacyCommandWithTheSameMetadata()
        {
            /*
               The compatibility surface is the discovery oracle: whatever it
               finds must be registered by the catalog-first path with the
               same normalized name and attribute metadata.
            */
            (MethodInfo method, RegisterCommandAttribute attribute)[] legacy = CommandShell
                .RegisteredCommands
                .Value;

            Assert.IsNotEmpty(legacy, "Sanity: expected the oracle to find declared commands");

            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands();

            List<string> legacyNames = legacy
                .Select(tuple => tuple.attribute.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach ((MethodInfo method, RegisterCommandAttribute attribute) in legacy)
            {
                string commandName = attribute.Name;
                bool registered = shell.AutoRegisteredCommands.Contains(commandName);
                Assert.IsTrue(
                    registered,
                    $"Command '{commandName}' (method {method.Name}) was discovered by the "
                        + "compatibility surface but not registered by the catalog path"
                );

                if (!shell.Commands.TryGetValue(commandName, out CommandInfo info))
                {
                    Assert.Fail(
                        $"Command '{commandName}' is auto-registered but missing from the shell"
                    );
                    continue;
                }

                Assert.AreEqual(
                    attribute.MinArgCount,
                    info.minArgCount,
                    $"Command '{commandName}' has mismatched min bounds"
                );
                int? expectedMaxArgCount = attribute.MaxArgCount < 0 ? null : attribute.MaxArgCount;
                Assert.AreEqual(
                    expectedMaxArgCount,
                    info.maxArgCount,
                    $"Command '{commandName}' has mismatched max bounds"
                );
                Assert.AreEqual(
                    attribute.Help ?? string.Empty,
                    info.help ?? string.Empty,
                    $"Command '{commandName}' has mismatched help text"
                );
                Assert.AreEqual(
                    attribute.Hint,
                    info.hint,
                    $"Command '{commandName}' has mismatched hint text"
                );
                Assert.AreEqual(
                    attribute.AddToHistory,
                    info.addToHistory,
                    $"Command '{commandName}' has mismatched history policy"
                );
            }

            Assert.AreEqual(
                legacyNames.Count,
                shell.AutoRegisteredCommands.Count,
                "The catalog path must register exactly the declared command set"
            );
        }
    }
}
