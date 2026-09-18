namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Emit;
    using Attributes;
    using Backend;
    using NUnit.Framework;

    /*
        Covers the ordered discovery-provider registry on CommandShell and the
        editor TypeCache discovery service: provider order, claim semantics
        (a claim replaces reflection for the reached assembly), dispose,
        exception containment, rejected-signature diagnostics through the
        shared registration pipeline, and TypeCache/reflection parity for the
        assemblies the index knows.
     */
    public sealed class CommandDiscoveryProviderTests
    {
        private static readonly string[] KnownDefaultCommandNames = CommandShell
            .RegisteredCommands.Value.Where(tuple => tuple.attribute.Default)
            .Select(tuple => tuple.attribute.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

#if UNITY_EDITOR
        /*
           Defined once: dynamic assemblies are non-collectible, and IL2CPP
           players do not support Reflection.Emit, so this case is editor-only.
        */
        private static readonly Assembly DynamicAssembly = CreateDynamicAssembly();

        private static Assembly CreateDynamicAssembly()
        {
            AssemblyName name = new($"DiscoveryProviderTestDynamic-{Guid.NewGuid():N}");
            return AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        }
#endif

        private readonly List<IDisposable> _registeredProviders = new();

        [SetUp]
        public void SetUp()
        {
            _registeredProviders.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable handle in _registeredProviders)
            {
                handle.Dispose();
            }

            _registeredProviders.Clear();
        }

        [Test]
        public void ProviderCommandsRegisterThroughSharedPipeline()
        {
            bool invoked = false;
            StubProvider provider = new StubProvider
            {
                Producer = _ => new List<CommandShell.AutoCommand>
                {
                    CreateInvokableCommand("provider-command", () => invoked = true),
                },
            };
            Register(provider);

            CommandShell shell = CreateShell();
            shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(
                shell.Commands.ContainsKey("provider-command"),
                "Provider-claimed commands should register through the shared pipeline"
            );
            Assert.IsTrue(
                shell.AutoRegisteredCommands.Contains("provider-command"),
                "Provider-claimed commands should be reported as auto-registered"
            );

            shell.RunCommand("provider-command");

            Assert.IsTrue(invoked, "Executing the command should run the provider's binder");
        }

        [Test]
        public void ProvidersConsultOnlyCataloglessAssemblies()
        {
            StubProvider provider = new StubProvider();
            Register(provider);

            CommandShell shell = CreateShell();
            shell.EnsureAutoCommandsRegistered();

            Assert.IsNotEmpty(
                provider.Consulted,
                "Sanity: expected at least one catalog-less candidate assembly to consult"
            );
            Assert.IsFalse(
                provider.Consulted.Contains(typeof(BuiltInCommands).Assembly),
                "Assemblies with a non-empty generated catalog must never reach a provider"
            );
            Assert.IsFalse(
                provider.Consulted.Contains(typeof(CommandDiscoveryProviderTests).Assembly),
                "Assemblies with a non-empty generated catalog must never reach a provider"
            );
        }

        [Test]
        public void FirstRegisteredProviderWins()
        {
            StubProvider first = new StubProvider { Producer = _ => CreateNamed("provider-first") };
            StubProvider second = new StubProvider
            {
                Producer = _ => CreateNamed("provider-second"),
            };
            Register(first);
            Register(second);

            CommandShell shell = CreateShell();
            shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(
                shell.Commands.ContainsKey("provider-first"),
                "The first registered provider should claim assemblies first"
            );
            Assert.IsFalse(
                shell.Commands.ContainsKey("provider-second"),
                "Later providers must not contribute to assemblies an earlier provider claimed"
            );
            Assert.IsEmpty(
                second.Consulted,
                "Later providers must not be consulted once an earlier provider claimed"
            );
        }

        [Test]
        public void UnclaimedAssembliesFallThroughToLaterProviders()
        {
            StubProvider passing = new StubProvider { ClaimFilter = _ => false };
            StubProvider claiming = new StubProvider
            {
                Producer = _ => CreateNamed("provider-second"),
            };
            Register(passing);
            Register(claiming);

            CommandShell shell = CreateShell();
            shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(
                shell.Commands.ContainsKey("provider-second"),
                "An assembly the first provider passed should reach the second provider"
            );
            Assert.IsNotEmpty(
                passing.Consulted,
                "Sanity: the first provider should have been consulted first"
            );
            CollectionAssert.IsSubsetOf(
                passing.Consulted,
                claiming.Consulted,
                "Assemblies the first provider passed must be the ones the second provider sees"
            );
        }

        [Test]
        public void DisposedProviderStopsContributing()
        {
            StubProvider first = new StubProvider { Producer = _ => CreateNamed("provider-first") };
            IDisposable handle = Register(first);
            handle.Dispose();

            StubProvider second = new StubProvider
            {
                Producer = _ => CreateNamed("provider-second"),
            };
            Register(second);

            CommandShell shell = CreateShell();
            shell.EnsureAutoCommandsRegistered();

            Assert.IsFalse(
                shell.Commands.ContainsKey("provider-first"),
                "A disposed provider must stop contributing commands"
            );
            Assert.IsTrue(
                shell.Commands.ContainsKey("provider-second"),
                "Discovery should continue through the providers that remain registered"
            );
        }

        [Test]
        public void DuplicateProviderRegistrationIsNoOp()
        {
            StubProvider provider = new StubProvider();
            Register(provider);
            Register(provider);

            CommandShell shell = CreateShell();
            shell.EnsureAutoCommandsRegistered();

            Assert.IsNotEmpty(
                provider.Consulted,
                "Sanity: the provider should have been consulted for the registration"
            );
            Assert.AreEqual(
                provider.Consulted.Count,
                provider.Consulted.Distinct().Count(),
                "A hook rerun must not double-register a provider instance"
            );
        }

        [Test]
        public void ProviderExceptionsAreContained()
        {
            StubProvider throwing = new StubProvider
            {
                Producer = _ => throw new InvalidOperationException("Provider failure"),
            };
            Register(throwing);

            CommandShell shell = CreateShell();
            shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(
                shell.AutoCommandsRegistered,
                "A throwing provider must not abort auto registration"
            );
            Assert.IsTrue(
                KnownDefaultCommandNames.All(name => shell.AutoRegisteredCommands.Contains(name)),
                "Discovery should complete through the remaining stages when a provider throws"
            );
        }

        [Test]
        public void ProviderRejectedSignaturesReportInvalidSignatureErrors()
        {
            MethodInfo invalidMethod = typeof(CommandDiscoveryProviderTests).GetMethod(
                nameof(InvalidSignatureMethod),
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.That(invalidMethod != null, "Sanity: expected the invalid-signature method");
            RegisterCommandAttribute attribute = new RegisterCommandAttribute();
            attribute.NormalizeName(invalidMethod);

            StubProvider provider = new StubProvider
            {
                Producer = _ => new List<CommandShell.AutoCommand>
                {
                    CommandShell.AutoCommand.FromReflected(invalidMethod, attribute),
                },
            };
            Register(provider);

            CommandShell shell = CreateShell();
            shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(
                shell.HasErrors,
                "Rejected provider signatures should queue the invalid-signature error"
            );
            Assert.IsFalse(
                shell.Commands.ContainsKey(attribute.Name),
                "Rejected signatures must not register a runnable command"
            );
        }

        [Test]
        public void ProvidersApplyAtReadinessNotAtInitialization()
        {
            CommandShell shell = new CommandShell(new CommandHistory(16));
            shell.InitializeAutoRegisteredCommands(deferRegistration: true);

            StubProvider provider = new StubProvider
            {
                Producer = _ => CreateNamed("provider-late"),
            };
            Register(provider);

            Assert.IsEmpty(
                provider.Consulted,
                "Deferred initialization must not consult providers synchronously"
            );
            Assert.IsEmpty(
                shell.AutoRegisteredCommands,
                "Deferred initialization must not surface commands before readiness"
            );

            shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(
                shell.Commands.ContainsKey("provider-late"),
                "Readiness should consult providers registered after initialization"
            );
        }

        [Test]
        public void ProviderClaimReplacesReflectionCollection()
        {
            Assembly coreAssembly = typeof(int).Assembly;
            StubProvider provider = new StubProvider
            {
                Producer = _ => CreateNamed("provider-core"),
            };
            IDisposable handle = Register(provider);

            List<CommandShell.AutoCommand> collected = new();
            CommandShell.AutoCommandSource source = CommandShell.CollectAutoCommands(
                coreAssembly,
                collected
            );
            Assert.AreEqual(
                CommandShell.AutoCommandSource.Provider,
                source,
                "A claiming provider should own the assembly's collection"
            );
            Assert.AreEqual(
                1,
                collected.Count,
                "A claimed assembly should collect exactly the provider's candidates"
            );
            Assert.AreEqual(
                "provider-core",
                collected[0].Name,
                "The provider's candidate should be the collected command"
            );

            handle.Dispose();
            collected.Clear();
            source = CommandShell.CollectAutoCommands(coreAssembly, collected);
            Assert.AreEqual(
                CommandShell.AutoCommandSource.Reflected,
                source,
                "Without a provider the assembly should fall through to reflection"
            );
            Assert.IsEmpty(collected, "The core assembly carries no reflected commands");
        }

        [Test]
        public void ClearThenReinitializeReconsultsProviders()
        {
            StubProvider provider = new StubProvider
            {
                Producer = _ => CreateNamed("provider-cycle"),
            };
            Register(provider);

            CommandShell shell = CreateShell();
            int consultedAfterFirst = provider.Consulted.Count;
            Assert.IsNotEmpty(
                provider.Consulted,
                "Sanity: the provider should have served the first registration"
            );

            shell.ClearAutoRegisteredCommands();
            shell.InitializeAutoRegisteredCommands();

            Assert.Greater(
                provider.Consulted.Count,
                consultedAfterFirst,
                "Re-registration should re-consult providers"
            );
            Assert.IsTrue(
                shell.Commands.ContainsKey("provider-cycle"),
                "Provider commands should re-register after a clear"
            );
        }

        [Test]
        public void ProviderCommandCollidingWithBuiltInWins()
        {
            bool invoked = false;
            StubProvider provider = new StubProvider
            {
                Producer = _ => new List<CommandShell.AutoCommand>
                {
                    CreateInvokableCommand("list-themes", () => invoked = true),
                },
            };
            Register(provider);

            CommandShell shell = CreateShell();

            CollectionAssert.Contains(
                KnownDefaultCommandNames,
                "list-themes",
                "Sanity: expected 'list-themes' to be a default built-in for the collision"
            );
            shell.RunCommand("list-themes");

            Assert.IsTrue(
                invoked,
                "Provider-served candidates register with the earlier candidates and win "
                    + "the name collision against built-ins"
            );
        }

#if UNITY_EDITOR
        [Test]
        public void TypeCacheDiscoveryMatchesReflectionOracle()
        {
            Assembly[] knownAssemblies =
            {
                typeof(CommandDiscoveryProviderTests).Assembly,
                typeof(BuiltInCommands).Assembly,
            };

            foreach (Assembly assembly in knownAssemblies)
            {
                List<CommandShell.AutoCommand> collected = new();
                bool claimed = TypeCacheCommandDiscovery.Instance.TryCollect(assembly, collected);
                Assert.IsTrue(
                    claimed,
                    $"TypeCache should claim the compiled assembly '{assembly.GetName().Name}'"
                );

                string[] providerNames = collected
                    .Select(command => command.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                string[] oracleNames = CommandShell
                    .RegisteredCommands.Value.Where(tuple =>
                        ReferenceEquals(tuple.method.DeclaringType.Assembly, assembly)
                    )
                    .Select(tuple => tuple.attribute.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

                Assert.IsNotEmpty(
                    oracleNames,
                    $"Sanity: expected attributed commands in '{assembly.GetName().Name}'"
                );
                Assert.AreEqual(
                    string.Join("\n", oracleNames),
                    string.Join("\n", providerNames),
                    $"TypeCache discovery diverged from the reflection oracle for "
                        + $"'{assembly.GetName().Name}'"
                );
            }
        }

        [Test]
        public void TypeCacheDiscoveryClaimsKnownAndPassesUnknownAssemblies()
        {
            List<CommandShell.AutoCommand> collected = new();

            Assert.IsTrue(
                TypeCacheCommandDiscovery.Instance.TryCollect(
                    typeof(CommandDiscoveryProviderTests).Assembly,
                    collected
                ),
                "TypeCache should claim the test assembly, which holds attributed commands"
            );
            Assert.IsNotEmpty(
                collected,
                "Claimed assemblies should surface their attributed commands"
            );

            collected.Clear();
            Assert.IsFalse(
                TypeCacheCommandDiscovery.Instance.TryCollect(typeof(int).Assembly, collected),
                "Assemblies without attributed commands must pass to the next provider"
            );
            Assert.IsEmpty(collected, "Unknown assemblies must not contribute commands");

            collected.Clear();
            Assert.IsFalse(
                TypeCacheCommandDiscovery.Instance.TryCollect(DynamicAssembly, collected),
                "Dynamic assemblies are not in the TypeCache index and must pass to reflection"
            );
            Assert.IsEmpty(collected, "Unknown assemblies must not contribute commands");
        }
#endif

        /*
           Declared without [RegisterCommand] so no catalog carries it; provider
           candidates pass through the same signature validation as reflected
           ones.
        */
        private static void InvalidSignatureMethod(string args) { }

        private static CommandShell CreateShell()
        {
            CommandShell shell = new CommandShell(new CommandHistory(16));
            shell.InitializeAutoRegisteredCommands();
            return shell;
        }

        private static CommandShell.AutoCommand CreateInvokableCommand(string name, Action invoked)
        {
            Action<CommandArg[]> handler = _ => invoked();
            return CommandShell.AutoCommand.FromCatalog(
                new CommandCatalogEntry(
                    name,
                    $"{name}-method",
                    0,
                    -1,
                    "Provider test command",
                    null,
                    true,
                    false,
                    false,
                    false,
                    () => handler,
                    null
                )
            );
        }

        private static List<CommandShell.AutoCommand> CreateNamed(string name)
        {
            return new List<CommandShell.AutoCommand> { CreateInvokableCommand(name, () => { }) };
        }

        private IDisposable Register(ICommandDiscoveryProvider provider)
        {
            IDisposable handle = CommandShell.RegisterDiscoveryProvider(provider);
            _registeredProviders.Add(handle);
            return handle;
        }

        private sealed class StubProvider : ICommandDiscoveryProvider
        {
            internal Func<Assembly, bool> ClaimFilter = _ => true;

            internal Func<Assembly, List<CommandShell.AutoCommand>> Producer;

            internal readonly List<Assembly> Consulted = new();

            public bool TryCollect(Assembly assembly, List<CommandShell.AutoCommand> commands)
            {
                Consulted.Add(assembly);
                if (!ClaimFilter(assembly))
                {
                    return false;
                }

                List<CommandShell.AutoCommand> produced = Producer?.Invoke(assembly);
                if (produced != null)
                {
                    commands.AddRange(produced);
                }

                return true;
            }
        }
    }
}
