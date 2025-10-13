namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using WallstopStudios.DxCommandTerminal.Attributes;

    internal sealed class DummyCompleter : IArgumentCompleter
    {
        public IEnumerable<string> Complete(CommandCompletionContext context)
        {
            // Return suggestions with and without whitespace
            yield return "foo bar";
            yield return "baz";
        }
    }

    internal sealed class RecordingCompleter : IArgumentCompleter
    {
        private readonly Func<CommandCompletionContext, IEnumerable<string>> _handler;

        public RecordingCompleter(Func<CommandCompletionContext, IEnumerable<string>> handler)
        {
            _handler = handler;
        }

        public List<CommandCompletionContext> Calls { get; } = new List<CommandCompletionContext>();

        public IEnumerable<string> Complete(CommandCompletionContext context)
        {
            Calls.Add(context);
            if (_handler != null)
            {
                IEnumerable<string> results = _handler(context);
                if (results != null)
                {
                    return results;
                }
            }

            return Array.Empty<string>();
        }
    }

    internal sealed class ChainedCompleter : IArgumentCompleter
    {
        public List<CommandCompletionContext> Calls { get; } = new List<CommandCompletionContext>();

        public IEnumerable<string> Complete(CommandCompletionContext context)
        {
            Calls.Add(context);
            if (context.ArgIndex == 0)
            {
                return new[] { "alpha", "beta" };
            }

            if (context.ArgIndex == 1)
            {
                if (
                    context.ArgsBeforeCursor != null
                    && 0 < context.ArgsBeforeCursor.Count
                    && context.ArgsBeforeCursor[0].contents == "alpha"
                )
                {
                    return new[] { "gamma" };
                }

                return new[] { "delta" };
            }

            return Array.Empty<string>();
        }
    }

    internal sealed class AutoRegisteredCompleter : IArgumentCompleter
    {
        public static AutoRegisteredCompleter Instance { get; } = new AutoRegisteredCompleter();

        private AutoRegisteredCompleter() { }

        public IEnumerable<string> Complete(CommandCompletionContext context)
        {
            return new[] { "auto" };
        }
    }

    internal static class AutoRegistrationFixture
    {
        public const string CommandName = "autoFixture";

        [RegisterCommand(Name = CommandName)]
        [CommandCompleter(typeof(AutoRegisteredCompleter))]
        public static void AutoFixture(CommandArg[] args) { }
    }

    public sealed class AutocompleteTests
    {
        [Test]
        public void CompleterProducesQuotedSuggestions()
        {
            CommandHistory history = new CommandHistory(8);
            CommandShell shell = new CommandShell(history);
            shell.AddCommand("testcmd", _ => { }, 0, -1, string.Empty, null, new DummyCompleter());

            CommandAutoComplete ac = new CommandAutoComplete(history, shell);
            string[] results = ac.Complete("testcmd ");

            // Expect both suggestions formatted for insertion
            Assert.IsNotNull(results);
            CollectionAssert.Contains(results, "testcmd \"foo bar\"");
            CollectionAssert.Contains(results, "testcmd baz");
        }

        [Test]
        public void ManualAddCommandExposesCompleter()
        {
            CommandHistory history = new CommandHistory(8);
            CommandShell shell = new CommandShell(history);
            RecordingCompleter recordingCompleter = new RecordingCompleter(_ =>
                Array.Empty<string>()
            );

            bool added = shell.AddCommand(
                "manual",
                _ => { },
                0,
                -1,
                string.Empty,
                null,
                recordingCompleter
            );

            Assert.IsTrue(added);
            Assert.IsTrue(shell.Commands.TryGetValue("manual", out CommandInfo info));
            Assert.AreSame(recordingCompleter, info.completer);
        }

        [Test]
        public void AddCommandWithCommandInfoPreservesCompleter()
        {
            CommandHistory history = new CommandHistory(8);
            CommandShell shell = new CommandShell(history);
            RecordingCompleter recordingCompleter = new RecordingCompleter(_ =>
                Array.Empty<string>()
            );

            CommandInfo info = new CommandInfo(_ => { }, 0, 1, "help", "hint", recordingCompleter);

            bool added = shell.AddCommand("infoCommand", info);

            Assert.IsTrue(added);
            Assert.IsTrue(shell.Commands.TryGetValue("infoCommand", out CommandInfo stored));
            Assert.AreSame(recordingCompleter, stored.completer);
        }

        [Test]
        public void AutoRegisteredCommandReceivesCompleter()
        {
            CommandHistory history = new CommandHistory(8);
            CommandShell shell = new CommandShell(history);

            shell.InitializeAutoRegisteredCommands(Array.Empty<string>());

            Assert.IsTrue(
                shell.Commands.TryGetValue(
                    AutoRegistrationFixture.CommandName,
                    out CommandInfo info
                )
            );
            Assert.IsInstanceOf<AutoRegisteredCompleter>(info.completer);
            Assert.AreSame(AutoRegisteredCompleter.Instance, info.completer);
        }

        [Test]
        public void AutoCompleteChainsArguments()
        {
            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            ChainedCompleter chainedCompleter = new ChainedCompleter();
            shell.AddCommand("chain", _ => { }, 0, -1, string.Empty, null, chainedCompleter);

            CommandAutoComplete autoComplete = new CommandAutoComplete(history, shell);

            string[] commandSuggestions = autoComplete.Complete("cha");
            CollectionAssert.Contains(commandSuggestions, "chain");

            string[] firstArgumentSuggestions = autoComplete.Complete("chain ");
            CollectionAssert.AreEquivalent(
                new[] { "chain alpha", "chain beta" },
                firstArgumentSuggestions
            );
            Assert.AreEqual(1, chainedCompleter.Calls.Count);
            CommandCompletionContext firstContext = chainedCompleter.Calls[0];
            Assert.AreEqual(0, firstContext.ArgIndex);
            Assert.AreEqual(string.Empty, firstContext.PartialArg);
            Assert.AreEqual(0, firstContext.ArgsBeforeCursor.Count);

            string[] secondArgumentSuggestions = autoComplete.Complete("chain alpha ");
            CollectionAssert.Contains(secondArgumentSuggestions, "chain alpha gamma");
            Assert.AreEqual(2, chainedCompleter.Calls.Count);
            CommandCompletionContext secondContext = chainedCompleter.Calls[1];
            Assert.AreEqual(1, secondContext.ArgIndex);
            Assert.AreEqual(string.Empty, secondContext.PartialArg);
            Assert.AreEqual(1, secondContext.ArgsBeforeCursor.Count);
            Assert.AreEqual("alpha", secondContext.ArgsBeforeCursor[0].contents);

            chainedCompleter.Calls.Clear();
            string[] partialFirstArgument = autoComplete.Complete("chain a");
            CollectionAssert.Contains(partialFirstArgument, "chain alpha");
            Assert.AreEqual(1, chainedCompleter.Calls.Count);
            CommandCompletionContext partialFirstContext = chainedCompleter.Calls[0];
            Assert.AreEqual(0, partialFirstContext.ArgIndex);
            Assert.AreEqual("a", partialFirstContext.PartialArg);
            Assert.AreEqual(0, partialFirstContext.ArgsBeforeCursor.Count);

            chainedCompleter.Calls.Clear();
            string[] partialSecondArgument = autoComplete.Complete("chain alpha g");
            CollectionAssert.Contains(partialSecondArgument, "chain alpha gamma");
            Assert.AreEqual(1, chainedCompleter.Calls.Count);
            CommandCompletionContext partialSecondContext = chainedCompleter.Calls[0];
            Assert.AreEqual(1, partialSecondContext.ArgIndex);
            Assert.AreEqual("g", partialSecondContext.PartialArg);
            Assert.AreEqual(1, partialSecondContext.ArgsBeforeCursor.Count);
            Assert.AreEqual("alpha", partialSecondContext.ArgsBeforeCursor[0].contents);
        }

        [Test]
        public void AutoCompleteHonorsCaretIndexWithinInput()
        {
            CommandHistory history = new CommandHistory(16);
            CommandShell shell = new CommandShell(history);
            ChainedCompleter chainedCompleter = new ChainedCompleter();
            shell.AddCommand("chain", _ => { }, 0, -1, string.Empty, null, chainedCompleter);

            CommandAutoComplete autoComplete = new CommandAutoComplete(history, shell);
            List<string> buffer = new List<string>();
            int caretIndex = "chain alpha ".Length;
            autoComplete.Complete("chain alpha gamma", caretIndex, buffer);

            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual("chain alpha gamma", buffer[0]);
            Assert.AreEqual(1, chainedCompleter.Calls.Count);
            CommandCompletionContext context = chainedCompleter.Calls[0];
            Assert.AreEqual(1, context.ArgIndex);
            Assert.AreEqual(string.Empty, context.PartialArg);
            Assert.AreEqual(1, context.ArgsBeforeCursor.Count);
            Assert.AreEqual("alpha", context.ArgsBeforeCursor[0].contents);
        }

        [Test]
        public void AutoCompleteFallsBackToHistoryWhenCommandUnknown()
        {
            CommandHistory history = new CommandHistory(16);
            history.Push("login", true, true);
            history.Push("logout", true, true);
            CommandShell shell = new CommandShell(history);

            CommandAutoComplete autoComplete = new CommandAutoComplete(history, shell);
            string[] suggestions = autoComplete.Complete("lo");

            Assert.IsNotNull(suggestions);
            CollectionAssert.Contains(suggestions, "login");
            CollectionAssert.Contains(suggestions, "logout");
        }

        [Test]
        public void AutoCompleteDeduplicatesValuesAcrossSources()
        {
            CommandHistory history = new CommandHistory(16);
            history.Push("list", true, true);
            CommandShell shell = new CommandShell(history);
            shell.AddCommand("list", _ => { });
            List<string> knownWords = new List<string> { "list" };
            CommandAutoComplete autoComplete = new CommandAutoComplete(history, shell, knownWords);

            string[] suggestions = autoComplete.Complete("li");
            Assert.IsNotNull(suggestions);

            int matches = 0;
            for (int i = 0; i < suggestions.Length; ++i)
            {
                if (string.Equals(suggestions[i], "list", StringComparison.Ordinal))
                {
                    matches++;
                }
            }

            Assert.AreEqual(1, matches, "Expected deduplicated suggestion list.");
        }
    }
}
