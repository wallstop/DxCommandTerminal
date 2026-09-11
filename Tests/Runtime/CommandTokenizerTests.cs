namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;

    public sealed class CommandTokenizerTests
    {
        private static List<CommandArg> ParseWithTryEatArgument(string line)
        {
            List<CommandArg> arguments = new();
            string remaining = line;
            while (!string.IsNullOrWhiteSpace(remaining))
            {
                if (!CommandShell.TryEatArgument(ref remaining, out CommandArg argument))
                {
                    continue;
                }

                arguments.Add(argument);
            }

            return arguments;
        }

        private static List<CommandToken> Tokenize(string line)
        {
            List<CommandToken> tokens = new();
            CommandTokenizer.Tokenize(line, tokens);
            return tokens;
        }

        [TestCase("log")]
        [TestCase("log foo")]
        [TestCase("log  foo")]
        [TestCase("  log   foo  ")]
        [TestCase("log\tfoo")]
        [TestCase("log \"quoted arg\"")]
        [TestCase("log 'single quoted'")]
        [TestCase("log \"unterminated")]
        [TestCase("log 'unterminated with spaces")]
        [TestCase("log \"\"")]
        [TestCase("log ''")]
        [TestCase("log \"mixed' quotes")]
        [TestCase("log \"a\" 'b' c")]
        [TestCase("$variable")]
        [TestCase("log $variable trailing")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("log \"\" \"\" tail")]
        [TestCase("set var \"with space\"")]
        [TestCase("path/to/file,other")]
        [TestCase("log \"quote'inside\" 'quote\"inside'")]
        public void TokenizeMatchesTryEatArgument(string line)
        {
            List<CommandArg> expected = ParseWithTryEatArgument(line);
            List<CommandToken> actual = Tokenize(line);

            Assert.AreEqual(expected.Count, actual.Count, $"Token count mismatch for '{line}'");
            for (int i = 0; i < expected.Count; ++i)
            {
                CommandArg argument = expected[i];
                CommandToken token = actual[i];
                Assert.AreEqual(
                    argument.contents,
                    token.Contents,
                    $"Contents mismatch for token {i} of '{line}'"
                );
                Assert.AreEqual(
                    argument.startQuote,
                    token.StartQuote,
                    $"Start quote mismatch for token {i} of '{line}'"
                );
                Assert.AreEqual(
                    argument.endQuote,
                    token.EndQuote,
                    $"End quote mismatch for token {i} of '{line}'"
                );
            }
        }

        [Test]
        public void TokenSpansCoverRawText()
        {
            List<CommandToken> tokens = Tokenize("log \"pick axe\" tail");

            Assert.AreEqual(3, tokens.Count);
            Assert.AreEqual(0, tokens[0].Start);
            Assert.AreEqual(3, tokens[0].End);
            Assert.AreEqual(5, tokens[1].Start, "Quoted content starts after the opening quote");
            Assert.AreEqual(13, tokens[1].End, "Quoted content ends before the closing quote");
            Assert.AreEqual(15, tokens[2].Start);
            Assert.AreEqual(19, tokens[2].End);
        }

        [TestCase("log", 0)]
        [TestCase("log foo", 0)]
        public void CaretBeforeTheCommandNameOpensTheNameSlot(string line, int caret)
        {
            List<CommandToken> tokens = Tokenize(line);
            bool found = CommandTokenizer.TryFindActiveToken(
                line,
                caret,
                tokens,
                out int activeTokenIndex,
                out int replacementStart,
                out int replacementLength,
                out bool isNewArgument
            );

            Assert.IsTrue(found, $"Expected a result for caret {caret} in '{line}'");
            Assert.AreEqual(
                0,
                activeTokenIndex,
                $"Command name slot for caret {caret} in '{line}'"
            );
            Assert.IsTrue(
                isNewArgument,
                $"Command name slot is boundary-like for caret {caret} in '{line}'"
            );
            Assert.AreEqual(caret, replacementStart, $"Name slot starts at the caret in '{line}'");
            Assert.AreEqual(0, replacementLength, $"Name slot replaces nothing in '{line}'");
        }

        [Test]
        public void EmptyLineFindsNoToken()
        {
            bool found = CommandTokenizer.TryFindActiveToken(
                string.Empty,
                0,
                new List<CommandToken>(),
                out _,
                out _,
                out _,
                out _
            );

            Assert.IsFalse(found, "An empty line has no tokens to complete");
        }

        [Test]
        public void CaretInsideCommandNameSelectsTheNameToken()
        {
            const string line = "log foo";
            List<CommandToken> tokens = Tokenize(line);

            bool found = CommandTokenizer.TryFindActiveToken(
                line,
                1,
                tokens,
                out int activeTokenIndex,
                out int replacementStart,
                out int replacementLength,
                out bool isNewArgument
            );

            Assert.IsTrue(found);
            Assert.IsFalse(isNewArgument, "Caret 1 sits inside the 'log' token");
            Assert.AreEqual(0, activeTokenIndex, "The command name token is token 0");
            Assert.AreEqual(0, replacementStart);
            Assert.AreEqual(3, replacementLength);
        }

        [TestCase("log foo", 5, 1, "f", 4, 3, false)]
        [TestCase("log foo", 6, 1, "fo", 4, 3, false)]
        [TestCase("log foo", 7, 1, "foo", 4, 3, false)]
        [TestCase("log pickaxe", 10, 1, "pickax", 4, 7, false)]
        [TestCase("pickup \"pi", 10, 1, "pi", 8, 2, true)]
        [TestCase("pickup \"\"", 8, 1, "", 8, 0, true)]
        public void CaretInsideTokenSelectsThatToken(
            string line,
            int caret,
            int expectedIndex,
            string expectedTokenPrefix,
            int expectedStart,
            int expectedLength,
            bool expectedQuoted
        )
        {
            List<CommandToken> tokens = Tokenize(line);
            bool found = CommandTokenizer.TryFindActiveToken(
                line,
                caret,
                tokens,
                out int activeTokenIndex,
                out int replacementStart,
                out int replacementLength,
                out bool isNewArgument
            );

            Assert.IsTrue(found, $"Expected a result for caret {caret} in '{line}'");
            Assert.IsFalse(
                isNewArgument,
                $"Caret {caret} in '{line}' should edit an existing token"
            );
            Assert.AreEqual(
                expectedIndex,
                activeTokenIndex,
                $"Active token index for caret {caret} in '{line}'"
            );
            Assert.AreEqual(expectedStart, replacementStart);
            Assert.AreEqual(expectedLength, replacementLength);
            Assert.AreEqual(
                expectedQuoted,
                tokens[activeTokenIndex].StartQuote != null,
                $"Quoted state for caret {caret} in '{line}'"
            );
            Assert.AreEqual(
                expectedTokenPrefix,
                line.Substring(
                    tokens[activeTokenIndex].Start,
                    caret - tokens[activeTokenIndex].Start
                ),
                $"Token text up to the caret for caret {caret} in '{line}'"
            );
        }

        [TestCase("log foo ", 8)]
        [TestCase("log foo  bar", 9)]
        [TestCase("log \"\" ", 7)]
        [TestCase("pickup ", 7)]
        [TestCase("pickup  ", 8)]
        public void CaretAtBoundaryOpensNewArgument(string line, int caret)
        {
            List<CommandToken> tokens = Tokenize(line);
            bool found = CommandTokenizer.TryFindActiveToken(
                line,
                caret,
                tokens,
                out int activeTokenIndex,
                out int replacementStart,
                out int replacementLength,
                out bool isNewArgument
            );

            Assert.IsTrue(found, $"Expected a result for caret {caret} in '{line}'");
            Assert.IsTrue(isNewArgument, $"Caret {caret} in '{line}' should open a new argument");
            Assert.AreEqual(0, replacementLength, $"Boundary replacement is empty for '{line}'");
            Assert.AreEqual(caret, replacementStart, $"Boundary starts at the caret for '{line}'");
        }

        [Test]
        public void BoundaryAfterFirstArgumentCompletesNextStage()
        {
            const string line = "log foo  bar";
            List<CommandToken> tokens = Tokenize(line);

            bool found = CommandTokenizer.TryFindActiveToken(
                line,
                9,
                tokens,
                out int activeTokenIndex,
                out _,
                out _,
                out bool isNewArgument
            );

            Assert.IsTrue(found);
            Assert.IsTrue(isNewArgument);
            Assert.AreEqual(
                2,
                activeTokenIndex,
                "A boundary between 'foo' and 'bar' completes the argument after 'foo'"
            );
        }

        [Test]
        public void OutOfRangeCaretIsClamped()
        {
            const string line = "log foo ";
            List<CommandToken> tokens = Tokenize(line);

            bool foundNegative = CommandTokenizer.TryFindActiveToken(
                line,
                -10,
                tokens,
                out int negativeIndex,
                out int negativeStart,
                out int negativeLength,
                out bool negativeNew
            );
            bool foundLarge = CommandTokenizer.TryFindActiveToken(
                line,
                999,
                tokens,
                out int largeIndex,
                out int largeStart,
                out int largeLength,
                out bool largeNew
            );

            Assert.IsTrue(foundNegative);
            Assert.AreEqual(0, negativeIndex, "A caret before the command name is the name slot");
            Assert.AreEqual(0, negativeStart);
            Assert.AreEqual(0, negativeLength);
            Assert.IsTrue(negativeNew);

            Assert.IsTrue(foundLarge);
            Assert.AreEqual(2, largeIndex, "A caret past the line end opens the next argument");
            Assert.AreEqual(line.Length, largeStart);
            Assert.AreEqual(0, largeLength);
            Assert.IsTrue(largeNew);
        }
    }
}
