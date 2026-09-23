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

        [TestCase("$target", "\"$target\"")]
        [TestCase("$", "\"$\"")]
        [TestCase("$target's", "\"$target's\"")]
        [TestCase("$target\"quoted", "'$target\"quoted'")]
        [TestCase("$target name", "\"$target name\"")]
        [TestCase("target$", "target$")]
        [TestCase("target", "target")]
        [TestCase("target name", "\"target name\"")]
        public void UnquotedInsertionPreservesLiteralValue(string value, string expected)
        {
            string insertion = CommandTokenizer.QuoteInsertionIfNeeded(value, false);
            Assert.AreEqual(expected, insertion);
            List<CommandToken> tokens = Tokenize(insertion);
            Assert.AreEqual(1, tokens.Count);
            Assert.AreEqual(value, tokens[0].Contents);
            if (value.StartsWith('$'))
            {
                Assert.That(tokens[0].EndQuote != null);
            }
        }

        [TestCase('"')]
        [TestCase('\'')]
        public void ClosedQuotedInsertionKeepsExistingDelimiters(char quote)
        {
            const string value = "$target";
            string insertion = CommandTokenizer.QuoteInsertionIfNeeded(value, true);
            Assert.AreEqual(value, insertion);
            List<CommandToken> tokens = Tokenize($"{quote}{insertion}{quote}");
            Assert.AreEqual(1, tokens.Count);
            Assert.AreEqual(value, tokens[0].Contents);
            Assert.AreEqual(quote, tokens[0].EndQuote);
        }

        [TestCase("inspect $", "$target")]
        [TestCase("inspect \"$", "$target")]
        [TestCase("inspect '$", "$target")]
        [TestCase("inspect \"$\" tail", "$target\"quoted")]
        [TestCase("inspect '$' tail", "$target's")]
        [TestCase("inspect \"pi", "pick axe")]
        [TestCase("inspect \"x\" tail", "x\"'")]
        [TestCase("inspect x", "\tx")]
        [TestCase("inspect x", "\u2003x")]
        [TestCase("inspect x", "x\\path")]
        [TestCase("inspect x", "x$target")]
        public void PreparedInsertionRoundTripsLiteral(string input, string value)
        {
            List<CommandToken> before = Tokenize(input);
            CommandToken active = before[1];
            Assert.IsTrue(
                CommandTokenizer.TryPrepareInsertion(
                    input,
                    value,
                    active.Start,
                    active.End - active.Start,
                    active.StartQuote != null,
                    out string insertion,
                    out int start,
                    out int length
                )
            );
            string completed = input.Remove(start, length).Insert(start, insertion);
            List<CommandToken> after = Tokenize(completed);
            Assert.AreEqual(before.Count, after.Count);
            Assert.AreEqual(value, after[1].Contents);
            if (value.StartsWith('$'))
            {
                Assert.That(after[1].EndQuote != null);
            }
            for (int i = 2; i < before.Count; ++i)
            {
                Assert.AreEqual(before[i].Contents, after[i].Contents);
            }
        }

        [TestCase("inspect abc tail", 8, 1, "x", false, "inspect xbc tail")]
        [TestCase("inspect \"abc\" tail", 9, 1, "x", true, "inspect \"xbc\" tail")]
        [TestCase("inspect abc tail", 8, 1, "torch pick", false, "inspect \"torch pick\"bc tail")]
        [TestCase("inspect \"ax\" tail", 10, 1, "$n", true, "inspect \"a$n\" tail")]
        public void CustomRangeReplacesRawSubstringPreservingTail(
            string input,
            int start,
            int length,
            string value,
            bool tokenQuoted,
            string expectedLine
        )
        {
            Assert.IsTrue(
                CommandTokenizer.TryPrepareInsertion(
                    input,
                    value,
                    start,
                    length,
                    tokenQuoted,
                    out string insertion,
                    out int replacementStart,
                    out int replacementLength,
                    wholeToken: false
                )
            );
            Assert.AreEqual(start, replacementStart);
            Assert.AreEqual(length, replacementLength);
            Assert.AreEqual(expectedLine, input.Remove(start, length).Insert(start, insertion));
        }

        [TestCase("inspect $", "$target\"'")]
        [TestCase("inspect \"$", "$target\"'")]
        [TestCase("inspect '$'", "$target\"'")]
        [TestCase("inspect x", "x \"'")]
        public void UnrepresentableInsertionsAreRejected(string input, string value)
        {
            CommandToken active = Tokenize(input)[1];
            Assert.IsFalse(
                CommandTokenizer.TryPrepareInsertion(
                    input,
                    value,
                    active.Start,
                    active.End - active.Start,
                    active.StartQuote != null,
                    out string insertion,
                    out _,
                    out _
                )
            );
            Assert.IsEmpty(insertion);
        }

        [TestCase("'", "$target")]
        [TestCase("`", "$target\"'")]
        [TestCase("`'", "`target")]
        public void PreparedInsertionUsesConfiguredDelimiters(string delimiters, string value)
        {
            char[] original = CommandArg.Quotes.ToArray();
            try
            {
                CommandArg.Quotes.Clear();
                CommandArg.Quotes.AddRange(delimiters);
                Assert.IsTrue(
                    CommandTokenizer.TryPrepareInsertion(
                        "inspect x",
                        value,
                        8,
                        1,
                        false,
                        out string insertion,
                        out int start,
                        out int length
                    )
                );
                List<CommandToken> tokens = Tokenize(
                    "inspect x".Remove(start, length).Insert(start, insertion)
                );
                Assert.AreEqual(2, tokens.Count);
                Assert.AreEqual(value, tokens[1].Contents);
                Assert.That(tokens[1].EndQuote != null);
            }
            finally
            {
                CommandArg.Quotes.Clear();
                CommandArg.Quotes.AddRange(original);
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
        public void CaretOnTheCommandNameSelectsTheNameToken(string line, int caret)
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
            Assert.IsFalse(isNewArgument, $"Caret {caret} in '{line}' selects the name token");
            Assert.AreEqual(0, activeTokenIndex);
            Assert.AreEqual(0, replacementStart);
            Assert.AreEqual(3, replacementLength);
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
        [TestCase("log foo", 4, 1, "", 4, 3, false)]
        [TestCase("log foo  bar", 9, 2, "", 9, 3, false)]
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
            const string line = "log foo  ";
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
                "A boundary after 'foo' completes the argument after 'foo'"
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
            Assert.AreEqual(0, negativeIndex, "A clamped caret on 'log' selects the name token");
            Assert.IsFalse(negativeNew);
            Assert.AreEqual(0, negativeStart);
            Assert.AreEqual(3, negativeLength);

            Assert.IsTrue(foundLarge);
            Assert.AreEqual(2, largeIndex, "A caret past the line end opens the next argument");
            Assert.AreEqual(line.Length, largeStart);
            Assert.AreEqual(0, largeLength);
            Assert.IsTrue(largeNew);
        }
    }
}
