namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;
    using UI;

    /*
        Contract tests for builder subcommands (PLAN T09 phase 2): one
        registered command routes its first argument to nested definitions,
        typed arguments parse and validate per subcommand, completion stages
        follow the route, and misconfiguration throws at definition time.
     */
    public sealed class CommandBuilderSubcommandTests
    {
        private Func<CommandExecutionContext> _previousAmbientProvider;

        private static string ConsumeError(CommandShell shell)
        {
            return shell.TryConsumeErrorMessage(out string error) ? error : null;
        }

        private static string ConsumeAllErrors(CommandShell shell)
        {
            string joined = null;
            while (shell.TryConsumeErrorMessage(out string error))
            {
                joined = joined == null ? error : $"{joined}\n{error}";
            }

            return joined;
        }

        private static string[] ResultsToTexts(List<CommandCompletion> results)
        {
            string[] texts = new string[results.Count];
            for (int i = 0; i < texts.Length; ++i)
            {
                texts[i] = results[i].InsertionText;
            }

            return texts;
        }

        [SetUp]
        public void SetUp()
        {
            CommandArg.UnregisterParser<UnregisteredType>();
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
        public void RoutesFirstArgumentToMatchingSubcommand()
        {
            CommandShell shell = new(new CommandHistory(16));
            string addedItem = null;
            int addedCount = 0;
            string removedItem = null;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory", "Manage the inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<string>("item", spec => spec.Required())
                                    .Arg<int>("count", spec => spec.Default(1).Range(1, 99))
                                    .Handler(
                                        (_, arguments) =>
                                        {
                                            addedItem = arguments.Get<string>(0);
                                            addedCount = arguments.Get<int>(1);
                                        }
                                    )
                        )
                        .Subcommand(
                            "remove",
                            remove =>
                                remove
                                    .Arg<string>("item", spec => spec.Required())
                                    .Handler(
                                        (_, arguments) => removedItem = arguments.Get<string>(0)
                                    )
                        ),
                    out _
                ),
                "The router should register cleanly"
            );

            Assert.IsTrue(shell.RunCommand("inventory add pickaxe 3"));
            Assert.AreEqual(
                "pickaxe",
                addedItem,
                "The subcommand handler should receive its arguments"
            );
            Assert.AreEqual(3, addedCount, "Input should override the declared default");
            Assert.IsNull(removedItem, "Only the routed subcommand runs");
            Assert.IsNull(ConsumeAllErrors(shell), "A valid route queues no error");

            Assert.IsTrue(shell.RunCommand("inventory remove sword"));
            Assert.AreEqual(
                "sword",
                removedItem,
                "The other subcommand routes through the same command"
            );
            Assert.IsNull(ConsumeAllErrors(shell), "The second route queues no error");
        }

        [Test]
        public void RoutesSubcommandNamesCaseInsensitively()
        {
            CommandShell shell = new(new CommandHistory(16));
            int invocations = 0;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("add", add => add.Handler((_, _) => ++invocations)),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inventory ADD"));
            Assert.AreEqual(1, invocations, "Routing matches command-name case rules");
            Assert.IsNull(ConsumeAllErrors(shell));

            Assert.IsTrue(shell.RunCommand("inventory \"add\""));
            Assert.AreEqual(
                2,
                invocations,
                "Quoted subcommand tokens route on their stripped contents"
            );
            Assert.IsNull(ConsumeAllErrors(shell));
        }

        [Test]
        public void UnknownSubcommandListsAvailableAndSkipsHandlers()
        {
            CommandShell shell = new(new CommandHistory(16));
            int invocations = 0;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<string>("item", spec => spec.Required())
                                    .Handler((_, _) => ++invocations)
                        )
                        .Subcommand("list", list => list.Handler((_, _) => ++invocations)),
                    out _
                )
            );

            Assert.IsTrue(
                shell.RunCommand("inventory forge pickaxe"),
                "The router handles the invocation; the mistake becomes a queued error"
            );
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "An unknown subcommand queues an error");
            Assert.That(error, Does.Contain("unknown subcommand 'forge'"));
            Assert.That(
                error,
                Does.Contain("add <item:string>"),
                "The error lists what can follow the command"
            );
            Assert.That(error, Does.Contain("list"));
            Assert.AreEqual(0, invocations, "No handler runs for an unknown subcommand");
        }

        [Test]
        public void BareInvocationErrorsListingSubcommands()
        {
            CommandShell shell = new(new CommandHistory(16));
            int invocations = 0;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("add", add => add.Handler((_, _) => ++invocations)),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inventory"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error, "A bare invocation without a fallback queues an error");
            Assert.That(error, Does.Contain("'inventory': expected a subcommand"));
            Assert.That(error, Does.Contain("add"));
            Assert.AreEqual(0, invocations);
        }

        [Test]
        public void BareInvocationRunsParentFallback()
        {
            CommandShell shell = new(new CommandHistory(16));
            int fallbackRuns = 0;
            int rawCount = -1;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Handler(
                            (_, arguments) =>
                            {
                                ++fallbackRuns;
                                rawCount = arguments.Count;
                            }
                        )
                        .Subcommand("add", add => add.Handler((_, _) => { })),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inventory"));
            Assert.AreEqual(
                1,
                fallbackRuns,
                "The parent handler acts as the bare-invocation fallback"
            );
            Assert.AreEqual(0, rawCount, "The fallback receives no arguments");
            Assert.IsNull(ConsumeAllErrors(shell));
        }

        [Test]
        public void MissingSubcommandArgumentsReportComposedUsage()
        {
            CommandShell shell = new(new CommandHistory(16));
            int invocations = 0;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<string>("item", spec => spec.Required())
                                    .Handler((_, _) => ++invocations)
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inventory add"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(
                error,
                Does.Contain("'inventory add': requires at least 1 argument"),
                "The bounds error names the routed subcommand"
            );
            Assert.That(
                error,
                Does.Contain("Usage: inventory add <item:string>"),
                "The derived usage composes parent and subcommand"
            );
            Assert.AreEqual(0, invocations);
        }

        [Test]
        public void TooManySubcommandArgumentsReportUsage()
        {
            CommandShell shell = new(new CommandHistory(16));
            int invocations = 0;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<string>("item", spec => spec.Required())
                                    .Handler((_, _) => ++invocations)
                        )
                        .Subcommand(
                            "stash",
                            stash =>
                                stash
                                    .Arg<string>("a", spec => spec.Required())
                                    .Arg<string>("b", spec => spec.Required())
                                    .Arg<string>("c", spec => spec.Required())
                                    .Handler((_, _) => { })
                        ),
                    out _
                )
            );

            /*
                The shell-level bound is open for routers, so even an over-fill
                of the widest sibling gets the precise composed message.
             */
            Assert.IsTrue(shell.RunCommand("inventory add pickaxe spare"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("'inventory add': expects at most 1 argument"));
            Assert.That(error, Does.Contain("Usage: inventory add <item:string>"));
            Assert.AreEqual(0, invocations);

            Assert.IsTrue(shell.RunCommand("inventory add pickaxe spare third fourth"));
            error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(
                error,
                Does.Contain("'inventory add': expects at most 1 argument"),
                "Over-filling the widest sibling's span still names the routed leaf"
            );
            Assert.AreEqual(0, invocations);
        }

        [Test]
        public void SubcommandArgumentMistakesReportComposedName()
        {
            CommandShell shell = new(new CommandHistory(16));
            int invocations = 0;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<int>("count", spec => spec.Required().Range(1, 9))
                                    .Handler((_, _) => ++invocations)
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inventory add abc"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(
                error,
                Does.Contain("'inventory add':"),
                "Parse errors name the composed command"
            );
            Assert.That(error, Does.Contain("for argument 'count'"));
            Assert.AreEqual(0, invocations);

            Assert.IsTrue(shell.RunCommand("inventory add 42"));
            error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(error, Does.Contain("'inventory add':"));
            Assert.That(error, Does.Contain("out of range"));
            Assert.AreEqual(0, invocations);
        }

        [Test]
        public void NestedSubcommandsRouteRecursively()
        {
            CommandShell shell = new(new CommandHistory(16));
            string deepItem = null;
            int groupFallbackRuns = 0;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inv")
                        .Subcommand(
                            "group",
                            group =>
                                group
                                    .Handler((_, _) => ++groupFallbackRuns)
                                    .Subcommand(
                                        "add",
                                        add =>
                                            add.Arg<string>("item", spec => spec.Required())
                                                .Handler(
                                                    (_, arguments) =>
                                                        deepItem = arguments.Get<string>(0)
                                                )
                                    )
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inv group add pickaxe"));
            Assert.AreEqual("pickaxe", deepItem, "Two routing levels reach the deep handler");
            Assert.IsNull(ConsumeAllErrors(shell));

            Assert.IsTrue(shell.RunCommand("inv group"));
            Assert.AreEqual(
                1,
                groupFallbackRuns,
                "A nested router's own handler is its bare-invocation fallback"
            );
            Assert.IsNull(ConsumeAllErrors(shell));
        }

        [Test]
        public void NestedUnknownSubcommandNamesTheFullPath()
        {
            CommandShell shell = new(new CommandHistory(16));

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inv")
                        .Subcommand(
                            "group",
                            group => group.Subcommand("add", add => add.Handler((_, _) => { }))
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inv group forge"));
            string error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(
                error,
                Does.Contain("'inv group': unknown subcommand 'forge'"),
                "Nested diagnostics compose every routing level"
            );

            Assert.IsTrue(shell.RunCommand("inv group"));
            error = ConsumeError(shell);
            Assert.IsNotNull(error);
            Assert.That(
                error,
                Does.Contain("'inv group': expected a subcommand"),
                "A nested router without a fallback errors on a bare invocation"
            );
            Assert.That(error, Does.Contain("add"));
        }

        [Test]
        public void SubcommandRawViewExcludesRouterTokens()
        {
            CommandShell shell = new(new CommandHistory(16));
            int argumentsCount = -1;
            int rawCount = -1;
            string rawFirst = null;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<string>("item", spec => spec.Required())
                                    .Arg<string>("extra", spec => spec.Default("none"))
                                    .Handler(
                                        (_, arguments) =>
                                        {
                                            argumentsCount = arguments.Count;
                                            rawCount = arguments.Raw.Count;
                                            rawFirst = arguments.Raw[0].contents;
                                        }
                                    )
                        ),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inventory add pickaxe"));
            Assert.AreEqual(
                2,
                argumentsCount,
                "The typed view covers the declared arguments, defaults included"
            );
            Assert.AreEqual(
                1,
                rawCount,
                "The raw view covers only what was typed after the subcommand"
            );
            Assert.AreEqual("pickaxe", rawFirst, "Raw index 0 is the subcommand's first argument");
        }

        [Test]
        public void CompletionStageZeroOffersSubcommandNames()
        {
            CommandShell shell = new(new CommandHistory(16));

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory", "Manage the inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Help("Add an item")
                                    .Arg<string>("item", spec => spec.Required())
                                    .Handler((_, _) => { })
                        )
                        .Subcommand(
                            "remove",
                            remove =>
                                remove
                                    .Arg<string>("item", spec => spec.Required())
                                    .Handler((_, _) => { })
                        ),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "inventory ",
                    "inventory ".Length,
                    results,
                    out _
                ),
                "A router command owns its own completion"
            );
            Assert.AreEqual(2, results.Count, "Blank input offers every subcommand");
            Assert.AreEqual("add", results[0].InsertionText, "Declaration order is preserved");
            Assert.AreEqual("remove", results[1].InsertionText);
            Assert.AreEqual(
                "Add an item",
                results[0].Description,
                "Subcommand help flows into completion descriptions"
            );
            Assert.IsEmpty(
                results[1].Description,
                "Subcommands without help complete with an empty description"
            );

            results.Clear();
            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "inventory re",
                    "inventory re".Length,
                    results,
                    out _
                )
            );
            CollectionAssert.AreEqual(
                new[] { "remove" },
                ResultsToTexts(results),
                "Prefix filtering keeps only matching subcommand names"
            );
        }

        [Test]
        public void CompletionRoutesToSubcommandChoices()
        {
            CommandShell shell = new(new CommandHistory(16));

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "give",
                            give =>
                                give.Arg<string>(
                                        "item",
                                        spec => spec.Required().Choices("pickaxe", "sword")
                                    )
                                    .Handler((_, _) => { })
                        )
                        .Subcommand(
                            "drop",
                            drop =>
                                drop.Arg<int>("slot", spec => spec.Required().Range(0, 3))
                                    .Handler((_, _) => { })
                        ),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "inventory give ",
                    "inventory give ".Length,
                    results,
                    out _
                )
            );
            CollectionAssert.AreEqual(
                new[] { "pickaxe", "sword" },
                ResultsToTexts(results),
                "The routed subcommand's choices complete its first argument"
            );

            results.Clear();
            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "inventory give pick",
                    "inventory give pick".Length,
                    results,
                    out _
                )
            );
            CollectionAssert.AreEqual(
                new[] { "pickaxe" },
                ResultsToTexts(results),
                "Prefix filtering applies within the routed subcommand"
            );
        }

        [Test]
        public void CompletionRoutesNestedChoices()
        {
            CommandShell shell = new(new CommandHistory(16));

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inv")
                        .Subcommand(
                            "group",
                            group =>
                                group.Subcommand(
                                    "set",
                                    set =>
                                        set.Arg<string>(
                                                "mode",
                                                spec => spec.Required().Choices("easy", "hard")
                                            )
                                            .Handler((_, _) => { })
                                )
                        ),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "inv group set ",
                    "inv group set ".Length,
                    results,
                    out _
                )
            );
            CollectionAssert.AreEqual(
                new[] { "easy", "hard" },
                ResultsToTexts(results),
                "Completion follows both routing levels"
            );
        }

        [Test]
        public void DynamicChoicesReceiveSubcommandScopedContext()
        {
            CommandShell shell = new(new CommandHistory(16));
            int observedStage = -1;
            string observedFirst = null;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("summon")
                        .Subcommand(
                            "pair",
                            pair =>
                                pair.Arg<string>(
                                        "creature",
                                        spec => spec.Required().Choices("wolf", "dragon")
                                    )
                                    .Arg<string>(
                                        "title",
                                        spec =>
                                            spec.Required()
                                                .Choices(context =>
                                                {
                                                    observedStage = context.ActiveArgumentIndex;
                                                    observedFirst = context
                                                        .PrecedingArguments
                                                        .IsEmpty
                                                        ? null
                                                        : context.PrecedingArguments[0].contents;
                                                    return new[] { $"{observedFirst}-lord" };
                                                })
                                    )
                                    .Handler((_, _) => { })
                        ),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "summon pair wolf ",
                    "summon pair wolf ".Length,
                    results,
                    out _
                )
            );
            CollectionAssert.AreEqual(
                new[] { "wolf-lord" },
                ResultsToTexts(results),
                "The dynamic provider builds candidates from the subcommand's own preceding argument"
            );
            Assert.AreEqual(
                1,
                observedStage,
                "The dynamic provider sees the subcommand-relative stage"
            );
            Assert.AreEqual(
                "wolf",
                observedFirst,
                "The dynamic provider sees the subcommand's preceding arguments, not router tokens"
            );
        }

        [Test]
        public void CompletionOfUnknownSubcommandProducesNoResults()
        {
            CommandShell shell = new(new CommandHistory(16));

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Arg<string>("item", spec => spec.Required().Choices("pickaxe"))
                                    .Handler((_, _) => { })
                        ),
                    out _
                )
            );

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    "inventory addx ",
                    "inventory addx ".Length,
                    results,
                    out _
                ),
                "The router keeps ownership so history suggestions cannot overwrite input"
            );
            Assert.IsEmpty(results, "An unknown subcommand completes to nothing");
        }

        /*
            Definition-time validation: every misconfiguration throws before
            registration, mirroring CommandContextExecutionTests.DefinitionValidation.
         */
        [Test]
        public void DefinitionTimeValidation()
        {
            CommandConfigurationException duplicate = Assert.Throws<CommandConfigurationException>(
                () =>
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("add", _ => { })
                        .Subcommand("ADD", _ => { })
            );
            Assert.That(
                duplicate.Message,
                Does.Contain("duplicate subcommand name 'ADD'"),
                "Duplicate detection follows command-name case rules"
            );

            CommandConfigurationException parentArguments =
                Assert.Throws<CommandConfigurationException>(() =>
                    new CommandShell(new CommandHistory(16)).AddCommand(
                        CommandBuilder
                            .Create("inventory")
                            .Arg<int>("mode", spec => spec.Required())
                            .Subcommand("add", add => add.Handler((_, _) => { })),
                        out _
                    )
                );
            Assert.That(
                parentArguments.Message,
                Does.Contain("cannot declare its own arguments"),
                "Router commands route; they take no own arguments"
            );

            CommandConfigurationException childContexts =
                Assert.Throws<CommandConfigurationException>(() =>
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand(
                            "add",
                            add =>
                                add.Contexts(CommandExecutionContexts.EditorEditMode)
                                    .Handler((_, _) => { })
                        )
                );
            Assert.That(
                childContexts.Message,
                Does.Contain("do not set Contexts on a subcommand"),
                "The parent's contexts govern every subcommand"
            );

            CommandConfigurationException childHistory =
                Assert.Throws<CommandConfigurationException>(() =>
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("add", add => add.AddToHistory(false).Handler((_, _) => { }))
                );
            Assert.That(
                childHistory.Message,
                Does.Contain("do not set AddToHistory on a subcommand")
            );

            CommandConfigurationException missingHandler =
                Assert.Throws<CommandConfigurationException>(() =>
                    new CommandShell(new CommandHistory(16)).AddCommand(
                        CommandBuilder.Create("inventory").Subcommand("add", _ => { }),
                        out _
                    )
                );
            Assert.That(
                missingHandler.Message,
                Does.Contain("Command 'inventory add':"),
                "The diagnostic names the composed path"
            );
            Assert.That(missingHandler.Message, Does.Contain("set a handler"));

            CommandConfigurationException colliding = Assert.Throws<CommandConfigurationException>(
                () =>
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("ad d", _ => { })
                        .Subcommand("ADD", _ => { })
            );
            Assert.That(
                colliding.Message,
                Does.Contain("duplicate subcommand name 'ADD'"),
                "Normalization happens before duplicate detection"
            );

            CommandConfigurationException emptyName = Assert.Throws<CommandConfigurationException>(
                () =>
                    CommandBuilder.Create("inventory").Subcommand("   ", _ => { })
            );
            Assert.That(
                emptyName.Message,
                Does.Contain("subcommand names must not be empty"),
                "Names that normalize to nothing are rejected"
            );
        }

        [Test]
        public void RouterRegistersDerivedHintAndBounds()
        {
            CommandShell shell = new(new CommandHistory(16));

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("add", add => add.Handler((_, _) => { }))
                        .Subcommand(
                            "stash",
                            stash =>
                                stash
                                    .Arg<string>("a", spec => spec.Required())
                                    .Arg<string>("b", spec => spec.Required())
                                    .Handler((_, _) => { })
                        ),
                    out _
                )
            );

            Assert.IsTrue(
                shell.Commands.TryGetValue("inventory", out CommandInfo info),
                "The router registers under the parent name only"
            );
            Assert.AreEqual(
                "inventory <subcommand>",
                info.hint,
                "The router hint derives from the command name"
            );
            Assert.AreEqual(
                0,
                info.minArgCount,
                "Bare invocations must reach the router, so the minimum is zero"
            );
            Assert.IsNull(
                info.maxArgCount,
                "The router owns bounds after routing, so the shell leaves the maximum open"
            );
        }

        [Test]
        public void HandleDisposeRemovesRouterCommand()
        {
            CommandShell shell = new(new CommandHistory(16));

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("add", add => add.Handler((_, _) => { })),
                    out CommandRegistrationHandle handle
                )
            );

            handle.Dispose();

            Assert.IsFalse(
                shell.Commands.ContainsKey("inventory"),
                "Disposing the handle removes the routed command"
            );
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("remove", remove => remove.Handler((_, _) => { })),
                    out _
                ),
                "A replacement router registers under the same name"
            );
        }

        [Test]
        public void SubcommandNameSpacesStripAtConfiguration()
        {
            CommandShell shell = new(new CommandHistory(16));
            int invocations = 0;

            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create("inventory")
                        .Subcommand("ad d", add => add.Handler((_, _) => ++invocations)),
                    out _
                )
            );

            Assert.IsTrue(shell.RunCommand("inventory add"));
            Assert.AreEqual(1, invocations, "Subcommand names strip spaces like command names");
        }
    }
}
