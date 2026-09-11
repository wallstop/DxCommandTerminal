namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine.TestTools;

    public sealed class CommandContextExecutionTests
    {
        private Func<CommandExecutionContext> _previousAmbientProvider;

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

        private static IEnumerator SpawnTerminal()
        {
            return TerminalTests.SpawnTerminal(resetStateOnInit: true);
        }

        private static string ConsumeError()
        {
            return Terminal.Shell.TryConsumeErrorMessage(out string error) ? error : null;
        }

        [UnityTest]
        public IEnumerator GameplayDefaultRunsInEditorPlayModeAndRejectsEditMode()
        {
            yield return SpawnTerminal();

            CommandExecutionContexts observed = CommandExecutionContexts.None;
            int invocations = 0;
            CommandDefinition definition = new()
            {
                Name = "ctx-test",
                Handler = (context, arguments) =>
                {
                    observed = context.Environment;
                    ++invocations;
                },
            };
            Assert.IsTrue(
                Terminal.Shell.AddCommand(definition),
                "The definition should register cleanly"
            );
            Assert.AreEqual(
                CommandExecutionContextSets.Gameplay,
                definition.Contexts,
                "Definitions default to the gameplay contexts"
            );

            // Play Mode is the ambient environment for editor test runs; no
            // provider is installed, so Unity decides.
            Assert.IsTrue(
                Terminal.Shell.RunCommand("ctx-test alpha"),
                "A gameplay command should run in Editor Play Mode"
            );
            Assert.AreEqual(1, invocations);
            Assert.AreEqual(
                CommandExecutionContexts.EditorPlayMode,
                observed,
                "The handler should observe the resolved Editor Play Mode environment"
            );
            Assert.IsNull(ConsumeError(), "Running an eligible command queues no error");

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.EditorEditMode);

            Assert.IsFalse(
                Terminal.Shell.RunCommand("ctx-test beta"),
                "A gameplay command must not run in Edit Mode without explicit opt-in"
            );
            Assert.AreEqual(1, invocations, "The handler must not be invoked when ineligible");
            Assert.AreEqual(
                "ctx-test is not available in the current execution context",
                ConsumeError(),
                "Eligibility rejections queue a descriptive error"
            );
        }

        [UnityTest]
        public IEnumerator LegacyRegistrationsKeepUnrestrictedEligibility()
        {
            yield return SpawnTerminal();

            int invocations = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand("legacy-ctx-test", arguments => ++invocations, 0, -1)
            );

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.EditorEditMode);
            Assert.IsTrue(
                Terminal.Shell.RunCommand("legacy-ctx-test"),
                "Commands registered without context metadata keep running in every environment"
            );
            Assert.AreEqual(1, invocations);
            Assert.IsNull(ConsumeError());

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.Player);
            Assert.IsTrue(Terminal.Shell.RunCommand("legacy-ctx-test again"));
            Assert.AreEqual(2, invocations);
        }

        [UnityTest]
        public IEnumerator ExplicitContextsGateEligibility()
        {
            yield return SpawnTerminal();

            int allContextsRuns = 0;
            int playerOnlyRuns = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-all",
                        Contexts = CommandExecutionContextSets.All,
                        Handler = (context, arguments) => ++allContextsRuns,
                    }
                )
            );
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-player",
                        Contexts = CommandExecutionContexts.Player,
                        Handler = (context, arguments) => ++playerOnlyRuns,
                    }
                )
            );

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.EditorEditMode);
            Assert.IsTrue(
                Terminal.Shell.RunCommand("ctx-all"),
                "Explicit All opt-in includes Edit Mode"
            );
            Assert.AreEqual(1, allContextsRuns);
            Assert.IsFalse(
                Terminal.Shell.RunCommand("ctx-player"),
                "Player-only commands stay rejected in Edit Mode"
            );
            Assert.AreEqual(0, playerOnlyRuns);

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.EditorPlayMode);
            Assert.IsTrue(Terminal.Shell.RunCommand("ctx-all"));
            Assert.IsFalse(Terminal.Shell.RunCommand("ctx-player"));
            Assert.AreEqual(2, allContextsRuns);
            Assert.AreEqual(0, playerOnlyRuns);

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.Player);
            Assert.IsTrue(Terminal.Shell.RunCommand("ctx-player"));
            Assert.AreEqual(1, playerOnlyRuns);
        }

        [UnityTest]
        public IEnumerator ContextAwareHandlerReceivesContextAndArguments()
        {
            yield return SpawnTerminal();

            CommandExecutionContext observedContext = default;
            List<string> observedArguments = new();
            CommandDefinition definition = new()
            {
                Name = "ctx-args",
                MinArgCount = 0,
                MaxArgCount = -1,
                Handler = (context, arguments) =>
                {
                    observedContext = context;
                    observedArguments.Clear();
                    foreach (CommandArg argument in arguments)
                    {
                        observedArguments.Add(argument.contents);
                    }
                },
            };
            Assert.IsTrue(Terminal.Shell.AddCommand(definition));

            Assert.IsTrue(Terminal.Shell.RunCommand("ctx-args one \"two words\" three"));
            Assert.AreEqual(CommandExecutionContexts.EditorPlayMode, observedContext.Environment);
            Assert.IsNull(observedContext.UserContext, "No user context was supplied");
            CollectionAssert.AreEqual(
                new[] { "one", "two words", "three" },
                observedArguments,
                "The borrowed view exposes the parsed arguments in input order"
            );

            List<CommandArg> callerArguments = new() { new CommandArg("injected") };
            object userContext = new();
            Assert.IsTrue(
                Terminal.Shell.RunCommand(
                    new CommandExecutionContext(
                        CommandExecutionContexts.EditorPlayMode,
                        userContext
                    ),
                    "ctx-args",
                    callerArguments
                )
            );
            Assert.AreSame(
                userContext,
                observedContext.UserContext,
                "The caller-supplied context object reaches the handler"
            );
            CollectionAssert.AreEqual(new[] { "injected" }, observedArguments);
        }

        [UnityTest]
        public IEnumerator NestedCommandsDoNotCorruptBorrowedArguments()
        {
            yield return SpawnTerminal();

            List<string> outerAfterNested = new();
            List<string> innerSeen = new();
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "nested-outer",
                        Handler = (context, arguments) =>
                        {
                            foreach (CommandArg argument in arguments)
                            {
                                outerAfterNested.Add(argument.contents);
                            }

                            // A nested dispatch must use its own parse scope.
                            Terminal.Shell.RunCommand("nested-inner 1 2");

                            outerAfterNested.Clear();
                            foreach (CommandArg argument in arguments)
                            {
                                outerAfterNested.Add(argument.contents);
                            }
                        },
                    }
                )
            );
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "nested-inner",
                        Handler = (context, arguments) =>
                        {
                            foreach (CommandArg argument in arguments)
                            {
                                innerSeen.Add(argument.contents);
                            }
                        },
                    }
                )
            );

            Assert.IsTrue(Terminal.Shell.RunCommand("nested-outer a b"));
            CollectionAssert.AreEqual(
                new[] { "1", "2" },
                innerSeen,
                "The nested command should receive its own arguments"
            );
            CollectionAssert.AreEqual(
                new[] { "a", "b" },
                outerAfterNested,
                "The outer borrowed arguments must survive the nested dispatch"
            );
        }

        [UnityTest]
        public IEnumerator LegacyHandlerGetsFreshArrayPerInvocation()
        {
            yield return SpawnTerminal();

            List<CommandArg[]> observedArrays = new();
            CommandDefinition definition = new()
            {
                Name = "fresh-array",
                LegacyHandler = arguments => observedArrays.Add(arguments),
            };
            Assert.IsTrue(Terminal.Shell.AddCommand(definition));

            List<CommandArg> callerArguments = new() { new CommandArg("first") };
            CommandExecutionContext context = CommandExecutionContext.Current;
            Assert.IsTrue(Terminal.Shell.RunCommand(context, "fresh-array", callerArguments));
            Assert.IsTrue(Terminal.Shell.RunCommand(context, "fresh-array", callerArguments));

            Assert.AreEqual(2, observedArrays.Count);
            Assert.AreNotSame(
                observedArrays[0],
                observedArrays[1],
                "Legacy handlers must never share one array across invocations"
            );
            Assert.AreEqual(
                "first",
                observedArrays[0][0].contents,
                "The first invocation's array stays stable after later invocations"
            );
        }

        [UnityTest]
        public IEnumerator BorrowedViewToArrayProducesOwnedCopies()
        {
            yield return SpawnTerminal();

            List<CommandArg[]> retained = new();
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "copy-out",
                        Handler = (context, arguments) => retained.Add(arguments.ToArray()),
                    }
                )
            );

            List<CommandArg> callerArguments = new() { new CommandArg("one") };
            CommandExecutionContext context = CommandExecutionContext.Current;
            Assert.IsTrue(Terminal.Shell.RunCommand(context, "copy-out", callerArguments));

            callerArguments.Clear();
            callerArguments.Add(new CommandArg("two"));
            Assert.IsTrue(Terminal.Shell.RunCommand(context, "copy-out", callerArguments));

            Assert.AreEqual(2, retained.Count);
            CollectionAssert.AreEqual(
                new[] { "one" },
                retained[0].Select(argument => argument.contents).ToArray(),
                "The copy taken during the first invocation is unaffected by later buffers"
            );
        }

        [UnityTest]
        public IEnumerator HistoryDisabledDefinitionSkipsHistory()
        {
            yield return SpawnTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-silent",
                        AddToHistory = false,
                        Handler = (context, arguments) => { },
                    }
                )
            );
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-loud",
                        Handler = (context, arguments) => { },
                    }
                )
            );

            Assert.IsTrue(Terminal.Shell.RunCommand("ctx-silent with args"));
            Assert.IsTrue(Terminal.Shell.RunCommand("ctx-loud with args"));

            string[] history = Terminal.History.GetHistory(true, true).ToArray();
            CollectionAssert.AreEqual(
                new[] { "ctx-loud with args" },
                history,
                "Commands with AddToHistory = false leave no history entries"
            );
        }

        [UnityTest]
        public IEnumerator EligibilityRejectionRespectsHistoryPolicy()
        {
            yield return SpawnTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-history-reject",
                        Contexts = CommandExecutionContexts.Player,
                        Handler = (context, arguments) => { },
                    }
                )
            );

            // The command declares Player-only eligibility, so the eligible
            // ambient here is a player.
            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.Player);
            Assert.IsTrue(
                Terminal.Shell.RunCommand("ctx-history-reject"),
                "An eligible command runs normally"
            );

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.EditorEditMode);
            Assert.IsFalse(Terminal.Shell.RunCommand("ctx-history-reject again"));

            string[] successfulHistory = Terminal.History.GetHistory(true, true).ToArray();
            CollectionAssert.AreEqual(
                new[] { "ctx-history-reject" },
                successfulHistory,
                "The rejected invocation must not count as a successful history entry"
            );
        }

        [UnityTest]
        public IEnumerator DefinitionSnapshotIsIsolatedFromLaterEdits()
        {
            yield return SpawnTerminal();

            int invocations = 0;
            CommandDefinition definition = new()
            {
                Name = "ctx-snapshot",
                Handler = (context, arguments) => ++invocations,
            };
            Assert.IsTrue(Terminal.Shell.AddCommand(definition));

            // Mutating the definition after registration must not affect the
            // registration or register anything under the new name.
            definition.Name = "ctx-renamed";
            definition.Handler = null;
            definition.Contexts = CommandExecutionContexts.None;

            Assert.IsTrue(
                Terminal.Shell.RunCommand("ctx-snapshot"),
                "The registered command keeps its original definition"
            );
            Assert.AreEqual(1, invocations);
            Assert.IsFalse(
                Terminal.Shell.Commands.ContainsKey("ctx-renamed"),
                "Later definition edits must not register new commands"
            );
        }

        [UnityTest]
        public IEnumerator DefinitionValidation()
        {
            yield return SpawnTerminal();

            Assert.Throws<ArgumentNullException>(
                () => Terminal.Shell.AddCommand(null),
                "A null definition is rejected"
            );

            Assert.IsFalse(
                Terminal.Shell.AddCommand(new CommandDefinition { Name = "ctx-no-handler" }),
                "A definition without any handler must be rejected"
            );
            Assert.AreEqual(
                "Command ctx-no-handler must declare exactly one handler (Handler or LegacyHandler).",
                ConsumeErrorOrEmpty()
            );

            Assert.IsFalse(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-two-handlers",
                        Handler = (context, arguments) => { },
                        LegacyHandler = arguments => { },
                    }
                ),
                "A definition with two handlers must be rejected"
            );
            Assert.AreEqual(
                "Command ctx-two-handlers must declare exactly one handler (Handler or LegacyHandler).",
                ConsumeErrorOrEmpty()
            );

            Assert.IsFalse(
                Terminal.Shell.AddCommand(
                    new CommandDefinition { Name = "   ", Handler = (context, arguments) => { } }
                ),
                "A blank name must be rejected"
            );
            StringAssert.StartsWith(
                "Invalid Command Name",
                ConsumeErrorOrEmpty(),
                "Blank names queue the standard diagnostic"
            );

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-duplicate",
                        Handler = (context, arguments) => { },
                    }
                ),
                "The first registration wins"
            );
            Assert.IsFalse(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-duplicate",
                        Handler = (context, arguments) => { },
                    }
                ),
                "A duplicate name must be rejected like every other registration path"
            );
            Assert.AreEqual("Command ctx-duplicate is already defined.", ConsumeErrorOrEmpty());
        }

        private static string ConsumeErrorOrEmpty()
        {
            return Terminal.Shell != null && Terminal.Shell.TryConsumeErrorMessage(out string error)
                ? error
                : null;
        }
    }
}
