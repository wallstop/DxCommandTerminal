namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;

    public sealed class TryEatArgumentTests
    {
        [TestCase("", false, null, null, null, Description = "Empty string returns false")]
        [TestCase("   ", false, null, null, null, Description = "Whitespace-only returns false")]
        [TestCase("hello", true, "hello", null, null, Description = "Simple unquoted word")]
        [TestCase("hello world", true, "hello", null, null, Description = "First word of multi-word input")]
        [TestCase("\"quoted\"", true, "quoted", '"', '"', Description = "Double-quoted argument")]
        [TestCase("'quoted'", true, "quoted", '\'', '\'', Description = "Single-quoted argument")]
        [TestCase("\"unclosed", true, "unclosed", '"', null, Description = "Unclosed double quote consumes rest")]
        [TestCase("'unclosed", true, "unclosed", '\'', null, Description = "Unclosed single quote consumes rest")]
        [TestCase("\"\"", true, "", '"', '"', Description = "Empty double-quoted string")]
        [TestCase("''", true, "", '\'', '\'', Description = "Empty single-quoted string")]
        [TestCase("\"", true, "", '"', null, Description = "Lone double quote")]
        [TestCase("'", true, "", '\'', null, Description = "Lone single quote")]
        [TestCase("\"hello world\"", true, "hello world", '"', '"', Description = "Quoted string with space")]
        [TestCase("'hello world'", true, "hello world", '\'', '\'', Description = "Single-quoted string with space")]
        [TestCase("\"hello world\" rest", true, "hello world", '"', '"', Description = "Quoted with remainder")]
        [TestCase("'hello' rest", true, "hello", '\'', '\'', Description = "Single-quoted with remainder")]
        [TestCase("  hello", true, "hello", null, null, Description = "Leading whitespace trimmed")]
        [TestCase("  \"hello\"", true, "hello", '"', '"', Description = "Leading whitespace before quote")]
        [TestCase("'hello \"world\"'", true, "hello \"world\"", '\'', '\'', Description = "Double quotes inside single quotes")]
        [TestCase("\"hello 'world'\"", true, "hello 'world'", '"', '"', Description = "Single quotes inside double quotes")]
        [TestCase("hello   world", true, "hello", null, null, Description = "Multiple consecutive spaces returns first word")]
        [TestCase("hello\tworld", true, "hello\tworld", null, null, Description = "Tab character is not a space delimiter")]
        [TestCase("\"quoted\"extra", true, "quoted", '"', '"', Description = "Text after closing quote stops at quote")]
        public void ParsesCorrectly(
            string input,
            bool expectedResult,
            string expectedContents,
            char? expectedStartQuote,
            char? expectedEndQuote
        )
        {
            string remaining = input;
            bool result = CommandShell.TryEatArgument(ref remaining, out CommandArg arg);

            Assert.AreEqual(expectedResult, result, $"TryEatArgument return value mismatch for input: \"{input}\"");

            if (expectedResult)
            {
                Assert.AreEqual(
                    expectedContents,
                    arg.contents,
                    $"Contents mismatch for input: \"{input}\". Expected: \"{expectedContents}\", Got: \"{arg.contents}\""
                );
                Assert.AreEqual(
                    expectedStartQuote,
                    arg.startQuote,
                    $"Start quote mismatch for input: \"{input}\". Expected: '{expectedStartQuote}', Got: '{arg.startQuote}'"
                );
                Assert.AreEqual(
                    expectedEndQuote,
                    arg.endQuote,
                    $"End quote mismatch for input: \"{input}\". Expected: '{expectedEndQuote}', Got: '{arg.endQuote}'"
                );
            }
        }

        [TestCase("hello world", "hello", "world", Description = "Remainder after unquoted word has space consumed")]
        [TestCase("\"quoted\" rest", "quoted", " rest", Description = "Remainder after quoted arg preserves leading space")]
        [TestCase("'unclosed arg", "unclosed arg", "", Description = "Unclosed quote consumes all")]
        [TestCase("word", "word", "", Description = "Single word leaves empty remainder")]
        [TestCase("hello   world", "hello", "  world", Description = "Multiple spaces leaves remaining spaces minus one")]
        [TestCase("\"quoted\"extra", "quoted", "extra", Description = "Text after closing quote is remainder")]
        [TestCase("\"unclosed with spaces", "unclosed with spaces", "", Description = "Unclosed quote with spaces consumes all")]
        public void RemainingStringIsCorrect(
            string input,
            string expectedContents,
            string expectedRemaining
        )
        {
            string remaining = input;
            bool result = CommandShell.TryEatArgument(ref remaining, out CommandArg arg);

            Assert.IsTrue(result, $"Expected TryEatArgument to return true for input: \"{input}\"");
            Assert.AreEqual(
                expectedContents,
                arg.contents,
                $"Contents mismatch for input: \"{input}\""
            );
            Assert.AreEqual(
                expectedRemaining,
                remaining,
                $"Remaining string mismatch for input: \"{input}\". Expected: \"{expectedRemaining}\", Got: \"{remaining}\""
            );
        }
    }
}
