namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    /*
        Contract tests for the typed command builder (PLAN T09 phase 1):
        fluent definitions register into the same shell, arguments parse and
        validate before the handler runs, usage/bounds/completion derive from
        the same definition, and the registration handle removes exactly its
        own command.
     */
    public sealed class CommandBuilderTests
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
        public void TypedArgumentsParseAndDispatch()
        {
            CommandShell shell = new(History());
            int amount = 0;
            string target = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("heal", "Heals a target")
                        .Arg<int>("amount", spec => spec.Required().Range(1, 100))
                        .Arg<string>("target", spec => spec.Default("self"))
                        .Handler(
                            (context, arguments) =>
                            {
                                amount = arguments.Get<int>(0);
                                target = arguments.Get<string>(1);
                            }
                        ),
                    out _
                ),
                "The builder should register cleanly"
            );

            Assert.IsTrue(shell.RunCommand("heal 5 ally"));
            Assert.AreEqual(5, amount, "The int argument should parse");
            Assert.AreEqual("ally", target, "The string argument should pass through");
            Assert.IsNull(ConsumeError(shell), "A valid invocation queues no error");
        }

        [Test]
        public void MissingRequiredArgumentReportsBoundsAndSkipsHandler()
        {
            CommandShell shell = new(History());
            int invocations = 0;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("heal")
                        .Arg<int>("amount", spec => spec.Required())
                        .Handler((context, arguments) => ++invocations),
                    out _
                )
            );

            Assert.IsFalse(
                shell.RunCommand("heal"),
                "A missing required argument must not dispatch"
            );
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "A bounds violation queues an error");
            Assert.That(
                error,
                Does.Contain("exactly 1 argument"),
                "The derived bounds should drive the shell's bounds message"
            );
            Assert.That(
                error,
                Does.Contain("<amount:int>"),
                "The derived usage hint should appear in the bounds message"
            );
            Assert.AreEqual(0, invocations, "The handler must not run with missing arguments");
        }

        [Test]
        public void TooManyArgumentsReportsBoundsAndSkipsHandler()
        {
            CommandShell shell = new(History());
            int invocations = 0;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("heal")
                        .Arg<int>("amount", spec => spec.Required())
                        .Handler((context, arguments) => ++invocations),
                    out _
                )
            );

            Assert.IsFalse(shell.RunCommand("heal 5 extra"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("exactly 1 argument"));
            Assert.AreEqual(0, invocations);
        }

        [Test]
        public void OptionalArgumentFallsBackToDefault()
        {
            CommandShell shell = new(History());
            List<string> observedTargets = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("wave")
                        .Arg<string>("target", spec => spec.Default("everyone"))
                        .Handler(
                            (context, arguments) => observedTargets.Add(arguments.Get<string>(0))
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("wave"));
            Assert.IsTrue(shell.RunCommand("wave bob"));
            CollectionAssert.AreEqual(
                new[] { "everyone", "bob" },
                observedTargets,
                "An omitted optional argument uses its default; a provided one parses"
            );
            Assert.IsNull(ConsumeError(shell));
            Assert.IsNull(ConsumeError(shell));
        }

        [Test]
        public void OmittedOptionalArgumentsReadAsTheirDefaults()
        {
            CommandShell shell = new(History());
            string note = "sentinel";
            bool notePresent = false;
            int count = -1;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("annotate")
                        .Arg<string>("note")
                        .Arg<int>("count")
                        .Handler(
                            (context, arguments) =>
                            {
                                note = arguments.Get<string>(0);
                                notePresent = arguments.TryGet(0, out string _);
                                count = arguments.Get<int>(1);
                            }
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("annotate"));
            Assert.IsNull(note, "An optional string without an explicit default reads as null");
            Assert.IsTrue(
                notePresent,
                "A correctly-typed null default is a successful read, not a mismatch"
            );
            Assert.AreEqual(
                0,
                count,
                "An optional value type without a default reads as default(T)"
            );
        }

        [Test]
        public void TypedAccessorValidatesIndices()
        {
            CommandShell shell = new(History());
            Exception negativeGet = null;
            Exception overflowGet = null;
            bool negativeTryGet = true;
            bool overflowTryGet = true;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("probe")
                        .Arg<int>("value", spec => spec.Required())
                        .Handler(
                            (context, arguments) =>
                            {
                                try
                                {
                                    arguments.Get<int>(-1);
                                }
                                catch (ArgumentOutOfRangeException e)
                                {
                                    negativeGet = e;
                                }

                                try
                                {
                                    arguments.Get<int>(1);
                                }
                                catch (ArgumentOutOfRangeException e)
                                {
                                    overflowGet = e;
                                }

                                negativeTryGet = arguments.TryGet<int>(-1, out _);
                                overflowTryGet = arguments.TryGet<int>(5, out _);
                            }
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("probe 1"));
            Assert.IsNotNull(negativeGet, "A negative index throws ArgumentOutOfRangeException");
            Assert.IsNotNull(
                overflowGet,
                "An out-of-range index throws ArgumentOutOfRangeException"
            );
            Assert.IsFalse(negativeTryGet, "TryGet reports false for a negative index");
            Assert.IsFalse(overflowTryGet, "TryGet reports false for an out-of-range index");
        }

        [TestCase("abc", ExpectedResult = true)]
        [TestCase("1.5", ExpectedResult = true)]
        [TestCase("", ExpectedResult = false)]
        public bool ParseFailureReportsControlledErrorAndSkipsHandler(string input)
        {
            CommandShell shell = new(History());
            int invocations = 0;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("heal")
                        .Arg<int>("amount", spec => spec.Required())
                        .Handler((context, arguments) => ++invocations),
                    out _
                )
            );

            /*
                Like the built-in commands' own argument handling, a parse
                failure is a controlled error on a recognized command: the
                handler is skipped and the shell's result stays true (bounds
                violations, handled before the handler, still return false).
             */
            bool dispatched = shell.RunCommand($"heal {input}");
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "A parse failure must queue a controlled error");
            Assert.That(error, Does.Contain("amount"));
            Assert.AreEqual(0, invocations, "The handler must not run when parsing fails");
            return dispatched;
        }

        [Test]
        public void StaticChoicesValidateAndComplete()
        {
            CommandShell shell = new(History());
            List<string> picked = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("pickup")
                        .Arg<string>("item", spec => spec.Required().Choices("pickaxe", "torch"))
                        .Handler((context, arguments) => picked.Add(arguments.Get<string>(0))),
                    out _
                )
            );

            /*
                Like the built-in commands' own argument handling, a choice
                violation is a controlled error on a recognized command: the
                handler is skipped while the shell reports a dispatch.
             */
            Assert.IsTrue(shell.RunCommand("pickup hammer"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "A value outside the choices must be rejected");
            Assert.That(error, Does.Contain("pickaxe, torch"));
            Assert.AreEqual(0, picked.Count);

            Assert.IsTrue(
                shell.RunCommand("pickup PICKAXE"),
                "String choices match case-insensitively like command names"
            );
            Assert.AreEqual("PICKAXE", picked[0]);
            Assert.IsNull(ConsumeError(shell));

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(CommandExecutionContext.Current, "pickup to", 10, results, out _),
                "A builder command with choices should answer completion requests"
            );
            CollectionAssert.AreEqual(
                new[] { "torch" },
                results.ConvertAll(completion => completion.InsertionText),
                "Choices filter by the active token prefix"
            );
        }

        [Test]
        public void NumericRangeValidates()
        {
            CommandShell shell = new(History());
            List<float> observed = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("scale")
                        .Arg<float>("factor", spec => spec.Required().Range(0.5f, 3f))
                        .Handler((context, arguments) => observed.Add(arguments.Get<float>(0))),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("scale 0.5"));
            Assert.IsTrue(shell.RunCommand("scale 3"));
            Assert.IsTrue(shell.RunCommand("scale 0.4"));
            Assert.IsTrue(shell.RunCommand("scale 3.1"));
            CollectionAssert.AreEqual(
                new[] { 0.5f, 3f },
                observed,
                "Boundary values are inclusive"
            );
            string below = ConsumeError(shell);
            Assert.IsNotNull(below, "An out-of-range value queues a controlled error");
            Assert.That(below, Does.Contain("out of range"));
            Assert.IsNotNull(ConsumeError(shell), "The above-range case also queues an error");
            Assert.AreEqual(2, observed.Count, "Out-of-range invocations skip the handler");
        }

        [Test]
        public void CustomValidatorRunsAfterParsing()
        {
            CommandShell shell = new(History());
            List<int> observed = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("port")
                        .Arg<int>(
                            "port",
                            spec =>
                                spec.Required()
                                    .Validate(value =>
                                        (1 <= value) && (value <= 65535)
                                            ? null
                                            : "must be a valid port number"
                                    )
                        )
                        .Handler((context, arguments) => observed.Add(arguments.Get<int>(0))),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("port 8080"));
            Assert.AreEqual(1, observed.Count);
            Assert.IsTrue(shell.RunCommand("port 0"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("must be a valid port number"));
            Assert.AreEqual(1, observed.Count, "The handler must not run when validation fails");
        }

        [Test]
        public void EnumChoicesParseAndComplete()
        {
            CommandShell shell = new(History());
            List<WeaponType> observed = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("equip")
                        .Arg<WeaponType>("weapon", spec => spec.Required().EnumChoices())
                        .Handler(
                            (context, arguments) => observed.Add(arguments.Get<WeaponType>(0))
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("equip Bow"));
            Assert.AreEqual(WeaponType.Bow, observed[0]);
            Assert.IsTrue(shell.RunCommand("equip axe"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "Unknown enum names are rejected");
            Assert.That(error, Does.Contain("weapon"));
            Assert.AreEqual(1, observed.Count);

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(CommandExecutionContext.Current, "equip ", 6, results, out _)
            );
            CollectionAssert.AreEqual(
                new[] { "Sword", "Bow", "Staff" },
                results.ConvertAll(completion => completion.InsertionText),
                "Enum choices list every enum name"
            );
        }

        [Test]
        public void BoolChoicesCompleteBothValues()
        {
            CommandShell shell = new(History());
            List<bool> observed = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("godmode")
                        .Arg<bool>("enabled", spec => spec.Required().BoolChoices())
                        .Handler((context, arguments) => observed.Add(arguments.Get<bool>(0))),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("godmode true"));
            Assert.IsTrue(shell.RunCommand("godmode false"));
            CollectionAssert.AreEqual(new[] { true, false }, observed);

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(CommandExecutionContext.Current, "godmode tr", 10, results, out _)
            );
            CollectionAssert.AreEqual(
                new[] { "true" },
                results.ConvertAll(completion => completion.InsertionText)
            );
        }

        [Test]
        public void DynamicChoicesCompleteFromLiveContext()
        {
            CommandShell shell = new(History());
            List<CommandCompletionContext> requests = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("summon")
                        .Arg<string>("item", spec => spec.Required().Choices("pickaxe", "torch"))
                        .Arg<string>(
                            "ally",
                            spec =>
                                spec.Required()
                                    .Choices(context =>
                                    {
                                        requests.Add(context);
                                        return new[] { "ally-" + context.PrecedingArguments.Count };
                                    })
                        )
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(CommandExecutionContext.Current, "summon ", 7, results, out _)
            );
            CollectionAssert.AreEqual(
                new[] { "pickaxe", "torch" },
                results.ConvertAll(completion => completion.InsertionText)
            );

            /*
                Stage 1 sees the completed first argument as live context: the
                provider re-queries on every request.
             */
            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "summon pickaxe ",
                    15,
                    results,
                    out _
                )
            );
            Assert.AreEqual(1, requests.Count, "The provider runs once per stage-1 request");
            CollectionAssert.AreEqual(
                new[] { "ally-1" },
                results.ConvertAll(completion => completion.InsertionText),
                "Dynamic providers re-query on every request"
            );

            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "summon torch ",
                    13,
                    results,
                    out _
                )
            );
            Assert.AreEqual(2, requests.Count, "A second request queries the provider again");
            CollectionAssert.AreEqual(
                new[] { "ally-1" },
                results.ConvertAll(completion => completion.InsertionText),
                "Dynamic choices recompute instead of caching"
            );
        }

        [Test]
        public void DerivedUsageHintOrdersRequiredAndOptional()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("teleport")
                        .Help("Teleports the player")
                        .Arg<Vector3>("position", spec => spec.Required())
                        .Arg<float>("delay", spec => spec.Default(0f))
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            CommandInfo info = shell.Commands["teleport"];
            StringAssert.AreEqualIgnoringCase(
                "teleport <position:Vector3> [delay:float]",
                info.hint,
                "Required arguments render as <name:type>, optional ones as [name:type]"
            );
        }

        [Test]
        public void ExplicitHintOverridesDerivedUsage()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("shout")
                        .Hint("shout <message...>")
                        .Arg<string>("message", spec => spec.Required())
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            Assert.AreEqual(
                "shout <message...>",
                shell.Commands["shout"].hint,
                "An explicit hint wins over the derived usage"
            );
        }

        [Test]
        public void NoChoicesLeavesHistoryCompletionUntouched()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("noop-args")
                        .Arg<string>("value", spec => spec.Required())
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            Assert.IsNull(
                shell.Commands["noop-args"].completionProvider,
                "Commands without choices keep the history-based completion path"
            );
            Assert.IsFalse(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "noop-args ",
                    10,
                    new List<CommandCompletion>(),
                    out _
                ),
                "No provider means the shell reports no provider-backed completion"
            );
        }

        [Test]
        public void CompletionStagesChainByArgument()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("give")
                        .Arg<string>("item", spec => spec.Required().Choices("pickaxe", "torch"))
                        .Arg<int>("count", spec => spec.Default(1).Choices(1, 10))
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            CommandExecutionContext context = CommandExecutionContext.Current;
            Assert.IsTrue(shell.TryComplete(context, "give ", 5, results, out _));
            CollectionAssert.AreEqual(
                new[] { "pickaxe", "torch" },
                results.ConvertAll(completion => completion.InsertionText),
                "Stage 0 completes the first argument"
            );

            Assert.IsTrue(shell.TryComplete(context, "give pickaxe ", 13, results, out _));
            CollectionAssert.AreEqual(
                new[] { "1", "10" },
                results.ConvertAll(completion => completion.InsertionText),
                "Stage 1 completes the second argument with formatted choices"
            );

            Assert.IsTrue(shell.TryComplete(context, "give pickaxe 1 ", 15, results, out _));
            Assert.IsEmpty(
                results,
                "Stages beyond the definition produce no candidates but stay provider-answered"
            );
        }

        [Test]
        public void GetByArgumentNameAndTypeMismatchBehavior()
        {
            CommandShell shell = new(History());
            string byName = null;
            int byIndex = 0;
            Exception mismatch = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("report")
                        .Arg<int>("score", spec => spec.Required())
                        .Arg<string>("name", spec => spec.Required())
                        .Handler(
                            (context, arguments) =>
                            {
                                byName = arguments.Get<string>("name");
                                byIndex = arguments.Get<int>(0);
                                try
                                {
                                    arguments.Get<float>(0);
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

            Assert.IsTrue(shell.RunCommand("report 42 hero"));
            Assert.AreEqual("hero", byName);
            Assert.AreEqual(42, byIndex);
            Assert.IsNotNull(
                mismatch,
                "Reading an argument with the wrong type fails with a descriptive error"
            );
        }

        [Test]
        public void MissingHandlerFailsAtDefinitionTime()
        {
            CommandShell shell = new(History());
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                shell.AddCommand(CommandBuilder.Create("handlerless"), out _)
            );
            Assert.That(
                exception.Message,
                Does.Contain("Handler"),
                "The diagnostic should point at the missing handler"
            );
        }

        [Test]
        public void DuplicateArgumentNamesFailAtDefinitionTime()
        {
            CommandShell shell = new(History());
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                shell.AddCommand(
                    CommandBuilder
                        .Create("dupe")
                        .Arg<int>("count")
                        .Arg<int>("count")
                        .Handler((context, arguments) => { }),
                    out _
                )
            );
            Assert.That(exception.Message, Does.Contain("count"));
        }

        [Test]
        public void UnparseableArgumentTypeFailsAtDefinitionTime()
        {
            CommandShell shell = new(History());
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                shell.AddCommand(
                    CommandBuilder
                        .Create("custom")
                        .Arg<UnregisteredType>("value", spec => spec.Required())
                        .Handler((context, arguments) => { }),
                    out _
                )
            );
            Assert.That(exception.Message, Does.Contain("UnregisteredType"));

            Assert.IsTrue(
                CommandArg.RegisterParser<UnregisteredType>(
                    (string input, out UnregisteredType parsed) =>
                    {
                        if (input == "known")
                        {
                            parsed = new UnregisteredType();
                            return true;
                        }

                        parsed = default;
                        return false;
                    }
                ),
                "Sanity: the parser should register for the test type"
            );
            try
            {
                Assert.IsTrue(
                    shell.AddCommand(
                        CommandBuilder
                            .Create("custom")
                            .Arg<UnregisteredType>("value", spec => spec.Required())
                            .Handler((context, arguments) => { }),
                        out _
                    ),
                    "A registered parser unblocks the definition"
                );
            }
            finally
            {
                CommandArg.UnregisterParser<UnregisteredType>();
            }
        }

        [Test]
        public void DefaultMarksOptionalRegardlessOfOrder()
        {
            CommandShell shell = new(History());
            int runs = 0;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("order-omitted")
                        .Arg<int>("value", spec => spec.Required().Default(9))
                        .Handler((context, arguments) => ++runs),
                    out _
                ),
                "Required().Default() ends optional: the last fluent call wins"
            );

            Assert.IsTrue(shell.RunCommand("order-omitted"));
            Assert.AreEqual(1, runs, "An explicit Default makes the argument optional");
            Assert.IsNull(ConsumeError(shell));
        }

        [Test]
        public void DefaultsMustSatisfyTheirOwnValidation()
        {
            CommandShell shell = new(History());
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                shell.AddCommand(
                    CommandBuilder
                        .Create("bounded-default")
                        .Arg<int>("count", spec => spec.Default(0).Range(1, 100))
                        .Handler((context, arguments) => { }),
                    out _
                )
            );
            Assert.That(
                exception.Message,
                Does.Contain("count"),
                "The diagnostic names the argument whose default violates validation"
            );

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("bounded-default")
                        .Arg<int>("count", spec => spec.Default(1).Range(1, 100))
                        .Handler((context, arguments) => { }),
                    out _
                ),
                "A default inside the range registers"
            );
        }

        [Test]
        public void DisposedHandleDoesNotRemoveALegacyReplacement()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder.Create("replaced").Handler((context, arguments) => { }),
                    out CommandRegistrationHandle handle
                )
            );

            shell.ClearCustomCommands();
            Assert.IsTrue(
                shell.AddCommand("replaced", arguments => { }),
                "Sanity: a legacy command registers under the same name"
            );

            handle.Dispose();
            Assert.IsTrue(
                shell.Commands.ContainsKey("replaced"),
                "A stale handle must not remove the legacy replacement"
            );
        }

        [Test]
        public void AddToHistoryFalseSkipsHistoryForBuilderCommands()
        {
            CommandHistory history = History();
            CommandShell shell = new(history);
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("quiet")
                        .AddToHistory(false)
                        .Arg<int>("value", spec => spec.Required())
                        .Handler((context, arguments) => { }),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("quiet 5"));
            Assert.IsEmpty(
                history.GetHistory(true, true),
                "AddToHistory(false) must keep builder invocations out of the history"
            );
        }

        [Test]
        public void NestedBuilderCommandsDoNotCorruptParsedArguments()
        {
            CommandShell shell = new(History());
            string innerValue = null;
            string outerValue = null;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inner")
                        .Arg<string>("value", spec => spec.Required())
                        .Handler((context, arguments) => innerValue = arguments.Get<string>(0)),
                    out _
                )
            );
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("outer")
                        .Arg<string>("value", spec => spec.Required())
                        .Handler(
                            (context, arguments) =>
                            {
                                outerValue = arguments.Get<string>(0);
                                Assert.IsTrue(
                                    shell.RunCommand("inner nested"),
                                    "Nested dispatch succeeds from inside a builder handler"
                                );
                                outerValue = arguments.Get<string>(0);
                            }
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("outer alpha"));
            Assert.AreEqual("alpha", outerValue, "The outer arguments survive the nested dispatch");
            Assert.AreEqual("nested", innerValue, "The nested command ran with its own argument");
        }

        [Test]
        public void DisposedHandleRemovesExactlyItsOwnRegistration()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder.Create("borrowed").Handler((context, arguments) => { }),
                    out CommandRegistrationHandle handle
                ),
                "Registration should return a handle"
            );
            Assert.IsNotNull(handle);
            Assert.IsTrue(shell.Commands.ContainsKey("borrowed"));

            handle.Dispose();
            Assert.IsFalse(
                shell.Commands.ContainsKey("borrowed"),
                "Disposal removes the registration from discovery"
            );
            Assert.IsFalse(
                shell.RunCommand("borrowed"),
                "A disposed registration no longer executes"
            );

            // A later replacement with the same name must survive the stale handle.
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder.Create("borrowed").Handler((context, arguments) => { }),
                    out CommandRegistrationHandle replacement
                )
            );
            handle.Dispose();
            Assert.IsTrue(
                shell.Commands.ContainsKey("borrowed"),
                "Disposing a stale handle must not remove a later replacement"
            );
            replacement.Dispose();
            Assert.IsFalse(shell.Commands.ContainsKey("borrowed"));

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder.Create("borrowed").Handler((context, arguments) => { }),
                    out CommandRegistrationHandle reRegistered
                ),
                "Sanity: the name is free again after disposal"
            );
            reRegistered.Dispose();
            Assert.IsFalse(shell.Commands.ContainsKey("borrowed"));
        }

        [Test]
        public void HandleDisposeIsIdempotent()
        {
            CommandShell shell = new(History());
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder.Create("idempotent").Handler((context, arguments) => { }),
                    out CommandRegistrationHandle handle
                )
            );

            handle.Dispose();
            handle.Dispose();
            Assert.IsFalse(
                shell.Commands.ContainsKey("idempotent"),
                "A second dispose is a no-op, not a re-removal or a throw"
            );
        }

        [Test]
        public void BuilderContextsGateEligibility()
        {
            CommandShell shell = new(History());
            int invocations = 0;
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("player-only")
                        .Contexts(CommandExecutionContexts.Player)
                        .Handler((context, arguments) => ++invocations),
                    out _
                )
            );

            /*
                The ambient provider substitutes for the Unity environment, so
                eligibility is exercised for both sides of the gate.
             */
            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.Player);
            Assert.IsTrue(shell.RunCommand("player-only"));
            Assert.AreEqual(1, invocations);
            Assert.IsNull(ConsumeError(shell));

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.EditorEditMode);
            Assert.IsFalse(shell.RunCommand("player-only"));
            Assert.AreEqual(1, invocations, "The handler must not run when ineligible");
            Assert.IsNotNull(ConsumeError(shell));
        }

        [UnityTest]
        public IEnumerator LifecycleComponentRegistersOnEnableDisposesOnDisable()
        {
            yield return TerminalTests.SpawnTerminal(resetStateOnInit: true);

            GameObject host = new(nameof(BuilderLifecycleComponent));
            BuilderLifecycleComponent component = host.AddComponent<BuilderLifecycleComponent>();
            try
            {
                Assert.IsTrue(
                    Terminal.Shell.Commands.ContainsKey("lifecycle-hit"),
                    "OnEnable should register the builder command"
                );

                component.enabled = false;
                Assert.IsFalse(
                    Terminal.Shell.Commands.ContainsKey("lifecycle-hit"),
                    "OnDisable should dispose the registration"
                );

                component.enabled = true;
                Assert.IsTrue(
                    Terminal.Shell.Commands.ContainsKey("lifecycle-hit"),
                    "Re-enabling registers the command again"
                );
                Assert.IsTrue(
                    Terminal.Shell.RunCommand("lifecycle-hit 7"),
                    "The re-registered command executes"
                );
                Assert.AreEqual(1, component.Invocations);
            }
            finally
            {
                UnityEngine.Object.Destroy(host);
            }
        }

        private enum WeaponType
        {
            Sword,
            Bow,
            Staff,
        }
    }

    internal readonly struct UnregisteredType { }
}
