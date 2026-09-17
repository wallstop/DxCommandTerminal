namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;

    /*
        Structured-failure contract for definition-time authoring errors
        (issue #68): every CommandConfigurationException carries a
        CommandConfigurationFailure kind plus the command, argument,
        subcommand, and type identity the failure concerns, so callers
        handle each circumstance without parsing the message.
     */
    public sealed class CommandConfigurationExceptionTests
    {
        private static CommandHistory History() => new(16);

        private static IEnumerable<TestCaseData> StructuredFailureCases()
        {
            TestCaseData Case(
                string description,
                Func<CommandShell, object> act,
                CommandConfigurationFailure failure,
                string commandName = null,
                string argumentName = null,
                string subcommandName = null,
                Type argumentType = null
            )
            {
                return new TestCaseData(
                    act,
                    failure,
                    commandName,
                    argumentName,
                    subcommandName,
                    argumentType
                ).SetName($"{failure} ({description})");
            }

            yield return Case(
                "blank command name",
                _ => CommandBuilder.Create("  "),
                CommandConfigurationFailure.EmptyName
            );
            yield return Case(
                "blank subcommand name",
                shell =>
                    shell.AddCommand(
                        CommandBuilder.Create("inventory").Subcommand("   ", _ => { }),
                        out _
                    ),
                CommandConfigurationFailure.EmptyName,
                commandName: "inventory",
                subcommandName: "   "
            );
            yield return Case(
                "blank argument name",
                shell => shell.AddCommand(CommandBuilder.Create("inventory").Arg<int>(" "), out _),
                CommandConfigurationFailure.EmptyName,
                commandName: "inventory",
                argumentName: " "
            );
            yield return Case(
                "duplicate argument name",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("heal")
                            .Arg<int>("amount")
                            .Arg<int>("AMOUNT")
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.DuplicateName,
                commandName: "heal",
                argumentName: "AMOUNT"
            );
            yield return Case(
                "duplicate subcommand name",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("inventory")
                            .Subcommand("add", _ => { })
                            .Subcommand("ADD", _ => { }),
                        out _
                    ),
                CommandConfigurationFailure.DuplicateName,
                commandName: "inventory",
                subcommandName: "ADD"
            );
            yield return Case(
                "missing handler",
                shell => shell.AddCommand(CommandBuilder.Create("handlerless"), out _),
                CommandConfigurationFailure.MissingHandler,
                commandName: "handlerless"
            );
            yield return Case(
                "missing handler on a subcommand names the composed path",
                shell =>
                    shell.AddCommand(
                        CommandBuilder.Create("inventory").Subcommand("add", _ => { }),
                        out _
                    ),
                CommandConfigurationFailure.MissingHandler,
                commandName: "inventory add"
            );
            yield return Case(
                "required argument after optional",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("give")
                            .Arg<string>("target", spec => spec.Default("self"))
                            .Arg<int>("count", spec => spec.Required())
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.ArgumentOrdering,
                commandName: "give",
                argumentName: "count"
            );
            yield return Case(
                "argument after the remaining argument",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("say")
                            .Remaining<int>("tokens")
                            .Arg<int>("after")
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.InvalidRemainingArgument,
                commandName: "say",
                argumentName: "after"
            );
            yield return Case(
                "second remaining argument",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("say")
                            .Remaining<int>("tokens")
                            .Remaining<int>("more")
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.InvalidRemainingArgument,
                commandName: "say",
                argumentName: "more"
            );
            yield return Case(
                "default on the remaining argument",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("say")
                            .Remaining<int>("tokens", spec => spec.Default(3))
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.InvalidRemainingArgument,
                commandName: "say",
                argumentName: "tokens"
            );
            yield return Case(
                "unparseable argument type",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("custom")
                            .Arg<UnparsedType>("value", spec => spec.Required())
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.UnparseableArgumentType,
                commandName: "custom",
                argumentName: "value",
                argumentType: typeof(UnparsedType)
            );
            yield return Case(
                "bool choices on a non-bool argument",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("flag")
                            .Arg<int>("count", spec => spec.BoolChoices())
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.UnsupportedArgumentFeature,
                commandName: "flag",
                argumentName: "count",
                argumentType: typeof(int)
            );
            yield return Case(
                "enum choices on a non-enum argument",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("mode")
                            .Arg<int>("level", spec => spec.EnumChoices())
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.UnsupportedArgumentFeature,
                commandName: "mode",
                argumentName: "level",
                argumentType: typeof(int)
            );
            yield return Case(
                "range on a non-comparable argument",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("custom")
                            .Arg<UnparsedType>(
                                "value",
                                spec => spec.Range(new UnparsedType(), new UnparsedType())
                            )
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.UnsupportedArgumentFeature,
                commandName: "custom",
                argumentName: "value",
                argumentType: typeof(UnparsedType)
            );
            yield return Case(
                "empty choices",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("pick")
                            .Arg<string>("item", spec => spec.Choices())
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.InvalidChoices,
                commandName: "pick",
                argumentName: "item"
            );
            yield return Case(
                "null choice element",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("pick")
                            .Arg<string>("item", spec => spec.Choices("axe", null))
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.InvalidChoices,
                commandName: "pick",
                argumentName: "item"
            );
            yield return Case(
                "inverted range",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("heal")
                            .Arg<int>("amount", spec => spec.Range(100, 1))
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.InvalidRange,
                commandName: "heal",
                argumentName: "amount"
            );
            yield return Case(
                "default fails its own validation",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("heal")
                            .Arg<int>("amount", spec => spec.Default(150).Range(1, 100))
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.InvalidDefault,
                commandName: "heal",
                argumentName: "amount"
            );
            yield return Case(
                "subcommand sets its own contexts",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("inventory")
                            .Subcommand(
                                "add",
                                add =>
                                    add.Contexts(CommandExecutionContexts.EditorEditMode)
                                        .Handler((_, _) => { })
                            ),
                        out _
                    ),
                CommandConfigurationFailure.InvalidSubcommandConfiguration,
                commandName: "inventory",
                subcommandName: "add"
            );
            yield return Case(
                "subcommand sets its own history policy",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("inventory")
                            .Subcommand(
                                "add",
                                add => add.AddToHistory(false).Handler((_, _) => { })
                            ),
                        out _
                    ),
                CommandConfigurationFailure.InvalidSubcommandConfiguration,
                commandName: "inventory",
                subcommandName: "add"
            );
            yield return Case(
                "routed parent declares its own arguments",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("inventory")
                            .Arg<int>("mode", spec => spec.Required())
                            .Subcommand("add", add => add.Handler((_, _) => { })),
                        out _
                    ),
                CommandConfigurationFailure.InvalidSubcommandConfiguration,
                commandName: "inventory"
            );
            yield return Case(
                "argument configure callback returns null",
                shell =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("odd")
                            .Arg<int>("count", _ => null)
                            .Handler((_, _) => { }),
                        out _
                    ),
                CommandConfigurationFailure.NullConfigurationResult,
                commandName: "odd",
                argumentName: "count"
            );
        }

        [Test]
        [TestCaseSource(nameof(StructuredFailureCases))]
        public void AuthoringFailuresCarryStructuredData(
            Func<CommandShell, object> act,
            CommandConfigurationFailure failure,
            string commandName,
            string argumentName,
            string subcommandName,
            Type argumentType
        )
        {
            CommandShell shell = new(History());
            CommandConfigurationException exception = Assert.Throws<CommandConfigurationException>(
                () =>
                    act(shell)
            );
            Assert.AreEqual(failure, exception.Failure, $"Failure kind for '{exception.Message}'");
            Assert.AreEqual(
                commandName,
                exception.CommandName,
                $"Command name for '{exception.Message}'"
            );
            Assert.AreEqual(
                argumentName,
                exception.ArgumentName,
                $"Argument name for '{exception.Message}'"
            );
            Assert.AreEqual(
                subcommandName,
                exception.SubcommandName,
                $"Subcommand name for '{exception.Message}'"
            );
            Assert.AreEqual(
                argumentType,
                exception.ArgumentType,
                $"Argument type for '{exception.Message}'"
            );
        }

        [Test]
        public void LegacyConstructorLeavesFailureUnclassified()
        {
            CommandConfigurationException unnamed = new("Something was misconfigured.");
            Assert.AreEqual(
                default(CommandConfigurationFailure),
                unnamed.Failure,
                "The message-only constructor predates the kinds"
            );
            Assert.IsNull(unnamed.CommandName);
            Assert.IsNull(unnamed.ArgumentName);
            Assert.IsNull(unnamed.SubcommandName);
            Assert.IsNull(unnamed.ArgumentType);

            CommandConfigurationException named = new("Bad", "heal");
            Assert.AreEqual(default(CommandConfigurationFailure), named.Failure);
            Assert.AreEqual("heal", named.CommandName);
        }

        [Test]
        public void SpecLevelFailuresPreserveTheOriginalException()
        {
            CommandShell shell = new(History());
            CommandConfigurationException exception = Assert.Throws<CommandConfigurationException>(
                () =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("heal")
                            .Arg<int>("amount", spec => spec.Range(100, 1)),
                        out _
                    )
            );
            Assert.AreEqual(
                CommandConfigurationFailure.InvalidRange,
                exception.Failure,
                "The re-throw keeps the failure kind"
            );
            Assert.IsInstanceOf<CommandConfigurationException>(
                exception.InnerException,
                "The original throw rides as the inner exception"
            );
            CommandConfigurationException inner = (CommandConfigurationException)
                exception.InnerException;
            Assert.AreEqual(
                CommandConfigurationFailure.InvalidRange,
                inner.Failure,
                "The inner exception keeps its kind"
            );
            Assert.IsNull(inner.CommandName, "The spec-level throw reports no command of its own");
            Assert.IsNull(
                inner.InnerException,
                "Spec-level throws originate at the spec; there is nothing deeper"
            );
        }

        [Test]
        public void SpecLevelFailuresInsideSubcommandsComposeThePath()
        {
            CommandShell shell = new(History());
            CommandConfigurationException exception = Assert.Throws<CommandConfigurationException>(
                () =>
                    shell.AddCommand(
                        CommandBuilder
                            .Create("inventory")
                            .Subcommand(
                                "add",
                                add => add.Arg<UnparsedType>("value", spec => spec.Required())
                            ),
                        out _
                    ),
                "The subcommand's configure callback throws during Subcommand"
            );
            Assert.AreEqual(
                CommandConfigurationFailure.UnparseableArgumentType,
                exception.Failure,
                "The failure kind survives the subcommand re-throw"
            );
            Assert.AreEqual(
                "inventory add",
                exception.CommandName,
                "The parent composes the routed path, matching Build-time diagnostics"
            );
            Assert.AreEqual("add", exception.SubcommandName);
            Assert.AreEqual("value", exception.ArgumentName);
            Assert.AreEqual(typeof(UnparsedType), exception.ArgumentType);
            Assert.IsNotNull(exception.InnerException);
        }

        private sealed class UnparsedType { }
    }
}
