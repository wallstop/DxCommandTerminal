namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UI;

    /*
        The decision a search makes: which lines of the log view survive, how
        many there are, and which one the developer is on. The wiring - the
        command, the key, and the children the view draws - is
        TerminalUILogFilterTests; this file is the decision.

        Every test applies a window and reads the result back out of the
        destination array, the way the view does, so a match count and the
        lines it counted cannot disagree.
     */
    public sealed class LogFilterTests
    {
        private static LogItem[] Window(params string[] messages)
        {
            LogItem[] window = new LogItem[messages.Length];
            for (int i = 0; i < messages.Length; ++i)
            {
                window[i] = new LogItem(TerminalLogType.Message, messages[i], string.Empty);
            }

            return window;
        }

        [Test]
        public void AQueryKeepsTheLinesThatHoldItInLogOrder()
        {
            LogFilter filter = new();
            Assert.That(filter.SetQuery("NullReference"), Is.True);

            LogItem[] window = Window(
                "starting",
                "NullReferenceException: value",
                "middle",
                "also NullReference here"
            );
            LogItem[] destination = new LogItem[window.Length];

            int kept = filter.Apply(window, window.Length, destination);

            Assert.That(kept, Is.EqualTo(2), "Only the two matching lines are drawn");
            Assert.That(destination[0].message, Is.EqualTo("NullReferenceException: value"));
            Assert.That(
                destination[1].message,
                Is.EqualTo("also NullReference here"),
                "The oldest match is drawn first, so the view reads top to bottom like the log"
            );
            Assert.That(filter.MatchCount, Is.EqualTo(2));
            Assert.That(
                filter.TotalCount,
                Is.EqualTo(4),
                "The count a developer compares against is the whole log, not the matches"
            );
        }

        [Test]
        public void MatchingIgnoresCaseAndFollowsNoCulture()
        {
            /*
                A developer types the shortest thing they remember, and the
                exception they are looking for is spelled with capitals. A
                culture-sensitive comparison would also decide that two
                characters are the same letter, which is not a question a log
                search should be asking.
             */
            LogFilter filter = new();
            Assert.That(filter.SetQuery("nullreference"), Is.True);

            LogItem[] window = Window("NullReferenceException: value");
            int kept = filter.Apply(window, window.Length, new LogItem[window.Length]);

            Assert.That(kept, Is.EqualTo(1), "A lowercase search reaches the capitalized line");
            Assert.That(
                LogFilter.Matches(
                    new LogItem(TerminalLogType.Message, "straße", string.Empty),
                    "STRASSE"
                ),
                Is.False,
                "No culture rule folds a sharp s into a double s"
            );
        }

        [Test]
        public void AQueryMatchesTheLineTheLogShows()
        {
            /*
                The text is escaped on the way into the buffer, so the message
                is already what the developer is reading. A search that matched
                something else would find lines they cannot see, which is the
                one thing a search must not do.
             */
            LogItem escaped = new(TerminalLogType.Message, @"name is Admin‮exe", string.Empty);

            Assert.That(
                LogFilter.Matches(escaped, "‮"),
                Is.True,
                "The escape the view draws is in the text the search reads"
            );
            Assert.That(
                LogFilter.Matches(escaped, "Admin"),
                Is.True,
                "The visible part of the name still matches"
            );
        }

        [Test]
        public void ACommandEchoIsNotAMatch()
        {
            /*
                The defect this pins. Both console surfaces echo the typed line
                into the log as `Input` before the handler runs, so the search's
                own echo - `find the hit` - sits in the window holding the
                query in its own text. Matching it made every search find
                itself: the count was always one too high, and a query that
                appears nowhere still reported a match.
             */
            LogItem echo = new(TerminalLogType.Input, "find the hit", string.Empty);

            Assert.That(
                LogFilter.Matches(echo, "the hit"),
                Is.False,
                "A search cannot find the command that performed it"
            );
            Assert.That(
                LogFilter.Matches(
                    new LogItem(TerminalLogType.Message, "find the hit", string.Empty),
                    "the hit"
                ),
                Is.True,
                "Output holding the same words is a match; only the echo type is excluded"
            );
        }

        [Test]
        public void AQueryThatOnlyTheEchoHoldsReportsNoMatch()
        {
            /*
                The user-visible shape of the same defect, and the answer a
                search has to be able to give. Without the exclusion this window
                matched its own echo and reported a hit for a string the log
                holds nowhere else.
             */
            LogFilter filter = new();
            Assert.That(filter.SetQuery("the hit"), Is.True);

            LogItem[] window =
            {
                new LogItem(TerminalLogType.Message, "routine frame", string.Empty),
                new LogItem(TerminalLogType.Input, "find the hit", string.Empty),
            };
            int kept = filter.Apply(window, window.Length, new LogItem[window.Length]);

            Assert.That(kept, Is.EqualTo(0), "A query no output holds found nothing");
            Assert.That(filter.MatchCount, Is.EqualTo(0));
        }

        [Test]
        public void TheEchoExclusionIsByTypeSoTheCountDoesNotMoveUnderTheDeveloper()
        {
            /*
                Excluding only the trailing echo - the shape the copy commands
                use - would make the count depend on whether anything was logged
                since. The same search would report a different number on the
                frame the developer ran their next command, and a line would
                appear in the view without them doing anything. Excluded by
                type, the count holds however much else the log has taken since.
             */
            LogFilter filter = new();
            Assert.That(filter.SetQuery("hit"), Is.True);

            LogItem[] before =
            {
                new LogItem(TerminalLogType.Message, "hit one", string.Empty),
                new LogItem(TerminalLogType.Message, "miss", string.Empty),
            };
            int first = filter.Apply(before, before.Length, new LogItem[before.Length]);

            /*
                The echo goes at the newest end, where a positional exclusion
                would drop it, and output carrying the same text goes behind it.
                A by-position rule and a by-type rule disagree here and only
                here: positionally the search ranges over two lines, by type
                over four.
             */
            LogItem[] after =
            {
                new LogItem(TerminalLogType.Message, "hit one", string.Empty),
                new LogItem(TerminalLogType.Message, "miss", string.Empty),
                new LogItem(TerminalLogType.Message, "hit two", string.Empty),
                new LogItem(TerminalLogType.Input, "find hit", string.Empty),
            };
            int second = filter.Apply(after, after.Length, new LogItem[after.Length]);

            Assert.That(
                second,
                Is.EqualTo(first + 1),
                "A new match and a new echo are two different things to the count"
            );
            Assert.That(
                filter.TotalCount,
                Is.EqualTo(3),
                "The echoed command is not one of the lines the search ranged over"
            );
        }

        [Test]
        public void AQueryOfNothingButWhitespaceIsRefused()
        {
            LogFilter filter = new();
            Assert.That(filter.SetQuery("boom"), Is.True, "A search is set to be lost");

            Assert.That(
                filter.SetQuery("   "),
                Is.False,
                "Every line contains a space, so this is not a search"
            );
            Assert.That(filter.SetQuery(string.Empty), Is.False);
            Assert.That(filter.SetQuery(null), Is.False);
            Assert.That(
                filter.Query,
                Is.EqualTo("boom"),
                "A refused query leaves the search the developer set alone"
            );
        }

        [Test]
        public void AQueryIsTrimmed()
        {
            LogFilter filter = new();
            Assert.That(filter.SetQuery("  boom  "), Is.True);

            Assert.That(
                filter.Query,
                Is.EqualTo("boom"),
                "A pasted query with a trailing space still finds the line"
            );
        }

        [Test]
        public void ANewQueryLandsOnTheFirstMatch()
        {
            LogFilter filter = new();
            Assert.That(filter.SetQuery("hit"), Is.True);

            LogItem[] window = Window("hit one", "miss", "hit two");
            filter.Apply(window, window.Length, new LogItem[window.Length]);

            Assert.That(
                filter.CurrentMatch,
                Is.EqualTo(1),
                "The first match is where a search starts"
            );
        }

        [Test]
        public void SteppingWrapsAtBothEnds()
        {
            LogFilter filter = new();
            Assert.That(filter.SetQuery("hit"), Is.True);

            LogItem[] window = Window("hit one", "hit two", "miss", "hit three");
            int kept = filter.Apply(window, window.Length, new LogItem[window.Length]);
            Assert.That(kept, Is.EqualTo(3));

            Assert.That(filter.StepForward(), Is.True);
            Assert.That(filter.CurrentMatch, Is.EqualTo(2));
            Assert.That(filter.StepForward(), Is.True);
            Assert.That(filter.CurrentMatch, Is.EqualTo(3), "The last match, not past it");
            Assert.That(filter.StepForward(), Is.True);
            Assert.That(
                filter.CurrentMatch,
                Is.EqualTo(1),
                "Forward from the last match wraps to the first"
            );
            Assert.That(filter.StepBackward(), Is.True);
            Assert.That(
                filter.CurrentMatch,
                Is.EqualTo(3),
                "Backward from the first match wraps to the last"
            );
        }

        [Test]
        public void SteppingWithNoMatchesReportsIt()
        {
            LogFilter filter = new();
            Assert.That(filter.SetQuery("hit"), Is.True);

            LogItem[] window = Window("miss", "miss");
            filter.Apply(window, window.Length, new LogItem[window.Length]);

            Assert.That(filter.MatchCount, Is.EqualTo(0));
            Assert.That(filter.StepForward(), Is.False, "There is nothing to step to");
            Assert.That(filter.StepBackward(), Is.False);
        }

        [Test]
        public void ARotatingBufferDoesNotLeaveThePositionPastTheEnd()
        {
            /*
                The position is a place in the kept list, and the list changes
                under a search that is left standing: every new line rotates
                the ring, and a match can be pushed out of the window entirely.
                A position past the end would name a child the view is not
                drawing, and the search would jump nowhere.
             */
            LogFilter filter = new();
            Assert.That(filter.SetQuery("hit"), Is.True);

            LogItem[] window = Window("hit one", "hit two", "hit three");
            int kept = window.Length;
            filter.Apply(window, kept, new LogItem[kept]);
            filter.StepForward();
            filter.StepForward();
            Assert.That(filter.CurrentMatch, Is.EqualTo(3));

            LogItem[] rotated = Window("miss", "hit two");
            kept = filter.Apply(rotated, rotated.Length, new LogItem[rotated.Length]);

            Assert.That(kept, Is.EqualTo(1));
            Assert.That(
                filter.CurrentMatch,
                Is.EqualTo(1),
                "Clamped to a position the current window has"
            );
        }

        [Test]
        public void ADestinationSmallerThanTheMatchesTruncates()
        {
            /*
                The view sizes its array to the buffer's capacity, so this is
                the shape that never happens in the product. It is pinned
                because the alternative is a write past the end of the
                caller's array, and a caller that sized its own array wrong
                should get fewer lines rather than a corrupted one.
             */
            LogFilter filter = new();
            Assert.That(filter.SetQuery("hit"), Is.True);

            LogItem[] window = Window("hit one", "hit two", "hit three");
            int kept = filter.Apply(window, window.Length, new LogItem[1]);

            Assert.That(kept, Is.EqualTo(1));
            Assert.That(
                filter.MatchCount,
                Is.EqualTo(1),
                "The count is what was drawn, not what matched"
            );
            Assert.That(filter.TotalCount, Is.EqualTo(3));
        }

        [Test]
        public void ClearingDropsTheQueryAndTheCounts()
        {
            LogFilter filter = new();
            Assert.That(filter.SetQuery("hit"), Is.True);

            LogItem[] window = Window("hit one", "miss");
            filter.Apply(window, window.Length, new LogItem[window.Length]);

            filter.Clear();

            Assert.That(filter.IsActive, Is.False);
            Assert.That(filter.Query, Is.Null);
            Assert.That(filter.MatchCount, Is.EqualTo(0));
            Assert.That(filter.TotalCount, Is.EqualTo(0));
            Assert.That(
                filter.CurrentMatch,
                Is.Null,
                "A cleared search has no position to resume from"
            );
            Assert.That(filter.StepForward(), Is.False);
        }

        [Test]
        public void AnUnfilteredFilterKeepsTheWholeWindow()
        {
            /*
                Not a property of a fresh object: what the view relies on is
                that an inactive filter is never handed to `Apply` at all, and
                that the counts it reports are zero rather than left over, so a
                stale "Match 3 of 20" cannot survive into a search that was
                never set.
             */
            LogFilter filter = new();

            Assert.That(
                filter.IsActive,
                Is.False,
                "A terminal with no search draws the window itself, never through this"
            );
            Assert.That(filter.MatchCount, Is.EqualTo(0));
            Assert.That(filter.TotalCount, Is.EqualTo(0));
            Assert.That(filter.CurrentMatch, Is.Null);
            Assert.That(
                filter.StepForward(),
                Is.False,
                "There is nothing to step through, and saying so is what stops a stale count"
            );
        }

        [Test]
        public void ApplyRunsToTheEndOfTheWindowEvenWhenTheDestinationIsFull()
        {
            /*
                The two numbers a search reports come from one pass over the
                window. A loop that stopped when the destination filled - the
                obvious way to write this - would count a truncated set of
                matches beside a complete set of candidates, and the report
                would describe a window the search never finished reading.
             */
            LogFilter filter = new();
            Assert.That(filter.SetQuery("hit"), Is.True);

            LogItem[] window = Window("hit one", "hit two", "hit three", "miss", "hit four");
            int kept = filter.Apply(window, window.Length, new LogItem[2]);

            Assert.That(kept, Is.EqualTo(2), "The destination held what it could");
            Assert.That(filter.MatchCount, Is.EqualTo(2), "The count is what was drawn");
            Assert.That(
                filter.TotalCount,
                Is.EqualTo(window.Length),
                "The denominator still covers every line the search could have matched"
            );
        }
    }
}
