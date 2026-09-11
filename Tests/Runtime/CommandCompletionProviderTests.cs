namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class CommandCompletionProviderTests
    {
        private static readonly string[] Inventory = { "pickaxe", "torch", "torch pick" };

        private Func<CommandExecutionContext> _previousAmbientProvider;

        private static IEnumerator SpawnTerminal()
        {
            return TerminalTests.SpawnTerminal(resetStateOnInit: true);
        }

        private static CommandCompletionProvider InventoryProvider()
        {
            return (in CommandCompletionContext context, List<CommandCompletion> results) =>
            {
                foreach (string item in Inventory)
                {
                    if (item.StartsWith(context.Token, StringComparison.Ordinal))
                    {
                        results.Add(new CommandCompletion(item));
                    }
                }
            };
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

        [UnityTest]
        public IEnumerator StagedProviderChainsByArgumentStage()
        {
            yield return SpawnTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "pickup",
                        Handler = (context, arguments) => { },
                        CompletionProvider = CommandCompletionProviders.Staged(
                            InventoryProvider(),
                            (
                                in CommandCompletionContext context,
                                List<CommandCompletion> results
                            ) =>
                            {
                                CollectionAssert.AreEqual(
                                    new[] { "pickaxe" },
                                    context
                                        .PrecedingArguments.Select(argument => argument.contents)
                                        .ToArray(),
                                    "Stage 1 receives the completed first argument"
                                );
                                results.Add(new CommandCompletion("sharpened"));
                                results.Add(new CommandCompletion("broken"));
                            }
                        ),
                    }
                )
            );

            List<CommandCompletion> results = new();
            CommandExecutionContext context = CommandExecutionContext.Current;

            // Stage 0: completing the first argument.
            Assert.IsTrue(Terminal.Shell.TryComplete(context, "pickup ", 7, results, out _));
            CollectionAssert.AreEqual(
                Inventory,
                results.Select(completion => completion.InsertionText).ToArray(),
                "Stage 0 completes from the inventory"
            );

            // Stage 1: 'pickaxe' is a preceding argument, the caret opens a new one.
            Assert.IsTrue(
                Terminal.Shell.TryComplete(context, "pickup pickaxe ", 15, results, out _)
            );
            CollectionAssert.AreEqual(
                new[] { "sharpened", "broken" },
                results.Select(completion => completion.InsertionText).ToArray(),
                "Stage 1 completes the second argument"
            );

            /*
               Requests past the last stage produce no candidates but still
               count as provider-answered.
            */
            Assert.IsTrue(
                Terminal.Shell.TryComplete(context, "pickup pickaxe sharpened ", 25, results, out _)
            );
            Assert.IsEmpty(results, "Stages beyond the provider list produce nothing");
        }

        [UnityTest]
        public IEnumerator CompletionContextCarriesRequestDetails()
        {
            yield return SpawnTerminal();

            List<CommandCompletionContext> observed = new();
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-complete",
                        Handler = (context, arguments) => { },
                        CompletionProvider = (
                            in CommandCompletionContext context,
                            List<CommandCompletion> results
                        ) => observed.Add(context),
                    }
                )
            );

            CommandExecutionContext context = CommandExecutionContext.Current;
            List<CommandCompletion> results = new();

            /*
               Mid-token inside a quoted argument: the caret completes an
               unclosed quoted token, so the request is quoted.
            */
            Assert.IsTrue(
                Terminal.Shell.TryComplete(
                    context,
                    "ctx-complete \"pick axe\" \"to",
                    27,
                    results,
                    out CommandCompletionContext completionContext
                )
            );
            Assert.AreEqual(1, observed.Count);
            CommandCompletionContext recorded = observed[0];
            Assert.AreEqual("to", recorded.Token, "The token text runs to the caret");
            Assert.AreEqual(
                25,
                recorded.ReplacementStart,
                "The quoted token starts after its quote"
            );
            Assert.AreEqual(2, recorded.ReplacementLength, "The replacement covers the raw token");
            Assert.IsTrue(recorded.IsQuoted);
            Assert.AreEqual('"', recorded.QuoteCharacter);
            Assert.AreEqual(1, recorded.ActiveArgumentIndex, "The second argument is stage 1");
            CollectionAssert.AreEqual(
                new[] { "pick axe" },
                recorded.PrecedingArguments.Select(argument => argument.contents).ToArray()
            );
            Assert.AreEqual(
                '"',
                recorded.PrecedingArguments[0].startQuote,
                "Preceding arguments preserve their quoting"
            );
            Assert.AreSame(
                completionContext.Token,
                recorded.Token,
                "The returned context is the one the provider saw"
            );

            // New argument at a whitespace boundary.
            Assert.IsTrue(
                Terminal.Shell.TryComplete(
                    context,
                    "ctx-complete pick ",
                    18,
                    results,
                    out completionContext
                )
            );
            Assert.AreEqual(2, observed.Count);
            recorded = observed[1];
            Assert.IsEmpty(recorded.Token, "A boundary token has no text yet");
            Assert.AreEqual(18, recorded.ReplacementStart);
            Assert.AreEqual(0, recorded.ReplacementLength);
            Assert.IsFalse(recorded.IsQuoted);
            Assert.IsNull(recorded.QuoteCharacter);
            CollectionAssert.AreEqual(
                new[] { "pick" },
                recorded.PrecedingArguments.Select(argument => argument.contents).ToArray()
            );
        }

        [UnityTest]
        public IEnumerator UnquotedCompletionsStayRawAndProviderOrderIsStable()
        {
            yield return SpawnTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-plain",
                        Handler = (context, arguments) => { },
                        CompletionProvider = (
                            in CommandCompletionContext context,
                            List<CommandCompletion> results
                        ) =>
                        {
                            results.Add(new CommandCompletion("alpha"));
                            results.Add(new CommandCompletion("beta", description: "second"));
                            results.Add(new CommandCompletion("alpha", description: "duplicate"));
                        },
                    }
                )
            );

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                Terminal.Shell.TryComplete(
                    CommandExecutionContext.Current,
                    "ctx-plain ",
                    10,
                    results,
                    out _
                )
            );
            Assert.AreEqual(2, results.Count, "Duplicate insertion texts are deduplicated");
            Assert.AreEqual("alpha", results[0].InsertionText, "First occurrence wins");
            Assert.IsNull(results[0].Description);
            Assert.AreEqual("beta", results[1].InsertionText);
            Assert.AreEqual("second", results[1].Description);
            Assert.AreEqual("beta", results[1].EffectiveDisplayLabel);
        }

        [UnityTest]
        public IEnumerator EmptyInsertionTextsAreDropped()
        {
            yield return SpawnTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-empty",
                        Handler = (context, arguments) => { },
                        CompletionProvider = (
                            in CommandCompletionContext context,
                            List<CommandCompletion> results
                        ) =>
                        {
                            results.Add(new CommandCompletion(string.Empty));
                            results.Add(new CommandCompletion("kept"));
                        },
                    }
                )
            );

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                Terminal.Shell.TryComplete(
                    CommandExecutionContext.Current,
                    "ctx-empty ",
                    10,
                    results,
                    out _
                )
            );
            CollectionAssert.AreEqual(
                new[] { "kept" },
                results.Select(completion => completion.InsertionText).ToArray(),
                "Empty insertions are filtered out"
            );
        }

        [UnityTest]
        public IEnumerator ThrowingProviderIsContainedAndRecoverable()
        {
            yield return SpawnTerminal();

            int invocations = 0;
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-throwing",
                        Handler = (context, arguments) => { },
                        CompletionProvider = (
                            in CommandCompletionContext context,
                            List<CommandCompletion> results
                        ) =>
                        {
                            ++invocations;
                            results.Add(new CommandCompletion("before-failure"));
                            if (invocations == 1)
                            {
                                throw new InvalidOperationException("provider exploded");
                            }
                        },
                    }
                )
            );

            List<CommandCompletion> results = new();
            LogAssert.Expect(
                LogType.Error,
                "[DxCommandTerminal] Completion provider for 'ctx-throwing' failed: provider exploded"
            );
            Assert.IsTrue(
                Terminal.Shell.TryComplete(
                    CommandExecutionContext.Current,
                    "ctx-throwing ",
                    13,
                    results,
                    out _
                ),
                "A throwing provider still counts as provider-answered"
            );
            Assert.IsEmpty(results, "Partial results from a throwing provider are discarded");
            Assert.AreEqual(1, invocations);

            // Later requests are unaffected.
            Assert.IsTrue(
                Terminal.Shell.TryComplete(
                    CommandExecutionContext.Current,
                    "ctx-throwing ",
                    13,
                    results,
                    out _
                )
            );
            CollectionAssert.AreEqual(
                new[] { "before-failure" },
                results.Select(completion => completion.InsertionText).ToArray()
            );
            Assert.AreEqual(2, invocations);
        }

        [UnityTest]
        public IEnumerator CallerBufferIsReusableAcrossRequests()
        {
            yield return SpawnTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-reuse",
                        Handler = (context, arguments) => { },
                        CompletionProvider = (
                            in CommandCompletionContext context,
                            List<CommandCompletion> results
                        ) => results.Add(new CommandCompletion("candidate")),
                    }
                )
            );

            List<CommandCompletion> results = new();
            CommandExecutionContext context = CommandExecutionContext.Current;
            Assert.IsTrue(Terminal.Shell.TryComplete(context, "ctx-reuse ", 10, results, out _));
            Assert.AreEqual(1, results.Count);

            Assert.IsTrue(Terminal.Shell.TryComplete(context, "ctx-reuse ", 10, results, out _));
            Assert.AreEqual(
                1,
                results.Count,
                "Reusing the buffer must not accumulate stale results"
            );
        }

        [UnityTest]
        public IEnumerator UnknownOrProviderlessCommandsReturnFalse()
        {
            yield return SpawnTerminal();

            Assert.IsTrue(Terminal.Shell.AddCommand("ctx-plain-legacy", arguments => { }, 0, -1));

            List<CommandCompletion> results = new();
            CommandExecutionContext context = CommandExecutionContext.Current;
            Assert.IsFalse(
                Terminal.Shell.TryComplete(context, "ctx-missing ", 12, results, out _),
                "An unknown command has no provider to answer"
            );
            Assert.IsFalse(
                Terminal.Shell.TryComplete(context, "ctx-plain-legacy ", 17, results, out _),
                "Commands without a provider keep history-based completion"
            );
            Assert.IsFalse(
                Terminal.Shell.TryComplete(context, "ctx", 3, results, out _),
                "Completing the command name itself is not provider territory"
            );
            Assert.IsFalse(
                Terminal.Shell.TryComplete(context, string.Empty, 0, results, out _),
                "Empty input has nothing to complete"
            );
            Assert.IsFalse(
                Terminal.Shell.TryComplete(context, "ctx-missing x ", 14, results, out _),
                "An unknown command keeps returning false regardless of the argument stage"
            );
        }

        [UnityTest]
        public IEnumerator NullResultsBufferIsRejected()
        {
            yield return SpawnTerminal();

            Assert.Throws<ArgumentNullException>(
                () =>
                    Terminal.Shell.TryComplete(
                        CommandExecutionContext.Current,
                        "help ",
                        5,
                        null,
                        out _
                    ),
                "A null results buffer is a caller error"
            );
        }

        [UnityTest]
        public IEnumerator IneligibleCommandsStillComplete()
        {
            yield return SpawnTerminal();

            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    new CommandDefinition
                    {
                        Name = "ctx-ineligible",
                        Contexts = CommandExecutionContexts.Player,
                        Handler = (context, arguments) => { },
                        CompletionProvider = (
                            in CommandCompletionContext context,
                            List<CommandCompletion> results
                        ) => results.Add(new CommandCompletion("suggestion")),
                    }
                )
            );

            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.EditorEditMode);

            List<CommandCompletion> results = new();
            Assert.IsTrue(
                Terminal.Shell.TryComplete(
                    CommandExecutionContext.Current,
                    "ctx-ineligible ",
                    15,
                    results,
                    out _
                ),
                "Completion may surface candidates that execution later rejects"
            );
            CollectionAssert.AreEqual(
                new[] { "suggestion" },
                results.Select(completion => completion.InsertionText).ToArray()
            );

            Assert.IsFalse(
                Terminal.Shell.RunCommand("ctx-ineligible"),
                "Execution re-validates availability independently of completion"
            );
            StringAssert.Contains(
                "not available in the current execution context",
                Terminal.Shell.TryConsumeErrorMessage(out string error) ? error : string.Empty,
                "The stale choice is rejected at execution"
            );
        }

        [UnityTest]
        public IEnumerator IgnoredCommandsDoNotComplete()
        {
            yield return SpawnTerminal();

            Terminal.Shell.InitializeAutoRegisteredCommands(
                new[] { "help" },
                ignoreDefaultCommands: false
            );

            List<CommandCompletion> results = new();
            Assert.IsFalse(
                Terminal.Shell.TryComplete(
                    CommandExecutionContext.Current,
                    "help ",
                    5,
                    results,
                    out _
                ),
                "Ignored commands are not discoverable for completion"
            );
        }
    }
}
