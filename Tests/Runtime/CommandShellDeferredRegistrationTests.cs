namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Linq;
    using Backend;
    using NUnit.Framework;

    /*
        Covers the deferred first-use registration readiness boundary: with
        deferRegistration, discovery and delegate materialization must not run
        until the first command request or command-state read, and must apply
        the same filtering and diagnostics as eager initialization.
     */
    public sealed class CommandShellDeferredRegistrationTests
    {
        private static readonly string[] KnownAutoCommandNames = CommandShell
            .RegisteredCommands.Value.Select(tuple => tuple.attribute.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private static readonly string[] KnownDefaultCommandNames = CommandShell
            .RegisteredCommands.Value.Where(tuple => tuple.attribute.Default)
            .Select(tuple => tuple.attribute.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        [Test]
        public void EagerInitializationRegistersImmediately()
        {
            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands();

            Assert.IsTrue(
                shell.AutoCommandsRegistered,
                "Eager initialization should complete registration synchronously"
            );
            Assert.IsNotEmpty(
                shell.AutoRegisteredCommands,
                "Sanity: expected the eager path to register discovered commands"
            );
        }

        [Test]
        public void DeferredInitializationDefersRegistrationUntilReadiness()
        {
            Assert.IsNotEmpty(
                KnownAutoCommandNames,
                "Sanity: expected at least one auto-registered command to exist"
            );

            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands(deferRegistration: true);

            Assert.IsFalse(
                shell.AutoCommandsRegistered,
                "Deferred initialization must not register commands synchronously"
            );
            Assert.IsEmpty(
                shell.AutoRegisteredCommands,
                "Deferred initialization must not surface auto commands before readiness"
            );

            shell.EnsureAutoCommandsRegistered();

            Assert.IsTrue(
                shell.AutoCommandsRegistered,
                "Readiness should apply the deferred registration"
            );
            Assert.IsNotEmpty(
                shell.AutoRegisteredCommands,
                "Readiness should register the discovered commands"
            );

            int registeredCount = shell.AutoRegisteredCommands.Count;
            int commandCount = shell.Commands.Count;
            shell.EnsureAutoCommandsRegistered();

            Assert.AreEqual(
                registeredCount,
                shell.AutoRegisteredCommands.Count,
                "Readiness must be idempotent; a second call re-registered commands"
            );
            Assert.AreEqual(
                commandCount,
                shell.Commands.Count,
                "Readiness must be idempotent; a second call changed the command set"
            );
        }

        [Test]
        public void CommandsReadAppliesDeferredRegistration()
        {
            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands(deferRegistration: true);
            Assert.IsEmpty(
                shell.AutoRegisteredCommands,
                "Command state must stay unregistered before the first read"
            );

            int _ = shell.Commands.Count;

            Assert.IsTrue(
                shell.AutoCommandsRegistered,
                "Reading command state is the readiness boundary and should apply deferred "
                    + "registration"
            );
            Assert.IsNotEmpty(
                shell.AutoRegisteredCommands,
                "Command state reads should observe registered commands"
            );
        }

        [Test]
        public void DeferredInitializationAppliesIgnoredCommands()
        {
            string ignoredCommand = KnownAutoCommandNames[0];

            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands(
                ignoredCommands: new[] { ignoredCommand },
                deferRegistration: true
            );

            Assert.IsTrue(
                shell.IgnoredCommands.Contains(ignoredCommand),
                "The ignored command configuration must apply before readiness"
            );

            shell.EnsureAutoCommandsRegistered();

            Assert.IsFalse(
                shell.AutoRegisteredCommands.Contains(ignoredCommand),
                $"Ignored command '{ignoredCommand}' must not be registered"
            );
            Assert.IsFalse(
                shell.Commands.ContainsKey(ignoredCommand),
                $"Ignored command '{ignoredCommand}' must not be present after readiness"
            );
            Assert.IsFalse(
                shell.RunCommand(ignoredCommand),
                $"Ignored command '{ignoredCommand}' must not be runnable after readiness"
            );
            Assert.IsTrue(
                shell.HasErrors,
                "Running an ignored command should report that it was not found"
            );
        }

        [Test]
        public void DeferredInitializationAppliesIgnoreDefaultCommands()
        {
            Assert.IsNotEmpty(
                KnownDefaultCommandNames,
                "Sanity: expected at least one default command to exist"
            );
            string defaultCommand = KnownDefaultCommandNames[0];

            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands(
                ignoreDefaultCommands: true,
                deferRegistration: true
            );

            shell.EnsureAutoCommandsRegistered();

            Assert.IsFalse(
                shell.AutoRegisteredCommands.Contains(defaultCommand),
                $"Default command '{defaultCommand}' must not be registered when "
                    + "ignoreDefaultCommands is set"
            );
            Assert.IsFalse(
                shell.Commands.ContainsKey(defaultCommand),
                $"Default command '{defaultCommand}' must not be runnable when "
                    + "ignoreDefaultCommands is set"
            );
        }

        [Test]
        public void DeferredRegistrationLetsManualCommandsWinWithoutQueuedErrors()
        {
            string knownCommand = KnownAutoCommandNames[0];

            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands(deferRegistration: true);

            // A manual registration between enable and first use keeps the
            // name; readiness must not queue a duplicate error for it.
            Assert.IsTrue(
                shell.AddCommand(knownCommand, _ => { }, 0, -1, "manual override"),
                $"Sanity: registering '{knownCommand}' manually on an empty shell must succeed"
            );

            shell.EnsureAutoCommandsRegistered();

            Assert.IsFalse(
                shell.AutoRegisteredCommands.Contains(knownCommand),
                $"The auto command '{knownCommand}' must lose to the manual registration"
            );
            Assert.IsTrue(
                shell.Commands.ContainsKey(knownCommand),
                $"The manual registration of '{knownCommand}' must remain"
            );
            Assert.IsFalse(
                shell.HasErrors,
                "A manual override must not queue a terminal error at readiness"
            );
        }

        [Test]
        public void ClearAutoRegisteredCommandsCancelsDeferredRegistration()
        {
            string knownCommand = KnownAutoCommandNames[0];

            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            shell.InitializeAutoRegisteredCommands(deferRegistration: true);
            shell.ClearAutoRegisteredCommands();

            shell.EnsureAutoCommandsRegistered();

            Assert.IsFalse(
                shell.AutoCommandsRegistered,
                "Clearing must cancel the pending registration; readiness must not apply it"
            );
            Assert.IsEmpty(
                shell.AutoRegisteredCommands,
                "Cleared shells must not resurrect auto commands through readiness"
            );
            Assert.IsFalse(
                shell.RunCommand(knownCommand),
                $"Cleared auto command '{knownCommand}' must not be runnable"
            );
        }
    }
}
