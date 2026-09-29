namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Globalization;
    using Backend;
    using Helper;
    using NUnit.Framework;
    using UI;

    /*
        Log text is data crossing into a display sink, so the sanitizer must
        escape every character the sink reserves and touch nothing else. The
        hostile set is derived from Unicode categories here as well, so the
        two derivations have to agree; a test that reused the production
        classifier would only prove it is self-consistent.
     */
    public sealed class LogTextSanitizerTests
    {
        private static string Escape(char c)
        {
            return $"\\u{(int)c:X4}";
        }

        private static bool IsReserved(char c)
        {
            if (' ' <= c && c <= '~')
            {
                return false;
            }

            if (c == '\n' || c == '\t' || c == '\r')
            {
                return false;
            }

            if (char.IsControl(c))
            {
                return true;
            }

            return CharUnicodeInfo.GetUnicodeCategory(c)
                is UnicodeCategory.Format
                    or UnicodeCategory.LineSeparator
                    or UnicodeCategory.ParagraphSeparator;
        }

        [Test]
        public void EveryReservedCharacterIsEscapedAndEveryOtherSurvives()
        {
            for (int code = 0; code <= char.MaxValue; ++code)
            {
                char c = (char)code;
                string source = c.ToString();
                string actual = LogTextSanitizer.Sanitize(source);
                string expected =
                    c == '\r' ? "\n"
                    : IsReserved(c) ? Escape(c)
                    : source;
                Assert.AreEqual(
                    expected,
                    actual,
                    $"U+{code:X4} should render as '{expected}', not '{actual}'"
                );

                /* Idempotence is load-bearing: trace re-logs stored text. */
                Assert.AreSame(
                    actual,
                    LogTextSanitizer.Sanitize(actual),
                    $"U+{code:X4} was not stable under a second pass"
                );
            }
        }

        [TestCase(0xE0001, TestName = "Scalar.LanguageTag")]
        [TestCase(0xE0020, TestName = "Scalar.TagSpace")]
        [TestCase(0xE007F, TestName = "Scalar.CancelTag")]
        [TestCase(0x110BD, TestName = "Scalar.KaithiNumberSign")]
        [TestCase(0x1D173, TestName = "Scalar.MusicalBeam")]
        public void ReservedSupplementaryScalarsUseTheEightDigitForm(int scalar)
        {
            /*
                Neither half of a surrogate pair is Format on its own, so a
                per-code-unit filter walks straight past an invisible tag
                character. \u plus four digits would print \uE0001, a
                different character from the one that was logged.
             */
            Assert.AreEqual(
                $"\\U{scalar:X8}",
                LogTextSanitizer.Sanitize(char.ConvertFromUtf32(scalar)),
                $"U+{scalar:X} is Format and must render as an eight-digit escape"
            );
        }

        [TestCase(0x1F1FA, 0x1F1F8, TestName = "Scalar.EmojiFlag")]
        [TestCase(0x4E2D, 0x6587, TestName = "Scalar.Cjk")]
        [TestCase(0xD83D, 0xDE00, TestName = "Scalar.Emoji")]
        public void BenignSupplementaryScalarsSurvive(int high, int low)
        {
            /*
                The other direction: the filter must not become user-hostile
                and mangle every non-BMP character it meets.
             */
            string source = new string(new[] { (char)high, (char)low });
            Assert.AreSame(
                source,
                LogTextSanitizer.Sanitize(source),
                "A symbol outside the reserved categories must be returned untouched"
            );
        }

        [Test]
        public void AnUnpairedSurrogateIsLeftAlone()
        {
            /*
                A lone surrogate cannot be classified, and a valid pair must
                not be broken to classify it. Either way the engine renders a
                replacement glyph, which is a cosmetic, not a spoof.
             */
            string lone = "\uD800";
            Assert.AreSame(lone, LogTextSanitizer.Sanitize(lone));
        }

        [Test]
        public void OrdinaryTextIsNotRebuilt()
        {
            string message = "Player took 12 damage (health 88/100) at (3.5, -2.0, 0.0)";
            Assert.AreSame(
                message,
                LogTextSanitizer.Sanitize(message),
                "A clean message must return the same reference so a routine log write allocates nothing"
            );
        }

        [Test]
        public void NewlineAndTabSurvive()
        {
            string message = "line one\n\tindented";
            Assert.AreSame(message, LogTextSanitizer.Sanitize(message));
        }

        [Test]
        public void CarriageReturnBecomesOneLineBreak()
        {
            Assert.AreEqual(
                "one\ntwo",
                LogTextSanitizer.Sanitize("one\r\ntwo"),
                "A CRLF pair is one line break, never a visible escape"
            );
            Assert.AreEqual(
                "one\ntwo",
                LogTextSanitizer.Sanitize("one\rtwo"),
                "A lone carriage return is still a line break"
            );
            Assert.AreEqual(
                "one\ntwo\nthree",
                LogTextSanitizer.Sanitize("one\r\ntwo\r\nthree"),
                "Every frame of a multi-frame trace must stay clean"
            );
        }

        [Test]
        public void PathsAndRegexesSurviveUnchanged()
        {
            /*
                A filter that mangles readable text is its own defect. A
                Windows path, a regex, and a JSON payload are the everyday
                log lines that must read exactly as they were written.
             */
            string message = @"loaded C:\Users\dev\file.txt with \d+ and ""escaped"" \n";
            Assert.AreSame(
                message,
                LogTextSanitizer.Sanitize(message),
                "A readable line must return the same reference, unescaped"
            );
        }

        [Test]
        public void SanitizingIsIdempotent()
        {
            /*
                The trace command re-logs an already-sanitized message, so a
                second pass must be a no-op or the line grows on every trace.
                Both the escape and the carriage-return fold have to hold.
             */
            string source = "Admin\u202Eexe\u0007 path C:\\temp\r\nnext";
            string once = LogTextSanitizer.Sanitize(source);
            Assert.AreEqual(once, LogTextSanitizer.Sanitize(once), "Escapes must be stable");
            Assert.AreSame(
                once,
                LogTextSanitizer.Sanitize(once),
                "A sanitized message re-entering the funnel must return the same reference"
            );
        }

        [Test]
        public void ADisplayCopyIsItsOwnArrayAndRefillsInPlace()
        {
            /*
                A popup shows this copy while the index it returns selects the
                name the caller still holds, so the copy must be a separate
                array, and an inspector that redraws every frame must be able
                to refill it without a second allocation.
             */
            string[] names = { "Admin\u202Eexe", "plain" };

            /* Both production callers start here, not from null. */
            string[] copy = Array.Empty<string>();
            LogTextSanitizer.SanitizeInto(names, ref copy);
            Assert.AreEqual(new[] { "Admin\\u202Eexe", "plain" }, copy, "Both names escape");
            Assert.AreEqual(
                "Admin\u202Eexe",
                names[0],
                "The caller's own name stays raw: the popup index selects it"
            );
            Assert.AreSame("plain", copy[1], "A clean name passes through by reference");

            LogTextSanitizer.SanitizeInto(new[] { "one", "two", "three" }, ref copy);
            Assert.AreEqual(3, copy.Length, "A different length needs a new array");
            Assert.AreEqual(new[] { "one", "two", "three" }, copy, "The refill holds every name");

            string[] refilled = copy;
            LogTextSanitizer.SanitizeInto(new[] { "four", "five", "six" }, ref copy);
            Assert.AreSame(refilled, copy, "A same-length refill must not reallocate");
            Assert.AreEqual("four", copy[0], "A refill replaces the contents");

            string[] fromNull = null;
            LogTextSanitizer.SanitizeInto(names, ref fromNull);
            Assert.AreEqual(copy, fromNull, "A null copy is allocated, never the input");
            Assert.AreNotSame(names, fromNull, "The caller's array is never the copy");
        }

        [Test]
        public void NothingIsTruncated()
        {
            /*
                The cap was removed: the output label wraps inside a vertical
                scroller, so a long line was reachable rather than clipped, and
                trimming at the funnel destroyed log data. Long lines must
                survive whole.
             */
            string message = new string('a', 5000);
            Assert.AreSame(
                message,
                LogTextSanitizer.Sanitize(message),
                "Log data must never be truncated at the funnel"
            );
        }

        [Test]
        public void TheWholeMessageRunsThroughTheBuffer()
        {
            CommandLog log = new(4);
            Assert.IsTrue(
                log.HandleLog("Admin\u202Eexe", string.Empty, TerminalLogType.Message),
                "Sanity: the write must land"
            );
            LogItem stored = log.Logs[log.Logs.Count - 1];
            Assert.AreEqual(
                "Admin\\u202Eexe",
                stored.message,
                "The shared funnel must sanitize, not just the terminal view"
            );
        }

        [Test]
        public void TheStackTraceIsNormalizedToo()
        {
            CommandLog log = new(4) { stackTraceMode = TerminalStackTraceMode.All };
            Assert.IsTrue(
                log.HandleLog("boom", "at Frame\u202E()", TerminalLogType.Error),
                "Sanity: the write must land"
            );
            LogItem stored = log.Logs[log.Logs.Count - 1];
            Assert.AreEqual(
                "at Frame\\u202E()",
                stored.stackTrace,
                "A trace reaches the log list through the same command, so it funnels too"
            );
        }

        [Test]
        public void ShortPaletteOutputIsUntouched()
        {
            string text = "ok\ndone";
            Assert.AreSame(
                text,
                CommandPaletteUI.ClampOutputLength(text, 512),
                "Output inside the budget must return the same reference"
            );
        }

        [Test]
        public void PaletteOutputCutsOnALineBreakInsideTheBudget()
        {
            string text = "0123456789\n0123456789\n0123456789";
            Assert.AreEqual(
                "0123456789\n0123456789\n(+11 more chars)",
                CommandPaletteUI.ClampOutputLength(text, 22),
                "The cut must land on the last break inside the budget, and count only hidden characters"
            );
        }

        [Test]
        public void PaletteOutputUsesTheWholeBudgetWhenABreakFallsOnIt()
        {
            /*
                A break exactly on the boundary still fills the budget rather
                than cutting a character early.
             */
            Assert.AreEqual(
                $"{new string('a', 5)}\n(+5 more chars)",
                CommandPaletteUI.ClampOutputLength("aaaaa\nbbbb", 5),
                "The break at the budget boundary must be the cut, and it counts as hidden"
            );
        }

        [Test]
        public void PaletteOutputHardCutsASingleOverlongLine()
        {
            Assert.AreEqual(
                $"{new string('a', 10)}\n(+30 more chars)",
                CommandPaletteUI.ClampOutputLength(new string('a', 40), 10),
                "A single line past the budget has no break to cut on, so it hard-cuts"
            );
        }
    }
}
