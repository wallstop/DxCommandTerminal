namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class CommandCompletionTests
    {
        private static CommandCompletionProvider Record(List<int> stages)
        {
            return (in CommandCompletionContext context, List<CommandCompletion> results) =>
                stages.Add(context.ActiveArgumentIndex);
        }

        private static void Invoke(CommandCompletionProvider provider, int activeArgumentIndex)
        {
            provider(CreateContext(activeArgumentIndex), new List<CommandCompletion>());
        }

        private static CommandCompletionContext CreateContext(int activeArgumentIndex)
        {
            return new CommandCompletionContext(
                CommandExecutionContext.Current,
                string.Empty,
                0,
                activeArgumentIndex,
                new List<CommandArg>(),
                string.Empty,
                0,
                0,
                false,
                null
            );
        }

        [Test]
        public void ChoiceFormattingPreservesQuotedWhitespace(
            [Values("static", "dynamic", "formatted")] string source,
            [Values(" ", "\t", "\r\n", "\u2003", " padded ")] string value
        )
        {
            CommandShell shell = new(new CommandHistory(16));
            string received = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("whitespace-choice")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<string>(
                            "value",
                            spec =>
                                source switch
                                {
                                    "static" => spec.Required().Choices(value),
                                    "dynamic" => spec.Required()
                                        .Choices(context => new[] { value }),
                                    _ => spec.Required()
                                        .Choices(context => new[] { value }, item => item),
                                }
                        )
                        .Handler((context, arguments) => received = arguments.Get<string>("value")),
                    out CommandRegistrationHandle handle
                )
            );
            using (handle)
            {
                const string input = "whitespace-choice \"\"";
                List<CommandCompletion> results = new();
                Assert.IsTrue(
                    shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        input.Length - 1,
                        results,
                        out CommandCompletionContext context
                    )
                );
                Assert.AreEqual(1, results.Count);
                Assert.AreEqual(value, results[0].InsertionText);
                Assert.IsTrue(context.IsQuoted);
                string insertion = CommandTokenizer.QuoteInsertionIfNeeded(
                    results[0].InsertionText,
                    context.IsQuoted
                );
                string completed = input
                    .Remove(context.ReplacementStart, context.ReplacementLength)
                    .Insert(context.ReplacementStart, insertion);
                shell.RunCommand(completed);
                Assert.AreEqual(value, received);
                Assert.IsFalse(shell.TryConsumeErrorMessage(out _));
            }
        }

        [TestCase("static")]
        [TestCase("dynamic")]
        [TestCase("formatted")]
        public void EmptyChoiceTextIsSkippedBeforePrefixMatching(string source)
        {
            CommandArgumentSpec<string> spec = new("value");
            spec = source switch
            {
                "static" => spec.Choices(string.Empty, "valid"),
                "dynamic" => spec.Choices(context => new[] { string.Empty, "valid" }),
                _ => spec.Choices(context => new[] { null, string.Empty, "valid" }, value => value),
            };
            List<CommandCompletion> results = new();
            spec.AppendCompletions(CreateContext(0), results);
            CollectionAssert.AreEqual(
                new[] { "valid" },
                results.Select(item => item.InsertionText)
            );
        }

        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void ChoiceCallbackFailuresDiscardResultsAndRecover(
            bool providerThrows,
            bool providerReturnsNull,
            bool formatterThrows
        )
        {
            CommandShell shell = new(new CommandHistory(16));
            bool fail = true;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("callback-choice")
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<string>(
                            "value",
                            spec =>
                                spec.Choices(
                                    context =>
                                    {
                                        if (fail && providerThrows)
                                        {
                                            throw new InvalidOperationException("choice failed");
                                        }

                                        return fail && providerReturnsNull
                                            ? null
                                            : new[] { "first", "second" };
                                    },
                                    value =>
                                    {
                                        if (
                                            fail
                                            && formatterThrows
                                            && string.Equals(
                                                value,
                                                "second",
                                                StringComparison.Ordinal
                                            )
                                        )
                                        {
                                            throw new InvalidOperationException("choice failed");
                                        }

                                        return value;
                                    }
                                )
                        )
                        .Handler((context, arguments) => { }),
                    out CommandRegistrationHandle handle
                )
            );
            using (handle)
            {
                const string input = "callback-choice ";
                List<CommandCompletion> results = new() { new CommandCompletion("stale") };
                if (!providerReturnsNull)
                {
                    LogAssert.Expect(
                        LogType.Error,
                        "[DxCommandTerminal] Completion provider for 'callback-choice' failed: choice failed"
                    );
                }

                Assert.IsTrue(
                    shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        input.Length,
                        results,
                        out _
                    )
                );
                Assert.IsEmpty(results);
                fail = false;
                Assert.IsTrue(
                    shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        input.Length,
                        results,
                        out _
                    )
                );
                CollectionAssert.AreEqual(
                    new[] { "first", "second" },
                    results.Select(item => item.InsertionText)
                );
            }
        }

        [Test]
        public void ReplacementOverrideValidatesBothValues()
        {
            Assert.DoesNotThrow(() => new CommandCompletionReplacement(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CommandCompletionReplacement(-1, 0),
                "A negative start is rejected"
            );
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CommandCompletionReplacement(0, -1),
                "A negative length is rejected"
            );
        }

        [Test]
        public void ReplacementOverrideIsAtomic()
        {
            CommandCompletion withoutOverride = new("alpha");
            Assert.IsFalse(
                withoutOverride.HasReplacementOverride,
                "No replacement means the context range applies"
            );
            Assert.That(withoutOverride.Replacement == null);

            CommandCompletion withOverride = new(
                "alpha",
                replacement: new CommandCompletionReplacement(2, 3)
            );
            Assert.IsTrue(withOverride.HasReplacementOverride);
            CommandCompletionReplacement replacement = withOverride.Replacement.Value;
            Assert.AreEqual(2, replacement.Start);
            Assert.AreEqual(3, replacement.Length);
        }

        [Test]
        public void StagedSingleStageCompletesStageZeroOnly()
        {
            List<int> stages = new();
            CommandCompletionProvider provider = CommandCompletionProviders.Staged(Record(stages));

            Assert.Throws<ArgumentNullException>(
                () => CommandCompletionProviders.Staged((CommandCompletionProvider)null),
                "A null single stage is rejected"
            );

            Invoke(provider, 0);
            Invoke(provider, 1);
            CollectionAssert.AreEqual(new[] { 0 }, stages);
        }

        [Test]
        public void StagedTwoAndThreeStageOverloadsDispatchByStage()
        {
            List<int> twoStages = new();
            List<int> threeStages = new();
            CommandCompletionProvider two = CommandCompletionProviders.Staged(
                Record(twoStages),
                Record(twoStages)
            );
            CommandCompletionProvider three = CommandCompletionProviders.Staged(
                Record(threeStages),
                Record(threeStages),
                Record(threeStages)
            );

            Assert.Throws<ArgumentNullException>(
                () => CommandCompletionProviders.Staged(Record(twoStages), null),
                "Null fixed stages are rejected"
            );

            Invoke(two, 0);
            Invoke(two, 1);
            Invoke(two, 2);
            CollectionAssert.AreEqual(new[] { 0, 1 }, twoStages);

            Invoke(three, 1);
            Invoke(three, 3);
            CollectionAssert.AreEqual(new[] { 1 }, threeStages);
        }

        [Test]
        public void StagedParamsAcceptsNullStagesAndBeyondListRequests()
        {
            List<int> stages = new();
            CommandCompletionProvider provider = CommandCompletionProviders.Staged(
                Record(stages),
                null,
                Record(stages),
                Record(stages)
            );

            Invoke(provider, 0);
            Invoke(provider, 1);
            Invoke(provider, 2);
            Invoke(provider, 5);
            CollectionAssert.AreEqual(
                new[] { 0, 2 },
                stages,
                "Null stages produce nothing; stages beyond the list produce nothing"
            );

            Assert.Throws<ArgumentNullException>(
                () => CommandCompletionProviders.Staged((CommandCompletionProvider[])null),
                "A null stage array is rejected"
            );
        }
    }
}
