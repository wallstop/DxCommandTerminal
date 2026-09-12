namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;

    /*
        Contract tests for the builder's unbounded trailing argument (PLAN T09
        remainder): Remaining<T> collects every trailing token into one typed
        array, bounds/usage/completion derive from the same declaration, and
        misconfiguration throws at definition time.
     */
    public sealed class CommandBuilderRemainingTests
    {
        private Func<CommandExecutionContext> _previousAmbientProvider;

        private static CommandHistory History() => new(16);

        private static string ConsumeError(CommandShell shell)
        {
            return shell.TryConsumeErrorMessage(out string error) ? error : null;
        }

        [SetUp]
        public void SetUp()
        {
            _previousAmbientProvider = CommandExecutionContext.AmbientContextProvider;
            CommandExecutionContext.AmbientContextProvider = null;
        }

        [TearDown]
        public void TearDown()
        {
            CommandExecutionContext.AmbientContextProvider = _previousAmbientProvider;
            if (TerminalUI.Instance != null)
            {
                UnityEngine.Object.Destroy(TerminalUI.Instance.gameObject);
            }
        }

        [Test]
        public void RemainingCollectsAllTrailingTokensOrReadsEmpty()
        {
            CommandShell shell = new(History());
            List<string[]> observed = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("say")
                        .Remaining<string>("message")
                        .Handler(
                            (context, arguments) => observed.Add(arguments.Get<string[]>("message"))
                        ),
                    out _
                ),
                "The builder should register cleanly"
            );
            Assert.IsNull(
                shell.Commands["say"].maxArgCount,
                "A remaining argument leaves the command unbounded"
            );

            Assert.IsTrue(shell.RunCommand("say hello big world"));
            Assert.IsTrue(shell.RunCommand("say"));
            CollectionAssert.AreEqual(
                new[] { new[] { "hello", "big", "world" }, Array.Empty<string>() },
                observed,
                "Trailing tokens collect in order; an invocation without them reads empty"
            );
            Assert.IsNull(ConsumeError(shell));
            Assert.IsNull(ConsumeError(shell));
        }

        [Test]
        public void RequiredRemainingDemandsAtLeastOneToken()
        {
            CommandShell shell = new(History());
            List<string[]> observed = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("say")
                        .Remaining<string>("message", spec => spec.Required())
                        .Handler(
                            (context, arguments) => observed.Add(arguments.Get<string[]>("message"))
                        ),
                    out _
                )
            );

            Assert.IsFalse(
                shell.RunCommand("say"),
                "A required remaining argument with no tokens must not dispatch"
            );
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "The bounds violation queues an error");
            Assert.That(error, Does.Contain("at least 1 argument"));
            Assert.That(
                error,
                Does.Contain("<message:string...>"),
                "The derived usage hint marks the remaining argument"
            );
            Assert.IsEmpty(observed, "The handler must not run");

            Assert.IsTrue(shell.RunCommand("say hi"));
            CollectionAssert.AreEqual(
                new[] { new[] { "hi" } },
                observed,
                "One token satisfies a required remaining argument"
            );
            Assert.IsNull(ConsumeError(shell));
        }

        [Test]
        public void FixedArgumentsComposeWithRemaining()
        {
            CommandShell shell = new(History());
            int amount = 0;
            string target = null;
            string[] tags = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("heal")
                        .Arg<int>("amount", spec => spec.Required().Range(1, 100))
                        .Arg<string>("target", spec => spec.Default("self"))
                        .Remaining<string>("tags")
                        .Handler(
                            (context, arguments) =>
                            {
                                amount = arguments.Get<int>(0);
                                target = arguments.Get<string>(1);
                                tags = arguments.Get<string[]>("tags");
                            }
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("heal 5 red green"));
            Assert.AreEqual(5, amount);
            Assert.AreEqual("red", target, "A provided optional argument consumes its token");
            CollectionAssert.AreEqual(
                new[] { "green" },
                tags,
                "Tokens after the fixed arguments collect as the remaining array"
            );

            Assert.IsTrue(shell.RunCommand("heal 9"));
            Assert.AreEqual(9, amount);
            Assert.AreEqual("self", target, "An omitted optional argument keeps its default");
            Assert.IsEmpty(tags, "No trailing tokens read as an empty array");
            Assert.IsNull(ConsumeError(shell));
            Assert.IsNull(ConsumeError(shell));
        }

        [TestCase("1 x 3")]
        [TestCase("x")]
        public void RemainingParseFailureRejectsInvocationAndSkipsHandler(string input)
        {
            CommandShell shell = new(History());
            int invocations = 0;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("add")
                        .Remaining<int>("values")
                        .Handler((context, arguments) => ++invocations),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand($"add {input}"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "A parse failure must queue a controlled error");
            Assert.That(error, Does.Contain("values"));
            Assert.AreEqual(0, invocations, "The handler must not run when parsing fails");
        }

        [Test]
        public void RemainingValidationAppliesPerElement()
        {
            CommandShell shell = new(History());
            List<int[]> observed = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("buff")
                        .Remaining<int>("levels", spec => spec.Range(1, 10))
                        .Handler(
                            (context, arguments) => observed.Add(arguments.Get<int[]>("levels"))
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("buff 3 10 1"));
            CollectionAssert.AreEqual(
                new[] { new[] { 3, 10, 1 } },
                observed,
                "Every element inside the range passes"
            );

            Assert.IsTrue(shell.RunCommand("buff 5 0 7"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "An out-of-range element rejects the invocation");
            Assert.That(error, Does.Contain("out of range"));
            Assert.AreEqual(1, observed.Count, "The rejected invocation skips the handler");
        }

        [Test]
        public void RemainingReadsRequireArrayType()
        {
            CommandShell shell = new(History());
            string[] message = null;
            Exception mismatch = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("say")
                        .Remaining<string>("message")
                        .Handler(
                            (context, arguments) =>
                            {
                                message = arguments.Get<string[]>("message");
                                Assert.IsTrue(
                                    arguments.TryGet<string[]>("message", out string[] _),
                                    "TryGet reads the remaining array"
                                );
                                try
                                {
                                    arguments.Get<string>("message");
                                }
                                catch (InvalidOperationException e)
                                {
                                    mismatch = e;
                                }
                            }
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("say hi"));
            CollectionAssert.AreEqual(new[] { "hi" }, message);
            Assert.IsNotNull(
                mismatch,
                "Reading a remaining argument as its element type is a programming error"
            );
            Assert.IsTrue(
                mismatch is CommandArgumentTypeMismatchException,
                "The element-type read throws the typed binder exception"
            );
            CommandArgumentTypeMismatchException typed =
                (CommandArgumentTypeMismatchException)mismatch;
            Assert.AreEqual(
                typeof(string[]),
                typed.StoredType,
                "The remaining argument's slot holds the element-type array"
            );
            Assert.AreEqual(
                typeof(string),
                typed.RequestedType,
                "The element-type read requested the bare element type"
            );
        }

        [TestCase("", ExpectedResult = "say [message:string...]")]
        [TestCase(" required", ExpectedResult = "say <message:string...>")]
        public string DerivedUsageHintMarksRemainingWithEllipsis(string requiredSuffix)
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("say")
                        .Remaining<string>(
                            "message",
                            spec => requiredSuffix.Length == 0 ? spec : spec.Required()
                        )
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            return shell.Commands["say"].hint;
        }

        [Test]
        public void ArgumentsAfterRemainingFailAtDefinitionTime()
        {
            CommandShell shell = new(History());
            CommandConfigurationException exception = Assert.Throws<CommandConfigurationException>(
                () =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("mixed")
                            .Remaining<string>("rest")
                            .Arg<int>("count")
                            .Handler((context, arguments) => { }),
                        out _
                    )
            );
            Assert.That(exception.Message, Does.Contain("count"));
        }

        [Test]
        public void SecondRemainingArgumentFailsAtDefinitionTime()
        {
            CommandShell shell = new(History());
            CommandConfigurationException exception = Assert.Throws<CommandConfigurationException>(
                () =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("double")
                            .Remaining<string>("first")
                            .Remaining<string>("second")
                            .Handler((context, arguments) => { }),
                        out _
                    )
            );
            Assert.That(exception.Message, Does.Contain("remaining"));
        }

        [Test]
        public void DefaultOnRemainingArgumentFailsAtDefinitionTime()
        {
            CommandShell shell = new(History());
            CommandConfigurationException exception = Assert.Throws<CommandConfigurationException>(
                () =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("say")
                            .Remaining<string>("message", spec => spec.Default("none"))
                            .Handler((context, arguments) => { }),
                        out _
                    )
            );
            Assert.That(exception.Message, Does.Contain("default"));
        }

        [Test]
        public void RequiredRemainingAfterOptionalArgumentFailsAtDefinitionTime()
        {
            CommandShell shell = new(History());
            CommandConfigurationException exception = Assert.Throws<CommandConfigurationException>(
                () =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("say")
                            .Arg<string>("target", spec => spec.Default("all"))
                            .Remaining<string>("message", spec => spec.Required())
                            .Handler((context, arguments) => { }),
                        out _
                    )
            );
            Assert.That(
                exception.Message,
                Does.Contain("before every optional argument"),
                "A required remaining argument after an optional one is ambiguous with the optional"
            );
        }

        [Test]
        public void SubcommandRouteAcceptsUnboundedTrailingArguments()
        {
            CommandShell shell = new(History());
            string[] dropped = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<string>("item", spec => spec.Required())
                                    .Remaining<string>("extras")
                                    .Handler(
                                        (context, arguments) =>
                                            dropped = arguments.Get<string[]>("extras")
                                    )
                        )
                        .Subcommand(
                            "count",
                            count =>
                                count
                                    .Arg<string>("item", spec => spec.Required())
                                    .Handler((context, arguments) => { })
                        )
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inventory add pickaxe torch rope"));
            CollectionAssert.AreEqual(
                new[] { "torch", "rope" },
                dropped,
                "The routed subcommand collects its trailing tokens"
            );

            Assert.IsTrue(shell.RunCommand("inventory add pickaxe"));
            Assert.IsEmpty(dropped, "No trailing tokens read as an empty array");

            Assert.IsTrue(
                shell.RunCommand("inventory count pickaxe torch"),
                "A sibling route without a remaining argument still overflows its bounds"
            );
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "The fixed sibling route rejects surplus arguments");
            Assert.That(error, Does.Contain("at most 1 argument"));
        }

        [Test]
        public void RemainingChoicesCompleteAtEveryTrailingStage()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("summon")
                        .Arg<string>("mode", spec => spec.Required().Choices("dog", "cat"))
                        .Remaining<string>("tags", spec => spec.Choices("red", "blue"))
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            CommandExecutionContext context = CommandExecutionContext.Current;
            Assert.IsTrue(shell.TryComplete(context, "summon ", 7, results, out _));
            CollectionAssert.AreEqual(
                new[] { "dog", "cat" },
                results.ConvertAll(completion => completion.InsertionText),
                "Stage 0 completes the fixed argument"
            );

            Assert.IsTrue(shell.TryComplete(context, "summon dog ", 11, results, out _));
            CollectionAssert.AreEqual(
                new[] { "red", "blue" },
                results.ConvertAll(completion => completion.InsertionText),
                "Stage 1 completes the remaining argument"
            );

            Assert.IsTrue(shell.TryComplete(context, "summon dog red ", 15, results, out _));
            CollectionAssert.AreEqual(
                new[] { "red", "blue" },
                results.ConvertAll(completion => completion.InsertionText),
                "A trailing stage past the fixed arguments completes the remaining choices"
            );

            Assert.IsTrue(shell.TryComplete(context, "summon dog red b", 16, results, out _));
            CollectionAssert.AreEqual(
                new[] { "blue" },
                results.ConvertAll(completion => completion.InsertionText),
                "Every trailing stage filters the remaining choices by the active token"
            );
        }

        [Test]
        public void RemainingSubcommandChoicesCompleteAtEveryTrailingStage()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<string>("item", spec => spec.Required().Choices("pickaxe"))
                                    .Remaining<string>("tags", spec => spec.Choices("red", "blue"))
                                    .Handler((context, arguments) => { })
                        )
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            CommandExecutionContext context = CommandExecutionContext.Current;
            Assert.IsTrue(shell.TryComplete(context, "inventory add pickaxe ", 23, results, out _));
            CollectionAssert.AreEqual(
                new[] { "red", "blue" },
                results.ConvertAll(completion => completion.InsertionText),
                "The routed subcommand completes its remaining argument"
            );

            Assert.IsTrue(
                shell.TryComplete(context, "inventory add pickaxe red b", 27, results, out _)
            );
            CollectionAssert.AreEqual(
                new[] { "blue" },
                results.ConvertAll(completion => completion.InsertionText),
                "Every trailing stage filters the remaining choices by the active token"
            );
        }
    }
}
