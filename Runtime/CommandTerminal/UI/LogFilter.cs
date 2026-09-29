namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using Backend;

    /*
        A search over the log view: which lines stay on screen, how many of
        them there are, and which one the developer is reading.

        The log holds the newest N lines and nothing else, so the only way to
        reach one of them is to read past the rest. A filter hides what does
        not match, which is the thing a developer paging a full buffer by hand
        is already doing, one viewport at a time.

        Matching runs against the line as the log shows it. The text is
        escaped on the way into the buffer, so `LogItem.message` is what the
        developer is looking at, and a search that cannot find what is on
        screen is not a search. Ordinal and case-insensitive: `nullref` should
        reach `NullReferenceException`, and no culture rule decides whether two
        characters are the same letter on this machine.

        The position is the match's place in the kept list, not an index into
        the buffer. The ring rotates under the developer - every new line
        moves it - so a buffer index would name a different line after the next
        log entry, and a search that jumped somewhere else on its own would be
        worse than having no position at all.
     */
    internal sealed class LogFilter
    {
        public string Query { get; private set; }

        public bool IsActive => !string.IsNullOrEmpty(Query);

        /* Kept lines, and the lines the window held, from the same pass. */
        public int MatchCount { get; private set; }

        public int TotalCount { get; private set; }

        /* One-based, so the number a developer reads is the number reported. */
        public int? CurrentMatch { get; private set; }

        /*
            Whether a line survives the search. The message is never null -
            `LogItem` is the only way to make one and it coalesces - and the
            query is never empty, because a filter that keeps everything while
            reporting that it filtered is not a filter.

            Command echoes never match, and that is the whole reason this is
            not `message.Contains(query)` alone. Both console surfaces write
            the typed line into the log as `Input` BEFORE the handler runs, so
            the search's own echo - `find the hit` - is in the window this
            reads and holds the query in its own text. Every search then found
            itself: the count was inflated by at least one, and a query that
            appears nowhere still reported a match, which is the one answer a
            search must never give. See the log-echo-contract skill.

            The exclusion is by type rather than by position. Excluding only
            the trailing echo - the shape the copy commands use - would make
            the count depend on whether anything was logged since: the same
            search would report a different number on the frame the developer
            ran their next command, and a line would appear in the view
            without the developer having done anything. By type it is stable
            for as long as the search is set.

            The limit this buys is stated rather than hidden: a search cannot
            find a command the developer ran, only what the program said about
            it. The developer knows what they typed, and the output is what
            they are looking for.
         */
        public static bool Matches(LogItem item, string query)
        {
            return IsSearchable(item)
                && !string.IsNullOrEmpty(query)
                && item.message.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        /*
            Whether a line the search already excluded would have matched, had
            it been a candidate. The count the developer reads counts the lines
            the search ranged over, and an echo is not one of them; this is how
            the denominator is derived without a second string comparison per
            line, and it is not the rule - a caller that wants to know if a
            line matches asks `Matches`, which applies the exclusion itself.
         */
        private static bool IsSearchable(LogItem item)
        {
            return item.type != TerminalLogType.Input;
        }

        /*
            Replaces the query and forgets the position: the new query has a
            different set of matches, and the old position names one of them
            that may not be there.

            A query of nothing but whitespace is refused rather than trimmed to
            nothing. Every line contains a space, so honoring it would report
            a count of the whole log and hide nothing, and clearing instead
            would drop a search the developer can see they set. The caller
            reports the refusal and leaves the previous search alone.
         */
        public bool SetQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return false;
            }

            Query = query.Trim();
            CurrentMatch = null;
            MatchCount = 0;
            TotalCount = 0;
            return true;
        }

        public void Clear()
        {
            Query = null;
            CurrentMatch = null;
            MatchCount = 0;
            TotalCount = 0;
        }

        /*
            Copies the matching lines into the caller's array, oldest first,
            and returns how many it wrote. The destination is the view's own
            reused array, which grows with the window and never shrinks, so a
            search costs one pass over the window. The window itself is left
            alone: the log still holds every line when the search is cleared,
            and the next pass reads it again anyway.

            The position is clamped here rather than in the step, because the
            ring rotates between two steps. A match the developer was reading
            can be gone, and a position past the end of a shorter list would
            name a child that does not exist. A query set since the last pass
            has no position yet, and lands on the first match.
         */
        public int Apply(LogItem[] window, int count, LogItem[] destination)
        {
            if (window == null || destination == null)
            {
                return 0;
            }

            /*
                Bounded by the window, not trusted from the caller. The view
                sizes the destination to the window so the two never disagree,
                but a caller that got it wrong should get a truncated search
                rather than an index past the end of its own array.
             */
            int searchableEnd = Math.Min(count, window.Length);
            string query = Query;

            /*
                Two counters, one pass. `searchable` is the denominator the
                developer reads, and it is every line the search could have
                matched: an echo is excluded from the matches, so counting it
                here would report "20 of 61" beside a rule that says 61 lines
                were never candidates.

                The loop runs to the end of the window even once the
                destination is full, because a truncated `kept` with a complete
                `searchable` is the honest pair: the view drew what fitted, and
                the total still says how many lines there were. Stopping early
                would report the two from different sets.
             */
            int kept = 0;
            int searchable = 0;
            for (int i = 0; i < searchableEnd; ++i)
            {
                LogItem item = window[i];
                if (!IsSearchable(item))
                {
                    continue;
                }

                ++searchable;
                if (kept < destination.Length && Matches(item, query))
                {
                    destination[kept] = item;
                    ++kept;
                }
            }

            MatchCount = kept;
            TotalCount = searchable;
            if (kept < 1)
            {
                CurrentMatch = null;
            }
            else
            {
                CurrentMatch = Math.Min(CurrentMatch.GetValueOrDefault(1), kept);
            }

            return kept;
        }

        /* False means the window holds no match to step to. */
        public bool StepForward()
        {
            return Step(1);
        }

        public bool StepBackward()
        {
            return Step(-1);
        }

        /*
            The next match, wrapping at both ends. The matches are a set, not
            a document, and a search that stopped at the last one would need the
            developer to already know how many there were to come back to the
            first.

            A step taken before a position exists lands on the first match in
            the requested direction, so the first step of a new search is not a
            jump past the hit the developer is reading.
         */
        private bool Step(int delta)
        {
            if (MatchCount <= 0)
            {
                return false;
            }

            int next = CurrentMatch.GetValueOrDefault() + delta;
            if (next < 1)
            {
                next = MatchCount;
            }
            else if (MatchCount < next)
            {
                next = 1;
            }

            CurrentMatch = next;
            return true;
        }
    }
}
