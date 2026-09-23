namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Reflection.Emit;
    using Attributes;
    using Backend;
    using NUnit.Framework;

    /*
        Covers the explicit discovery opt-in for assemblies the default
        assembly-reference filter skips: dynamic assemblies and precompiled
        DLLs without a metadata reference to this package. The opt-in scans
        the assembly through the normal catalog, provider, and reflection
        stages; the default path keeps skipping it.
     */
    public sealed class CommandDiscoveryInclusionTests
    {
        private const string CommandName = "probe-included";

        private const string ProbeHolderTypeName =
            "WallstopStudios.DxCommandTerminal.Tests.ProbeCommands";

#if UNITY_EDITOR
        /*
           Defined once: dynamic assemblies are non-collectible, and IL2CPP
           players do not support Reflection.Emit, so this case is editor-only.
           The emitted command sets a static field, so runnable registrations
           are observable from the test.
         */
        private static readonly Assembly DynamicAssembly = CreateCommandAssembly();

        private static Assembly CreateCommandAssembly()
        {
            AssemblyName name = new($"DiscoveryInclusionTestDynamic-{Guid.NewGuid():N}");
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                name,
                AssemblyBuilderAccess.Run
            );
            ModuleBuilder module = assembly.DefineDynamicModule("Commands");
            TypeBuilder type = module.DefineType(
                ProbeHolderTypeName,
                TypeAttributes.Public | TypeAttributes.Sealed
            );
            FieldBuilder invoked = type.DefineField(
                "Invoked",
                typeof(bool),
                FieldAttributes.Public | FieldAttributes.Static
            );
            MethodBuilder method = type.DefineMethod(
                "ProbeIncludedCommand",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            method.DefineParameter(1, ParameterAttributes.None, "args");
            ILGenerator body = method.GetILGenerator();
            body.Emit(OpCodes.Ldc_I4_1);
            body.Emit(OpCodes.Stsfld, invoked);
            body.Emit(OpCodes.Ret);
            ConstructorInfo attributeConstructor = typeof(RegisterCommandAttribute).GetConstructor(
                new[] { typeof(string) }
            );
            method.SetCustomAttribute(
                new CustomAttributeBuilder(attributeConstructor, new object[] { CommandName })
            );
            type.CreateType();
            return assembly;
        }

        private static FieldInfo InvokedField()
        {
            Type holder = DynamicAssembly.GetType(ProbeHolderTypeName);
            Assert.That(holder != null, "Sanity: expected the emitted command holder type");
            FieldInfo field = holder.GetField("Invoked", BindingFlags.Public | BindingFlags.Static);
            Assert.That(field != null, "Sanity: expected the emitted invoked field");
            return field;
        }

        private static bool DynamicCommandInvoked()
        {
            return (bool)InvokedField().GetValue(null);
        }
#endif

        private readonly List<IDisposable> _handles = new();

        [SetUp]
        public void SetUp()
        {
            _handles.Clear();
#if UNITY_EDITOR
            InvokedField().SetValue(null, false);
#endif
        }

        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable handle in _handles)
            {
                handle.Dispose();
            }

            _handles.Clear();
        }

        [Test]
        public void NullAssemblyIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => CommandShell.IncludeDiscoveryAssembly(null));
        }

#if UNITY_EDITOR
        [Test]
        public void DynamicAssemblyIsSkippedByDefault()
        {
            /*
               Pins the default path: a dynamic assembly carrying an attributed
               command stays out of discovery. The filter exists so the default
               path never walks every assembly; this pin shows the emitted
               command is dropped until it is explicitly included.
             */
            Assert.IsFalse(
                CommandShell.MayContainCommands(
                    DynamicAssembly,
                    typeof(BuiltInCommands).Assembly.GetName()
                ),
                "Sanity: dynamic assemblies fail the reference filter"
            );

            CommandShell shell = CreateShell();

            Assert.IsFalse(
                shell.Commands.ContainsKey(CommandName),
                "An excluded assembly's commands must not register by default"
            );
        }

        [Test]
        public void IncludedAssemblyRegistersAndRuns()
        {
            Register(DynamicAssembly);

            CommandShell shell = CreateShell();

            Assert.IsTrue(
                shell.Commands.ContainsKey(CommandName),
                "An included assembly's attributed command should register"
            );
            Assert.IsTrue(
                shell.AutoRegisteredCommands.Contains(CommandName),
                "Included commands should register through the shared pipeline"
            );

            shell.RunCommand(CommandName);

            Assert.IsTrue(
                DynamicCommandInvoked(),
                "The included command should bind and run through readiness"
            );
        }

        [Test]
        public void DisposedInclusionStopsScanning()
        {
            IDisposable handle = Register(DynamicAssembly);

            CommandShell shell = CreateShell();
            Assert.IsTrue(
                shell.Commands.ContainsKey(CommandName),
                "Sanity: the included command should register before dispose"
            );

            handle.Dispose();
            CommandShell shellAfterDispose = CreateShell();

            Assert.IsFalse(
                shellAfterDispose.Commands.ContainsKey(CommandName),
                "A disposed inclusion must stop scanning the assembly"
            );
        }

        [Test]
        public void DuplicateInclusionIsSingleScanSource()
        {
            Register(DynamicAssembly);
            Register(DynamicAssembly);

            CommandShell shell = CreateShell();
            Assert.IsTrue(
                shell.Commands.ContainsKey(CommandName),
                "Duplicate inclusions must not break registration"
            );

            _handles[1].Dispose();
            CommandShell shellAfterSecondDispose = CreateShell();
            Assert.IsFalse(
                shellAfterSecondDispose.Commands.ContainsKey(CommandName),
                "The first dispose must remove the inclusion, like provider handles"
            );
        }

        [Test]
        public void LateInclusionAppliesOnNextRegistrationCycle()
        {
            CommandShell shell = CreateShell();
            Assert.IsTrue(
                shell.AutoCommandsRegistered,
                "Sanity: expected the fresh shell's registration to be applied"
            );
            Assert.IsFalse(
                shell.Commands.ContainsKey(CommandName),
                "Sanity: without inclusion the command must not register"
            );

            Register(DynamicAssembly);

            shell.ClearAutoRegisteredCommands();
            shell.InitializeAutoRegisteredCommands();
            Assert.IsTrue(
                shell.Commands.ContainsKey(CommandName),
                "The next registration cycle should honor a late inclusion"
            );
        }
#endif

        private static CommandShell CreateShell()
        {
            CommandShell shell = new CommandShell(new CommandHistory(16));
            shell.InitializeAutoRegisteredCommands();
            return shell;
        }

        private IDisposable Register(Assembly assembly)
        {
            IDisposable handle = CommandShell.IncludeDiscoveryAssembly(assembly);
            _handles.Add(handle);
            return handle;
        }
    }
}
