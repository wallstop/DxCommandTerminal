namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;

    public sealed class CommandShellBehaviorTests
    {
        [Test]
        public void InitializeAutoRegisteredCommandsRespectsIgnoredNames()
        {
            CommandHistory history = new(8);
            CommandShell shell = new(history);

            shell.InitializeAutoRegisteredCommands(new[] { "help" }, ignoreDefaultCommands: false);

            Assert.IsFalse(shell.Commands.ContainsKey("help"));
            Assert.IsTrue(shell.Commands.ContainsKey("log"));
            Assert.IsTrue(shell.IgnoredCommands.Contains("help"));
        }

        [Test]
        public void InitializeAutoRegisteredCommandsCanSkipDefaultsEntirely()
        {
            CommandHistory history = new(8);
            CommandShell shell = new(history);

            shell.InitializeAutoRegisteredCommands(
                Array.Empty<string>(),
                ignoreDefaultCommands: true
            );

            Assert.IsFalse(shell.Commands.ContainsKey("help"));
            Assert.IsFalse(shell.Commands.ContainsKey("log"));
            Assert.IsFalse(shell.Commands.ContainsKey("clear-console"));
            Assert.IsTrue(shell.AutoRegisteredCommands.SetEquals(shell.Commands.Keys));
        }

        [Test]
        public void InitializeAutoRegisteredCommandsRefreshesIgnoredCommands()
        {
            CommandHistory history = new(8);
            CommandShell shell = new(history);

            shell.InitializeAutoRegisteredCommands(
                Array.Empty<string>(),
                ignoreDefaultCommands: false
            );
            Assert.IsTrue(shell.Commands.ContainsKey("help"));

            shell.InitializeAutoRegisteredCommands(new[] { "help" }, ignoreDefaultCommands: false);

            Assert.IsFalse(shell.Commands.ContainsKey("help"));
            Assert.IsTrue(shell.IgnoredCommands.Contains("help"));
        }

        [Test]
        public void RunCommandQueuesErrorForMissingCommand()
        {
            CommandHistory history = new(4);
            CommandShell shell = new(history);

            bool result = shell.RunCommand("missing");

            Assert.IsFalse(result);
            Assert.IsTrue(shell.TryConsumeErrorMessage(out string message));
            StringAssert.Contains("missing", message);
            Assert.IsFalse(shell.TryConsumeErrorMessage(out _));

            List<string> all = new(history.GetHistory(false, false));
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("missing", all[0]);

            List<string> successes = new(history.GetHistory(true, false));
            Assert.AreEqual(0, successes.Count);
        }

        [Test]
        public void RunCommandInvokesHandlerAndRecordsSuccess()
        {
            CommandHistory history = new(4);
            CommandShell shell = new(history);

            CommandArg[] captured = null;
            shell.AddCommand("echo", args => captured = args, 0, -1);

            bool executed = shell.RunCommand("echo hello world");

            Assert.IsTrue(executed);
            Assert.IsNotNull(captured);
            Assert.AreEqual(2, captured.Length);
            Assert.AreEqual("hello", captured[0].contents);
            Assert.AreEqual("world", captured[1].contents);
            Assert.IsFalse(shell.TryConsumeErrorMessage(out _));

            List<string> successes = new(history.GetHistory(true, true));
            Assert.AreEqual(1, successes.Count);
            Assert.AreEqual("echo hello world", successes[0]);
        }

        [Test]
        public void RunCommandValidatesArgumentCount()
        {
            CommandHistory history = new(4);
            CommandShell shell = new(history);
            shell.AddCommand("require-two", _ => { }, 2, 2);

            bool executed = shell.RunCommand("require-two only-one");

            Assert.IsFalse(executed);
            Assert.IsTrue(shell.TryConsumeErrorMessage(out string message));
            StringAssert.Contains("requires", message);

            List<string> successes = new(history.GetHistory(true, false));
            Assert.AreEqual(0, successes.Count);

            List<string> all = new(history.GetHistory(false, false));
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("require-two only-one", all[0]);
        }

        [Test]
        public void VariableSubstitutionInjectsStoredValues()
        {
            CommandHistory history = new(4);
            CommandShell shell = new(history);

            CommandArg[] captured = null;
            shell.AddCommand("say", args => captured = args);

            Assert.IsTrue(shell.SetVariable("target", new CommandArg("world")));

            bool executed = shell.RunCommand("say $target");

            Assert.IsTrue(executed);
            Assert.IsNotNull(captured);
            Assert.AreEqual(1, captured.Length);
            Assert.AreEqual("world", captured[0].contents);
        }

        [Test]
        public void UnknownVariableReferencesRemainLiteral()
        {
            CommandHistory history = new(4);
            CommandShell shell = new(history);

            CommandArg[] captured = null;
            shell.AddCommand("say", args => captured = args);

            bool executed = shell.RunCommand("say $missing");

            Assert.IsTrue(executed);
            Assert.IsNotNull(captured);
            Assert.AreEqual(1, captured.Length);
            Assert.AreEqual("$missing", captured[0].contents);
        }

        [Test]
        public void VariableSubstitutionPreservesStoredQuotes()
        {
            CommandHistory history = new(4);
            CommandShell shell = new(history);

            CommandArg[] captured = null;
            shell.AddCommand("say", args => captured = args);

            shell.SetVariable("phrase", new CommandArg("hello world", '"', '"'));
            bool executed = shell.RunCommand("say $phrase");

            Assert.IsTrue(executed);
            Assert.IsNotNull(captured);
            Assert.AreEqual("hello world", captured[0].contents);

            List<string> entries = new(history.GetHistory(false, false));
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual("say \"hello world\"", entries[0]);
        }

        [Test]
        public void ClearVariableRemovesStoredValue()
        {
            CommandShell shell = new(new CommandHistory(2));

            Assert.IsTrue(shell.SetVariable("temp", new CommandArg("value")));
            Assert.IsTrue(shell.ClearVariable("temp"));
            Assert.IsFalse(shell.TryGetVariable("temp", out _));
            Assert.IsFalse(shell.ClearVariable("temp"));
        }

        [Test]
        public void TryConsumeErrorMessageReturnsFalseWhenEmpty()
        {
            CommandShell shell = new(new CommandHistory(2));

            Assert.IsFalse(shell.TryConsumeErrorMessage(out _));

            shell.IssueErrorMessage("sample error");
            Assert.IsTrue(shell.TryConsumeErrorMessage(out string message));
            Assert.AreEqual("sample error", message);
            Assert.IsFalse(shell.TryConsumeErrorMessage(out _));
        }

        [Test]
        public void AddCommandPreventsDuplicateNames()
        {
            CommandShell shell = new(new CommandHistory(2));

            bool first = shell.AddCommand("duplicate", _ => { });
            bool second = shell.AddCommand("DUPLICATE", _ => { });

            Assert.IsTrue(first);
            Assert.IsFalse(second);
            Assert.IsTrue(shell.TryConsumeErrorMessage(out string message));
            StringAssert.Contains("duplicate", message.ToLowerInvariant());
        }

        [Test]
        public void ClearVariableRejectsInvalidNames()
        {
            CommandShell shell = new(new CommandHistory(2));

            Assert.IsFalse(shell.ClearVariable(string.Empty));
            Assert.IsTrue(shell.TryConsumeErrorMessage(out string message));
            StringAssert.Contains("invalid", message.ToLowerInvariant());
        }
    }
}
