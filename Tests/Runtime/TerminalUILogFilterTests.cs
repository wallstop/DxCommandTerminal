namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Globalization;
    using System.Text;
    using Backend;
    using Components;
    using Input;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        A search over the log view: `find` hides the lines that do not match,
        F3 steps through the ones that do, and `clear-filter` puts the log
        back.

        The decision itself is engine-independent and lives in LogFilter and
        LogFindKeys; this suite is the wiring - that the command reaches the
        view, that the view draws only the matches, and that the key the
        command line holds focus for arrives where it is routed.

        The drawn lines are read straight off the view's children, so most of
        this suite answers on a host that lays out no UI at all. What it cannot
        answer is the scroll to the match: a child's position is a layout
        result, so the jump tests skip with the reason rather than asserting
        into geometry the host never produced.
     */
    public sealed class TerminalUILogFilterTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const int FrameBudget = 300;
        private const float Tolerance = 0.5f;

        /*
            Sized so the FILTERED log overflows the view, not just the whole
            one. The two jump tests assert on a scroll offset, and a search over
            20 matches leaves 20 lines in a log view far taller than that - so
            the scroller would hold no range at all, the layout guard would
            blame the environment, and the jump would never be measured even on
            a host with a rendered view. 240 lines at one match in three leaves
            80 to scroll through.

            Under the default 256-entry buffer on purpose. Past it the ring
            rotates and the oldest lines - matches included - are gone, so the
            counts these tests assert would depend on the fill order rather
            than on the rule.
         */
        private const int FillLines = 240;
        private const string MissMarker = "routine frame";
        private const string HitMarker = "the hit";

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private static T LoadAsset<T>(string relativePath)
            where T : ScriptableObject
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
        }

        /*
            Three frames, which is what a write this frame needs before a read
            can mean anything: the command's own log line is drawn by the next
            pass, and the pass after that is the first one that can see the
            result of it.
         */
        private static IEnumerator Settle()
        {
            yield return null;
            yield return null;
            yield return null;
        }

        private static IEnumerator FillTheLog()
        {
            /*
                Cleared first: the log buffer is the session's, so a previous
                test's lines would be counted as the search's, and these tests
                assert exact numbers.
             */
            Terminal.Buffer?.Clear();

            for (int line = 1; line <= FillLines; ++line)
            {
                bool hit = line % 3 == 0;
                Terminal.Log(
                    hit
                        ? $"line {line.ToString(CultureInfo.InvariantCulture)} with {HitMarker} in it"
                        : $"line {line.ToString(CultureInfo.InvariantCulture)} {MissMarker}"
                );
            }

            yield return null;
            yield return null;
            yield return null;
        }

        /*
            Runs the command the way the console runs it: the echo into the log
            first, then the shell. The echo is the newest entry the log holds
            while a handler reads it, and a suite that skipped it would answer
            questions the product never sees.
         */
        private static IEnumerator RunCommand(string line)
        {
            Terminal.Log(TerminalLogType.Input, line);
            Terminal.Shell.RunCommand(line);
            yield return null;
            yield return null;
        }

        [SetUp]
        public void SetUp()
        {
            DefaultTerminalInput.Instance.CommandText = string.Empty;
        }

        [TearDown]
        public void TearDown()
        {
            DefaultTerminalInput.Instance.CommandText = string.Empty;
            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator FindHidesEveryLineThatDoesNotMatch()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForDrawnCount(FillLines, "The log draws every line before a search");

            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "A search draws the line it found");

            Assert.That(
                DrawnText(),
                Does.Not.Contain(MissMarker),
                "A line that does not hold the text is not what the developer asked to see"
            );
        }

        [UnityTest]
        public IEnumerator ClearFilterPutsEveryLineBack()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForDrawnCount(FillLines, "The log draws every line before a search");

            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");
            yield return RunCommand("clear-filter");
            yield return WaitForDrawnText(
                MissMarker,
                "Clearing a search brings back the lines it hid, including the newest"
            );
        }

        [UnityTest]
        public IEnumerator AQuotedQueryReachesTheSearchWhole()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();

            /*
                The search is over the text as the log shows it, so the query
                has to reach it the same way any other multi-word argument
                would. A query the tokenizer split finds nothing and says so,
                which is the failure this pins: the developer asked for a
                phrase and got a search for its first word.
             */
            yield return RunCommand("find \"" + HitMarker + "\"");
            yield return WaitForLastLogLine(
                "of 240 log lines",
                "A quoted query is one string, so the phrase is what was searched"
            );
        }

        [UnityTest]
        public IEnumerator FindReportsHowManyLinesItFound()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();

            yield return RunCommand("find " + HitMarker);
            yield return WaitForLastLogLine(
                "of 240 log lines",
                "The count is the answer to whether the search hit, which a view showing some lines cannot give"
            );
        }

        [UnityTest]
        public IEnumerator FindWithNoMatchSaysSoAndHidesEverything()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return WaitForDrawnCount(FillLines, "The log draws every line before a search");

            yield return RunCommand("find nothing-holds-this-text");
            yield return WaitForLastLogLine(
                "No log line matches",
                "A search that hit nothing says so"
            );

            yield return Settle();
            Assert.That(
                DrawnText(),
                Is.Empty,
                "A search with no match shows nothing rather than the log it did not find in"
            );
        }

        [UnityTest]
        public IEnumerator ARefusedQueryLeavesTheSearchAlone()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");

            yield return RunCommand("find \"\"");
            yield return WaitForLastLogLine(
                "Nothing to search for",
                "An empty query cannot be a search, and cannot silently drop the one that is set"
            );

            yield return Settle();
            Assert.That(
                DrawnText(),
                Does.Contain("the hit"),
                "The search the developer set survives a query that was not one"
            );
        }

        [UnityTest]
        public IEnumerator FindWithNoArgumentStepsToTheNextMatch()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");

            yield return RunCommand("find");
            yield return WaitForLastLogLine(
                "Match 2 of 80",
                "A repeat steps to the next match, and reports which one"
            );
        }

        [UnityTest]
        public IEnumerator SteppingWithNoSearchSaysSoInsteadOfSilentlyDoingNothing()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();

            yield return RunCommand("find");
            yield return WaitForLastLogLine(
                "No search is set",
                "A command that reached the console and did nothing says so"
            );
        }

        [UnityTest]
        public IEnumerator F3StepsTheSearchTheCommandLineHoldsFocusFor()
        {
            yield return SpawnOpenTerminal();
            yield return RequireReachableKeys();
            yield return FillTheLog();
            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");

            yield return SendKey(KeyCode.F3);
            yield return WaitForLastLogLine(
                "Match 2 of 80",
                "The key the field holds focus for reaches the search"
            );
        }

        [UnityTest]
        public IEnumerator F3WithNoSearchIsLeftToTheCommandLine()
        {
            /*
                A negative test that cannot fail is not a negative test, and the
                obvious version of this one is exactly that: clear the search,
                press F3, and assert the log is still whole - which it is
                whether or not F3 was ever routed. So the key is pressed while
                a search is set, where a router that answered it must move the
                view, and the whole log is only the thing to compare against
                once the search is dropped.

                The step is what makes it falsifiable: if F3 were consumed
                without stepping, the position would stay at the match the
                search set and the view would not move either. So the order is
                step, then clear, and the assertion is that clearing is what
                restored the log.
             */
            yield return SpawnOpenTerminal();
            yield return RequireReachableKeys();
            yield return FillTheLog();
            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");

            yield return SendKey(KeyCode.F3);
            yield return WaitForLastLogLine(
                "Match 2 of 80",
                "F3 moved the search while one was set"
            );

            yield return RunCommand("clear-filter");
            yield return WaitForDrawnText(
                MissMarker,
                "Clearing the search brought the rest of the log back"
            );

            yield return SendKey(KeyCode.F3);
            yield return Settle();

            Assert.That(
                DrawnText(),
                Does.Contain(MissMarker),
                "A key with no search set is left to the field, which does nothing with it either"
            );
        }

        [UnityTest]
        public IEnumerator TheSearchSurvivesANewLineThatDoesNotMatch()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");

            Terminal.Log("output that arrived after the search and does not match");
            yield return Settle();

            Assert.That(
                DrawnText(),
                Does.Contain("the hit"),
                "New output does not end a search the developer is reading"
            );
            Assert.That(
                DrawnText(),
                Does.Not.Contain("arrived after the search"),
                "A line that does not match stays out of the view while the search is set"
            );
        }

        [UnityTest]
        public IEnumerator ANewLineThatMatchesStaysInTheSearch()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");

            Terminal.Log("a later line with " + HitMarker + " in it");
            yield return WaitForDrawnText(
                "a later line with",
                "Output that matches joins the search"
            );
        }

        [UnityTest]
        public IEnumerator TheReportedCountIsTheSameEveryTimeTheSameSearchRuns()
        {
            /*
                The finding this pins, and the second half of the same one. The
                search's own answer was excluded from the matches but still
                counted in the total, so every run added one to the
                denominator: "20 of 240", then "20 of 241", then "20 of 242".
                The matches held and the number the developer reads did not,
                and a count that moves when nothing else did is the same
                failure as a count that never settles.

                Three runs, because the first is the one that would pass either
                way - the defect needs a second answer to count.
             */
            yield return SpawnOpenTerminal();
            yield return FillTheLog();

            yield return RunThroughConsole("find " + HitMarker);
            yield return WaitForLastLogLine("of 240 log lines", "The first run reports a count");

            yield return RunThroughConsole("find " + HitMarker);
            yield return WaitForLastLogLine(
                "of 240 log lines",
                "The second run reports the same count, so the first answer is in neither half"
            );

            yield return RunThroughConsole("find " + HitMarker);
            yield return WaitForLastLogLine(
                "of 240 log lines",
                "The third run too, so neither answer moved the number"
            );
        }

        [UnityTest]
        public IEnumerator ASearchSurvivesTheCommandThatRanIt()
        {
            /*
                The finding this pins: `EnterCommand` re-attaches the tail and
                asks for a scroll to the end AFTER the handler returns, because
                running a command is a request for its output. A search handler
                queues a jump to its match, and the attach overwrote it - so
                the view went to the end and the jump either lost the race or
                spent its budget waiting for a layout that kept moving.

                Driven through `EnterCommand` rather than the shell, because the
                shell never re-attaches and so cannot see this at all.

                Asserted synchronously, in the frame `EnterCommand` returns,
                and that is not a shortcut. The jump is dropped once its budget
                runs out - four refreshes with no layout - so a test that
                waited a frame or two to look would find the flag cleared on a
                correct build and pass straight over a broken one. The decision
                is made inside that call and nowhere else, so this is the only
                moment it is observable - on any host, laid out or not, which
                is what a state no view can show has to be.
             */
            yield return SpawnOpenTerminal();
            yield return FillTheLog();

            DefaultTerminalInput.Instance.CommandText = "find " + HitMarker;
            _terminal.EnterCommand();

            Assert.That(
                _terminal.FindScrollQueued,
                Is.True,
                "The jump the search queued survived the run that queued it"
            );
            Assert.That(
                _terminal.WantsScrollToEnd,
                Is.False,
                "The run did not re-assert a scroll to the end over the search's jump"
            );
        }

        [UnityTest]
        public IEnumerator ASearchNeverCountsItsOwnAnswer()
        {
            /*
                The second finding. A search that hit nothing answers in the
                log, and the answer is ordinary text - so a query that happens
                to be a word in it, "search" here, matched the answer. The
                search reported a hit for a string that appears nowhere, and
                each repeat of the search added another.

                The query is chosen to be a word the answer really contains, so
                this fails whenever the exclusion is removed, and it is the
                answer's own words that make it collide - not the query.
             */
            yield return SpawnOpenTerminal();
            yield return FillTheLog();

            yield return RunThroughConsole("find search");
            yield return WaitForLastLogLine(
                "No log line matches",
                "Nothing in the log holds the word the search looked for"
            );
            yield return Settle();

            Assert.That(
                DrawnText(),
                Is.Empty,
                "The answer to a search that hit nothing is not itself a result"
            );

            yield return RunThroughConsole("find");
            yield return Settle();

            Assert.That(
                LastLogText(),
                Does.Contain("No log line matches"),
                "A repeat of the search does not find the answer the first one wrote"
            );
        }

        [UnityTest]
        public IEnumerator ASearchScrollsToItsFirstMatch()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");

            yield return RequireLaidOutLog();

            Scroller scroller = LogScroller();
            float highValue = scroller.highValue;
            Assert.That(
                0f < highValue,
                Is.True,
                "The filtered log overflows the view, so the jump has somewhere to go"
            );
            Assert.That(
                scroller.value,
                Is.EqualTo(0f).Within(Tolerance),
                "A search lands on its first match, which is the top of the filtered log"
            );
        }

        [UnityTest]
        public IEnumerator SteppingScrollsToTheMatchItMovedTo()
        {
            yield return SpawnOpenTerminal();
            yield return FillTheLog();
            yield return RunCommand("find " + HitMarker);
            yield return WaitForDrawnText("the hit", "The search landed");

            yield return RequireLaidOutLog();

            Assert.That(
                0f < LogScroller().highValue,
                Is.True,
                "The filtered log overflows the view, so the step has somewhere to go"
            );

            yield return RunCommand("find");
            yield return WaitForLogScrolledToChild(
                1,
                "Stepping to the next match moves the view to it"
            );

            Assert.That(
                LogScroller().value,
                Is.GreaterThan(0f),
                "The second match is below the first, so the view left the top for it"
            );
        }

        /*
            Waits for the scroller to hold the offset of one drawn child, which
            is what a search jump writes: the line's own position in the
            content. Asserting "greater than zero" instead would pass on any
            scroll in either direction, including the scroll-to-end a run
            triggers - which is the thing the jump has to beat.
         */
        private IEnumerator WaitForLogScrolledToChild(int childIndex, string message)
        {
            Scroller scroller = LogScroller();
            int frameBudget = FrameBudget;
            while (0 < frameBudget--)
            {
                float expected = ExpectedScrollForChild(childIndex);
                if (0f < expected && Mathf.Abs(scroller.value - expected) < Tolerance)
                {
                    break;
                }

                yield return null;
            }

            Assert.That(
                scroller.value,
                Is.EqualTo(ExpectedScrollForChild(childIndex)).Within(Tolerance),
                message
            );
        }

        /*
            Where a search jump puts the view for a given drawn line, derived
            the way the jump derives it. Zero when the line has no layout yet,
            which is what keeps the poll from waiting on a surface its own
            assert does not need.
         */
        private float ExpectedScrollForChild(int childIndex)
        {
            VisualElement content = LogContent();
            if (childIndex < 0 || content.childCount <= childIndex)
            {
                return 0f;
            }

            return content[childIndex].layout.yMin;
        }

        private IEnumerator SpawnOpenTerminal()
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUILogFilter");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("The log filter needs the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);

            yield return null;
            yield return null;

            int frameBudget = 600;
            while (0 < frameBudget-- && _terminal._commandInput == null)
            {
                yield return null;
            }

            Assert.That(
                _terminal._commandInput,
                Is.Not.Null,
                "The terminal input field exists once it is open"
            );
            DefaultTerminalInput.Instance.CommandText = string.Empty;
        }

        private IEnumerator SendKey(KeyCode keyCode, EventModifiers modifiers = EventModifiers.None)
        {
            using (KeyDownEvent key = KeyDownEvent.GetPooled('\0', keyCode, modifiers))
            {
                /* Sent at the root, which is what makes the key travel down to
                   the field the way a real keystroke does: a panel routes a
                   key by focus, and this synthetic panel has none, so an event
                   aimed at the field itself is dropped. */
                _terminal._uiDocument.rootVisualElement.SendEvent(key);
            }

            yield return null;
            yield return null;
        }

        /*
            Whether a synthetic key can reach the command field on this host at
            all, measured rather than assumed.

            The F3 tests cannot be answered by a key that never arrives, and the
            obvious way to find that out is to assert the step and read a
            failure - which is indistinguishable from the routing being broken.
            So the field is asked directly, with a callback registered the same
            way the terminal registers its own: trickle-down, on the same
            element, from an event sent at the same root. A host that drops
            such an event cannot answer anything about key routing, and that
            is a host limit to report rather than a defect to hunt for.

            `TerminalUIPasteTests.PasteReachesTheCommandTextAsAUserEdit` fails on
            unmodified master on this host for this reason, measured both ways:
            the same synthetic Ctrl+V never reaches the same field.
         */
        private IEnumerator RequireReachableKeys()
        {
            bool reached = false;
            _terminal._commandInput.RegisterCallback<KeyDownEvent>(
                _ => reached = true,
                TrickleDown.TrickleDown
            );

            using (KeyDownEvent key = KeyDownEvent.GetPooled('\0', KeyCode.F3, EventModifiers.None))
            {
                _terminal._uiDocument.rootVisualElement.SendEvent(key);
            }

            yield return null;
            if (!reached)
            {
                Assert.Ignore(
                    "A synthetic key does not reach the command field in this environment, so "
                        + "nothing about key routing can be measured here: a key that never arrives "
                        + "looks exactly like routing that is broken. "
                        + "TerminalUIPasteTests.PasteReachesTheCommandTextAsAUserEdit fails on "
                        + "unmodified master on this host for the same reason. Run these where a "
                        + "panel routes keys."
                );
            }
        }

        private ScrollView LogView()
        {
            ScrollView logView =
                _terminal._uiDocument.rootVisualElement.Q("LogScrollView") as ScrollView;
            Assert.That(logView, Is.Not.Null, "The log scroll view exists on an open terminal");
            return logView;
        }

        /*
            The same command through the real funnel, `EnterCommand` and all.

            `EnterCommand` re-attaches the log tail after the handler returns,
            because running a command is a request for its output. That runs
            AFTER anything the handler set up, so a handler whose whole point
            is where the view ends up has to survive the code that called it -
            and only this path exercises that ordering. Every other test here
            dispatches through the shell, which never re-attaches, so none of
            them can see it.
         */
        private IEnumerator RunThroughConsole(string line)
        {
            DefaultTerminalInput.Instance.CommandText = line;
            _terminal.EnterCommand();
            yield return null;
            yield return null;
        }

        private VisualElement LogContent()
        {
            return LogView().contentContainer;
        }

        private Scroller LogScroller()
        {
            ScrollView logView = LogView();
            Assert.That(logView.verticalScroller, Is.Not.Null, "It exposes a vertical scroller");
            return logView.verticalScroller;
        }

        private int DrawnCount()
        {
            return LogContent().childCount;
        }

        private string DrawnText()
        {
            VisualElement content = LogContent();
            int childCount = content.childCount;
            StringBuilder builder = new();
            for (int i = 0; i < childCount; ++i)
            {
                if (0 < i)
                {
                    builder.Append('\n');
                }

                builder.Append((content[i] as Label)?.text);
            }

            return builder.ToString();
        }

        /*
            The newest log line, read through `CopyTo` rather than `Logs`. A
            count followed by an index is two reads, and a line that arrived
            between them would make the poll wait for text that is no longer
            the newest - which is a poll that can hang on a log that keeps
            logging. One consistent read is the whole window.
         */
        private string LastLogText()
        {
            CommandLog buffer = Terminal.Buffer;
            if (buffer == null)
            {
                return string.Empty;
            }

            LogItem[] window = new LogItem[Math.Max(buffer.Capacity, 2)];
            int count = buffer.CopyTo(window);
            return count < 1 ? string.Empty : window[count - 1].message;
        }

        private IEnumerator WaitForDrawnCount(int expected, string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && DrawnCount() != expected)
            {
                yield return null;
            }

            Assert.That(DrawnCount(), Is.EqualTo(expected), message);
        }

        private IEnumerator WaitForDrawnText(string expected, string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !DrawnText().Contains(expected, StringComparison.Ordinal))
            {
                yield return null;
            }

            Assert.That(DrawnText(), Does.Contain(expected), message);
        }

        private IEnumerator WaitForLastLogLine(string expected, string message)
        {
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && !LastLogText().Contains(expected, StringComparison.Ordinal))
            {
                yield return null;
            }

            Assert.That(LastLogText(), Does.Contain(expected), message);
        }

        /*
            The two jump tests need a child position, and a child position is a
            layout result. A headless editor lays out no UI Toolkit panel, so
            the filtered log has no geometry and the scroller has nothing to
            scroll; the other tests here read the drawn children, which exist
            without a layout, and answer on this host.
         */
        private IEnumerator RequireLaidOutLog()
        {
            /*
                The scroller's range AND the viewport's own height. The range is
                what the jump writes, but a view with a range and no height has
                a scroller over a content that was never given room, and a child
                in it has a position the product never showed - which would make
                the offset this file asserts against a number no developer ever
                saw. Both, held across frames, so a panel left half-built by an
                earlier test cannot satisfy either one for a frame and lose it.
             */
            int stableFrames = 0;
            int frameBudget = FrameBudget;
            while (0 < frameBudget-- && stableFrames < 5)
            {
                ScrollView view = LogView();
                bool laidOut =
                    0f < LogScroller().highValue && 0f < view.contentViewport.layout.height;
                stableFrames = laidOut ? stableFrames + 1 : 0;
                yield return null;
            }

            if (stableFrames < 5)
            {
                Assert.Ignore(
                    "The log view did not hold a layout in this environment, so a child has no "
                        + "position and the search cannot be seen to jump to its match. A headless "
                        + "editor with no rendered view cannot answer this; run it where the Game "
                        + "view renders."
                );
            }
        }
    }
}
