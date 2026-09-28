namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Extensions;
    using NUnit.Framework;

    public sealed class StringExtensionsTests
    {
        /*
            The caret is a UTF-16 offset, and these are the offsets that sit
            inside a character. Each row is one of them with the position the
            caret has to land on instead; every value was derived from the
            character's own code units, not from the implementation.

            The rows cover the shapes a developer types or pastes: a surrogate
            pair (an emoji), a mark (an accented letter), a joined sequence (a
            family emoji, a skin tone), a flag's two letters, and a
            subdivision flag's invisible tag letters.
         */
        [TestCase("give item", 0, 0, Description = "The start of the value is a boundary")]
        [TestCase("give item", 5, 5, Description = "ASCII is already whole characters")]
        [TestCase("give item", 9, 9, Description = "The end of the value is a boundary")]
        [TestCase("give item", 400, 9, Description = "A caret past the end clamps to it")]
        [TestCase("give item", -2, 0, Description = "A negative caret clamps to the start")]
        [TestCase("", 3, 0, Description = "An empty value has one boundary")]
        [TestCase("give \ud83d\ude00 x", 6, 5, Description = "Inside an emoji")]
        [TestCase("give \ud83d\ude00 x", 7, 7, Description = "After an emoji is unchanged")]
        [TestCase("\ud83d\ude00", 1, 0, Description = "An emoji that is the whole value")]
        [TestCase("give x\ud83d\ude00", 8, 8, Description = "A caret at the end after an emoji")]
        [TestCase("cafe\u0301 x", 4, 3, Description = "Between a letter and its accent")]
        [TestCase("cafe\u0301 x", 5, 5, Description = "After an accent is unchanged")]
        [TestCase("\u0301", 1, 1, Description = "A leading mark is its own character")]
        [TestCase(
            "\ud83d\udc68\u200d\ud83d\udc69\u200d\ud83d\udc66",
            3,
            0,
            Description = "Inside a family"
        )]
        [TestCase(
            "\ud83d\udc68\u200d\ud83d\udc69\u200d\ud83d\udc66",
            6,
            0,
            Description = "Inside a family, later"
        )]
        [TestCase(
            "\ud83d\udc68\u200d\ud83d\udc69\u200d\ud83d\udc66",
            8,
            8,
            Description = "After a family"
        )]
        [TestCase(
            "\ud83d\udc68\u200d\ud83d\udc69\u200d\ud83d\udc66ab",
            9,
            9,
            Description = "Text after a family"
        )]
        [TestCase(
            "\ud83d\udc4d\ud83c\udffd",
            2,
            0,
            Description = "Between an emoji and its skin tone"
        )]
        [TestCase("\ud83d\udc4d\ud83c\udffd", 4, 4, Description = "After a skin tone")]
        [TestCase(
            "\u2764\ufe0f",
            1,
            0,
            Description = "Between a letter and its variation selector"
        )]
        [TestCase("\u2764\ufe0f", 2, 2, Description = "After a variation selector")]
        [TestCase("\ud83c\udde6\ud83c\udde7 x", 3, 0, Description = "Between a flag's two letters")]
        [TestCase("\ud83c\udde6\ud83c\udde7 x", 4, 4, Description = "After a flag")]
        [TestCase(
            "\ud83c\uddf5\ud83c\uddff x",
            3,
            0,
            Description = "A flag whose letters are the last two of the alphabet"
        )]
        [TestCase(
            "\ud83c\udde6\ud83c\udde7\ud83c\udde8",
            5,
            4,
            Description = "Inside the second letter of a flag"
        )]
        [TestCase(
            "\ud83c\udde6\ud83c\udde7\ud83c\udde8",
            4,
            4,
            Description = "A third letter starts a character"
        )]
        [TestCase(
            "\ud83c\udde6\ud83c\udde7\ud83c\udde8\ud83c\udde9",
            5,
            4,
            Description = "Two flags: inside the second flag's first letter"
        )]
        [TestCase(
            "\ud83c\udde6\ud83c\udde7\ud83c\udde8\ud83c\udde9",
            8,
            8,
            Description = "Two flags: after both"
        )]
        [TestCase(
            "\ud83c\udff0\ud83c\udde6",
            2,
            2,
            Description = "A castle is built from flag pieces but is not a flag letter"
        )]
        [TestCase(
            "\ud83c\udff4\udb40\udc67\udb40\udc62",
            3,
            0,
            Description = "Inside a subdivision flag's invisible tag letters"
        )]
        [TestCase(
            "\ud83c\udff4\udb40\udc67\udb40\udc62",
            2,
            0,
            Description = "A subdivision flag is one character, tag letters and all"
        )]
        [TestCase(
            "\ud83c\udff4\udb40\udc67\udb40\udc62",
            6,
            6,
            Description = "After a subdivision flag"
        )]
        [TestCase("a\u00adb", 1, 1, Description = "A soft hyphen is its own character")]
        [TestCase("\u1100\u1161", 1, 1, Description = "Hangul jamo are letters here")]
        public void SnapToTextBoundaryMovesACaretOutOfACharacter(
            string value,
            int caret,
            int expected
        )
        {
            Assert.AreEqual(expected, value.SnapToTextBoundary(caret));
        }

        [Test]
        public void SnapToTextBoundaryNeverLandsInsideASurrogatePair()
        {
            /*
                Every code unit of a string that mixes the shapes a command line
                holds, in every position, is a caret the package can be handed.
                None of them may come back pointing between the halves of a
                character, which is the property the whole helper exists for.
            */
            const string value =
                "give \ud83d\ude00 cafe\u0301 \ud83d\udc4d\ud83c\udffd \ud83c\udff4\udb40\udc67\udb40\udc62 "
                + "\ud83d\udc68\u200d\ud83d\udc69 \ud83c\udde6\ud83c\udde7 x";
            for (int caret = 0; caret <= value.Length; ++caret)
            {
                int snapped = value.SnapToTextBoundary(caret);
                Assert.LessOrEqual(snapped, caret, $"caret {caret} moved forward");
                Assert.That(
                    0 <= snapped && snapped <= value.Length,
                    Is.True,
                    $"caret {caret} snapped out of range to {snapped}"
                );
                bool splits =
                    0 < snapped
                    && snapped < value.Length
                    && char.IsLowSurrogate(value[snapped])
                    && char.IsHighSurrogate(value[snapped - 1]);
                Assert.IsFalse(splits, $"caret {caret} snapped inside a character");
            }
        }

        [Test]
        public void SnapToTextBoundaryTreatsANullValueAsEmpty()
        {
            Assert.AreEqual(0, ((string)null).SnapToTextBoundary(4));
        }
    }
}
