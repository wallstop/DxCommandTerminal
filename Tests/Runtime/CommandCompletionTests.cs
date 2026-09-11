namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;

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
            Assert.IsNull(withoutOverride.Replacement);

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
