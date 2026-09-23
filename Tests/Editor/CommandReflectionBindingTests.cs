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
        Binding behavior of reflection-discovered commands (the compatibility
        path behind IncludeDiscoveryAssembly and precompiled assemblies):
        reflected commands register without binding and pay their one-time
        Delegate.CreateDelegate on the first invocation, generic method
        definitions are rejected commands with the shared invalid-signature
        diagnostics, and degenerate-but-bindable signatures keep registering
        and running exactly as the eager binder did. Editor-only: players
        cannot emit IL.
     */
    public sealed class CommandReflectionBindingTests
    {
        private const string CounterCommandName = "binding-counter";

        private const string CounterHolderTypeName =
            "WallstopStudios.DxCommandTerminal.Tests.BindingCounterCommands";

        private const string GenericCommandName = "binding-generic";

        private const string GenericHolderCommandName = "binding-generic-holder";

        private const string AbstractCommandName = "binding-abstract";

        private const string HolderNamespace = "WallstopStudios.DxCommandTerminal.Tests";

        private readonly List<IDisposable> _handles = new();

#if UNITY_EDITOR
        /*
            Defined once: dynamic assemblies are non-collectible, and IL2CPP
            players do not support Reflection.Emit, so these cases are
            editor-only. The counter command increments a static field so
            repeated invocations are observable from the test.
         */
        private static readonly Assembly CounterAssembly = CreateCounterAssembly();

        private static readonly Assembly GenericMethodAssembly = CreateGenericMethodAssembly();

        private static readonly Assembly GenericHolderAssembly = CreateGenericHolderAssembly();

        private static readonly Assembly AbstractAssembly = CreateAbstractAssembly();

        private static readonly Assembly BlankNameAssembly = CreateBlankNameAssembly();

        private static AssemblyBuilder BeginAssembly(string prefix)
        {
            AssemblyName name = new($"{prefix}-{Guid.NewGuid():N}");
            return AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        }

        private static void AttachCommand(MethodBuilder method, string commandName)
        {
            ConstructorInfo attributeConstructor = typeof(RegisterCommandAttribute).GetConstructor(
                new[] { typeof(string) }
            );
            method.SetCustomAttribute(
                new CustomAttributeBuilder(attributeConstructor, new object[] { commandName })
            );
        }

        private static Assembly CreateCounterAssembly()
        {
            AssemblyBuilder assembly = BeginAssembly("BindingCounter");
            ModuleBuilder module = assembly.DefineDynamicModule("Commands");
            TypeBuilder type = module.DefineType(
                CounterHolderTypeName,
                TypeAttributes.Public | TypeAttributes.Sealed
            );
            FieldBuilder counter = type.DefineField(
                "Invocations",
                typeof(int),
                FieldAttributes.Public | FieldAttributes.Static
            );
            MethodBuilder method = type.DefineMethod(
                "CountCommand",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            method.DefineParameter(1, ParameterAttributes.None, "args");
            ILGenerator body = method.GetILGenerator();
            body.Emit(OpCodes.Ldsfld, counter);
            body.Emit(OpCodes.Ldc_I4_1);
            body.Emit(OpCodes.Add);
            body.Emit(OpCodes.Stsfld, counter);
            body.Emit(OpCodes.Ret);
            AttachCommand(method, CounterCommandName);
            type.CreateType();
            return assembly;
        }

        private static Assembly CreateGenericMethodAssembly()
        {
            AssemblyBuilder assembly = BeginAssembly("BindingGenericMethod");
            ModuleBuilder module = assembly.DefineDynamicModule("Commands");
            TypeBuilder type = module.DefineType(
                $"{HolderNamespace}.BindingGenericMethodCommands",
                TypeAttributes.Public | TypeAttributes.Sealed
            );
            MethodBuilder method = type.DefineMethod(
                "GenericCommand",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            method.DefineGenericParameters("T");
            method.DefineParameter(1, ParameterAttributes.None, "args");
            method.GetILGenerator().Emit(OpCodes.Ret);
            AttachCommand(method, GenericCommandName);
            type.CreateType();
            return assembly;
        }

        private static Assembly CreateGenericHolderAssembly()
        {
            AssemblyBuilder assembly = BeginAssembly("BindingGenericHolder");
            ModuleBuilder module = assembly.DefineDynamicModule("Commands");
            TypeBuilder type = module.DefineType(
                $"{HolderNamespace}.BindingGenericHolderCommands`1",
                TypeAttributes.Public | TypeAttributes.Sealed
            );
            type.DefineGenericParameters("T");
            MethodBuilder method = type.DefineMethod(
                "HolderCommand",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            method.DefineParameter(1, ParameterAttributes.None, "args");
            method.GetILGenerator().Emit(OpCodes.Ret);
            AttachCommand(method, GenericHolderCommandName);
            type.CreateType();
            return assembly;
        }

        private static Assembly CreateAbstractAssembly()
        {
            AssemblyBuilder assembly = BeginAssembly("BindingAbstract");
            ModuleBuilder module = assembly.DefineDynamicModule("Commands");
            TypeBuilder type = module.DefineType(
                $"{HolderNamespace}.BindingAbstractCommands",
                TypeAttributes.Interface | TypeAttributes.Abstract
            );
            MethodBuilder method = type.DefineMethod(
                "AbstractCommand",
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.Abstract
                    | MethodAttributes.Virtual
                    | MethodAttributes.NewSlot
                    | MethodAttributes.HideBySig,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            method.DefineParameter(1, ParameterAttributes.None, "args");
            AttachCommand(method, AbstractCommandName);
            type.CreateType();
            return assembly;
        }

        private static Assembly CreateBlankNameAssembly()
        {
            /*
                A method named "Command" normalizes to an empty inferred name,
                the documented blank-name flow.
             */
            AssemblyBuilder assembly = BeginAssembly("BindingBlankName");
            ModuleBuilder module = assembly.DefineDynamicModule("Commands");
            TypeBuilder type = module.DefineType(
                $"{HolderNamespace}.BindingBlankNameCommands",
                TypeAttributes.Public | TypeAttributes.Sealed
            );
            MethodBuilder method = type.DefineMethod(
                "Command",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(void),
                new[] { typeof(CommandArg[]) }
            );
            method.DefineParameter(1, ParameterAttributes.None, "args");
            method.GetILGenerator().Emit(OpCodes.Ret);
            ConstructorInfo attributeConstructor = typeof(RegisterCommandAttribute).GetConstructor(
                new[] { typeof(string) }
            );
            method.SetCustomAttribute(
                new CustomAttributeBuilder(attributeConstructor, new object[] { null })
            );
            type.CreateType();
            return assembly;
        }
#endif

        [SetUp]
        public void SetUp()
        {
#if UNITY_EDITOR
            /*
               The emitted counter lives in a non-collectible dynamic
               assembly, so its value persists across runs in one domain.
             */
            CounterAssembly
                .GetType(CounterHolderTypeName)
                .GetField("Invocations", BindingFlags.Public | BindingFlags.Static)
                .SetValue(null, 0);
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

#if UNITY_EDITOR
        [Test]
        public void ReflectedCommandBindsOnceAndReusesTheDelegate()
        {
            Register(CounterAssembly);

            CommandShell shell = CreateShell();

            Assert.IsTrue(
                shell.Commands.ContainsKey(CounterCommandName),
                "The reflected command should register at readiness without binding"
            );

            FieldInfo counter = CounterAssembly
                .GetType(CounterHolderTypeName)
                .GetField("Invocations", BindingFlags.Public | BindingFlags.Static);
            Assert.That(counter != null, "Sanity: expected the emitted counter field");
            Assert.AreEqual(
                0,
                counter.GetValue(null),
                "Readiness must not invoke or bind the command's handler"
            );

            Assert.IsTrue(shell.RunCommand(CounterCommandName));
            Assert.IsTrue(shell.RunCommand(CounterCommandName));
            Assert.AreEqual(
                2,
                counter.GetValue(null),
                "Both invocations should run through the one-time bound delegate"
            );

            /*
               One DeferredCommandHandler lives per AutoCommand (cached per
               assembly), so a second shell shares the bound delegate instead
               of paying its own bind.
             */
            CommandShell secondShell = CreateShell();
            Assert.IsTrue(
                ReferenceEquals(
                    shell.Commands[CounterCommandName].proc,
                    secondShell.Commands[CounterCommandName].proc
                ),
                "Both shells should dispatch through the same bound handler"
            );
        }

        [Test]
        public void GenericMethodSignatureIsRejectedCommand()
        {
            Register(GenericMethodAssembly);

            CommandShell shell = CreateShell();

            Assert.IsTrue(
                shell.AutoCommandsRegistered,
                "Sanity: readiness should complete with an unbindable signature present"
            );
            Assert.IsFalse(
                shell.Commands.ContainsKey(GenericCommandName),
                "A generic method definition must not register a runnable command"
            );
            Assert.IsFalse(shell.RunCommand(GenericCommandName));

            bool reported = false;
            while (shell.TryConsumeErrorMessage(out string errorMessage))
            {
                if (
                    errorMessage != null
                    && errorMessage.Contains(GenericCommandName, StringComparison.Ordinal)
                    && errorMessage.Contains("invalid signature", StringComparison.Ordinal)
                )
                {
                    reported = true;
                    break;
                }
            }

            Assert.IsTrue(
                reported,
                $"Expected the invalid-signature diagnostics for {GenericCommandName}"
            );
        }

        [Test]
        public void ToleratedDegenerateSignaturesStillRegister()
        {
            /*
                Compatibility pins for signatures the current scripting
                backend binds despite being degenerate declarations: both
                register through the deferred binder exactly as the eager
                binder did. Running the open-generic holder's command throws
                the backend's not-fully-instantiated error the same way the
                eager path did - dispatch does not contain handler failures.
                The abstract static is never invoked here; invoking an
                abstract delegate is undefined.
             */
            Register(GenericHolderAssembly);
            Register(AbstractAssembly);

            CommandShell shell = CreateShell();

            Assert.IsTrue(
                shell.AutoCommandsRegistered,
                "Degenerate signatures must not break readiness"
            );
            Assert.IsTrue(
                shell.Commands.ContainsKey(GenericHolderCommandName),
                "A bindable command in an open generic type must stay registered"
            );
            Assert.Throws<InvalidOperationException>(
                () => shell.RunCommand(GenericHolderCommandName),
                "Invoking an open-generic holder's command surfaces the backend error, as before"
            );
            Assert.IsTrue(
                shell.Commands.ContainsKey(AbstractCommandName),
                "An abstract static command stays registered where the backend binds it"
            );
        }

        [Test]
        public void BlankAutoCommandNamesQueueTheStandardDiagnostic()
        {
            /*
               A name inference that strips to empty must not register: the
               command would be silently unreachable from dispatch while
               still appearing in completion.
             */
            Register(BlankNameAssembly);

            CommandShell shell = CreateShell();

            Assert.IsTrue(shell.AutoCommandsRegistered);
            Assert.IsFalse(
                shell.Commands.ContainsKey(string.Empty),
                "A blank auto command name must not register"
            );

            bool reported = false;
            while (shell.TryConsumeErrorMessage(out string errorMessage))
            {
                if (
                    errorMessage != null
                    && errorMessage.StartsWith("Invalid Command Name", StringComparison.Ordinal)
                )
                {
                    reported = true;
                    break;
                }
            }

            Assert.IsTrue(reported, "Expected the standard blank-name diagnostic");
        }
#endif

        private static CommandShell CreateShell()
        {
            CommandShell shell = new CommandShell(new CommandHistory(16));
            shell.InitializeAutoRegisteredCommands();
            return shell;
        }

        private void Register(Assembly assembly)
        {
            _handles.Add(CommandShell.IncludeDiscoveryAssembly(assembly));
        }
    }
}
