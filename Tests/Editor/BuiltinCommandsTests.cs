namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;

    /*
        The built-in commands, driven through a real shell.

        EditMode, because none of this needs a panel: the built-ins read and
        write the session's buffer and shell, both of which are settable here,
        and the clipboard is the same `GUIUtility.systemCopyBuffer` the paste
        direction reads. The ambient execution context is pinned so the
        dispatch is the same one a player gets rather than whatever the editor
        happens to be doing when the test runs.

        The three here fail for three different reasons, which is why they are
        three tests and not one:

        - `time` rejoined its arguments with single spaces, so a quoted
          argument arrived as several and the timed command was rejected
          before it ran.
        - Nothing could read the log out. The commands here write the window
          they name to the clipboard and say what happened, including on the
          platforms that have no clipboard to write to.
     */
    public sealed class BuiltinCommandsTests
    {
        private const int LogCapacity = 16;

        /* What `copy-log` separates its lines with, so a test splits the way
           the command joined rather than assuming a newline it never chose. */
        private static readonly string LogCopySeparator = Environment.NewLine;

        private CommandLog _originalBuffer;
        private CommandShell _originalShell;
        private CommandHistory _originalHistory;
        private CommandAutoComplete _originalAutoComplete;
        private Func<CommandExecutionContext> _originalAmbientProvider;
        private string _originalClipboard;
        private CommandLog _buffer;
        private CommandShell _shell;
        private CommandHistory _history;
        private readonly List<string> _recorded = new();

        private static bool Contains(LogItem[] window, string message)
        {
            for (int i = 0; i < window.Length; ++i)
            {
                if (string.Equals(window[i].message, message, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] Contents(CommandArg[] args)
        {
            string[] contents = new string[args.Length];
            for (int i = 0; i < args.Length; ++i)
            {
                contents[i] = args[i].contents;
            }

            return contents;
        }

        /*
            Asserts the clipboard's copied lines without putting any of them
            in a failure message. A run's failure output is a sink nobody
            reading this repo controls, and the host clipboard holds
            whatever the developer copied last - a token, a password - so a
            mismatch is reported per line with a length-plus-hash
            descriptor that leaks nothing. The count assert comes first, so
            a copy that never happened reads as a count failure over the
            host's clipboard instead of as element mismatches.
         */
        private static void AssertCopiedLines(string[] copied, string[] expected, string because)
        {
            Assert.That(
                copied.Length,
                Is.EqualTo(expected.Length),
                $"{because} (line count: {copied.Length}; clipboard: "
                    + $"{DescribeClipboard(string.Join(LogCopySeparator, copied))})"
            );
            for (int i = 0; i < expected.Length && i < copied.Length; ++i)
            {
                Assert.That(
                    string.Equals(copied[i], expected[i], StringComparison.Ordinal),
                    Is.True,
                    $"{because} (line {i}: {DescribeClipboard(copied[i])})"
                );
            }
        }

        /*
            Length plus an ordinal hash of the text: enough to diagnose a
            mismatch, and no substring of the clipboard ever reaches a
            failure message, NUnit XML, or a run-report claim.
         */
        private static string DescribeClipboard(string text)
        {
            if (text == null)
            {
                return "null";
            }

            if (text.Length == 0)
            {
                return "empty";
            }

            int hash = 17;
            for (int i = 0; i < text.Length; ++i)
            {
                hash = (hash * 31) + text[i];
            }

            return $"{text.Length} chars, hash {hash:X8}";
        }

        [SetUp]
        public void SetUp()
        {
            _originalBuffer = Terminal.Buffer;
            _originalShell = Terminal.Shell;
            _originalHistory = Terminal.History;
            _originalAutoComplete = Terminal.AutoComplete;
            _originalAmbientProvider = CommandExecutionContext.AmbientContextProvider;
            _originalClipboard = GUIUtility.systemCopyBuffer;

            /* A player-shaped context, so an editor-side run is not a
               context-eligibility test in disguise. */
            CommandExecutionContext.AmbientContextProvider = () =>
                new CommandExecutionContext(CommandExecutionContexts.Player);

            _buffer = new CommandLog(LogCapacity);
            _history = new CommandHistory(16);
            _shell = new CommandShell(_history);
            _shell.InitializeAutoRegisteredCommands();
            _shell.EnsureAutoCommandsRegistered();
            Terminal.Buffer = _buffer;
            Terminal.Shell = _shell;
            Terminal.History = _history;
            _recorded.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Terminal.Buffer = _originalBuffer;
            Terminal.Shell = _originalShell;
            Terminal.History = _originalHistory;
            Terminal.AutoComplete = _originalAutoComplete;
            CommandExecutionContext.AmbientContextProvider = _originalAmbientProvider;
            GUIUtility.systemCopyBuffer = _originalClipboard;
        }

        [Test]
        public void TimeRunsTheCommandItWasGivenWithItsQuotedArgumentIntact()
        {
            _shell.AddCommand(
                "record",
                args => _recorded.Add(string.Join("|", Contents(args))),
                minArgs: 0
            );

            string error = Run("time record \"two words\" plain");

            Assert.That(
                error,
                Is.Null,
                $"`time` must run the command it names, not reject its arguments: {error}"
            );
            Assert.That(
                _recorded,
                Is.EqualTo(new[] { "two words|plain" }),
                "`time` re-tokenized a flattened string, so a quoted argument split"
            );
        }

        [Test]
        public void TimeReachesTheCommandItNamesAtAll()
        {
            _shell.AddCommand("record", args => _recorded.Add("ran"), minArgs: 0);

            Run("time record");

            Assert.That(
                _recorded,
                Is.EqualTo(new[] { "ran" }),
                "`time` must invoke the command it names"
            );
        }

        [Test]
        public void TimeRejectsNoCommandTheSameWayItAlwaysDid()
        {
            string error = Run("time");

            Assert.That(
                error,
                Is.Not.Null,
                "A `time` with no command is still an argument-count failure"
            );
        }

        [Test]
        public void TimeStillSubstitutesAVariable()
        {
            /*
                The documented idiom: a value stored by `set-variable` and
                reached with `$name` on a later line. Substitution happens when
                the outer line is parsed, so the timed command has to see the
                value the same way it would have through the string path.
             */
            Run("set-variable greet \"Hello World!\"");

            string error = Run("time log-terminal $greet");

            Assert.That(error, Is.Null, $"`time` must run the substituted line: {error}");
            Assert.That(
                Contains(Newest(3), "Hello World!"),
                Is.True,
                "`time` must not hand the timed command the literal `$greet`"
            );
        }

        [Test]
        public void TimeStillPushesTheTimedCommandToHistoryExactlyOnce()
        {
            Run("time log-terminal once");

            string[] history = _history.GetHistory(true, true).ToArray();

            Assert.That(
                history,
                Does.Contain("time log-terminal once"),
                "The line the developer typed is in history"
            );
            Assert.That(
                history.Count(line =>
                    string.Equals(line, "log-terminal once", StringComparison.Ordinal)
                ),
                Is.EqualTo(1),
                "The timed command's own line reaches history once, through the same "
                    + "funnel the string path used, so Up recalls exactly what it did before"
            );
        }

        [Test]
        public void CopyLastPutsTheNewestLineOnTheClipboard()
        {
            Run("log-terminal first");
            Run("log-terminal second");
            Run("copy-last");

            string copied = GUIUtility.systemCopyBuffer;
            Assert.That(
                string.Equals(copied, "second", StringComparison.Ordinal),
                Is.True,
                "`copy-last` copies the newest line, which is the one above the echo "
                    + $"(clipboard: {DescribeClipboard(copied)})"
            );
            Assert.That(
                Contains(Newest(3), "Copied the most recent log line to the clipboard."),
                Is.True,
                "`copy-last` answers in the console like every other command"
            );
        }

        [Test]
        public void CopyLogPutsTheWholeWindowOnTheClipboard()
        {
            Run("log-terminal alpha");
            Run("log-terminal beta");
            Run("copy-log");

            string copied = GUIUtility.systemCopyBuffer;
            Assert.That(
                0 <= copied.IndexOf("alpha", StringComparison.Ordinal),
                Is.True,
                "`copy-log` with no count copies every buffered line "
                    + $"(clipboard: {DescribeClipboard(copied)})"
            );
            Assert.That(
                0 <= copied.IndexOf("beta", StringComparison.Ordinal),
                Is.True,
                "`copy-log` with no count copies every buffered line "
                    + $"(clipboard: {DescribeClipboard(copied)})"
            );
        }

        [TestCase("1")]
        [TestCase("3")]
        public void CopyLogTakesTheNewestCount(string count)
        {
            for (int i = 0; i < 5; ++i)
            {
                Run($"log-terminal line{i}");
            }

            Run($"copy-log {count}");

            string[] copied = GUIUtility.systemCopyBuffer.Split(LogCopySeparator);
            Assert.That(
                copied.Length,
                Is.EqualTo(int.Parse(count, CultureInfo.InvariantCulture)),
                $"`copy-log {count}` copies exactly {count} lines"
            );
            string newest = copied[copied.Length - 1];
            Assert.That(
                string.Equals(newest, "line4", StringComparison.Ordinal),
                Is.True,
                "The window is the newest lines, and its own end is the newest of them "
                    + $"(newest: {DescribeClipboard(newest)})"
            );
        }

        [Test]
        public void CopyLogRejectsACountItCannotRead()
        {
            string untouched = GUIUtility.systemCopyBuffer;
            Run("log-terminal alpha");
            Run("copy-log not-a-number");

            string after = GUIUtility.systemCopyBuffer;
            Assert.That(
                string.Equals(after, untouched, StringComparison.Ordinal),
                Is.True,
                "A count that does not parse must not fall through to copying everything "
                    + $"(clipboard: {DescribeClipboard(after)})"
            );
            Assert.That(
                Contains(Newest(2), "Invalid line count not-a-number."),
                Is.True,
                "A count that does not parse is reported, not silently ignored"
            );
        }

        [Test]
        public void CopyLogRejectsAZeroCount()
        {
            string untouched = GUIUtility.systemCopyBuffer;
            Run("log-terminal alpha");
            Run("copy-log 0");

            string after = GUIUtility.systemCopyBuffer;
            Assert.That(
                string.Equals(after, untouched, StringComparison.Ordinal),
                Is.True,
                "`copy-log 0` copies nothing rather than everything "
                    + $"(clipboard: {DescribeClipboard(after)})"
            );
            Assert.That(
                Contains(Newest(2), "Line count must be at least 1."),
                Is.True,
                "A zero count is reported"
            );
        }

        [Test]
        public void CopyLastTakesTheLineAboveTheEchoOfTheCommandAskingForIt()
        {
            /*
                The bug this file's `Run` helper hid. The console echoes the
                typed line as `Input` before any handler runs, so from a real
                console the newest entry is "copy-last" itself, and copying the
                newest entry copied the word back at the developer instead of
                the error they were reaching for.
             */
            Run("log-terminal the error line");
            Run("copy-last");

            string copied = GUIUtility.systemCopyBuffer;
            Assert.That(
                string.Equals(copied, "the error line", StringComparison.Ordinal),
                Is.True,
                "`copy-last` copies the line above its own echo, not the echo "
                    + $"(clipboard: {DescribeClipboard(copied)})"
            );
        }

        [Test]
        public void CopyLogDoesNotPutTheCommandAskingForItOnTheClipboard()
        {
            Run("log-terminal alpha");
            Run("log-terminal beta");
            Run("copy-log 2");

            string[] copied = GUIUtility.systemCopyBuffer.Split(LogCopySeparator);

            AssertCopiedLines(
                copied,
                new[] { "log-terminal beta", "beta" },
                "`copy-log 2` takes the two entries above the echo, and they are the ones the developer saw last"
            );
        }

        [Test]
        public void CopyLogKeepsTheCommandsYouRanInTheTranscript()
        {
            /*
                The other half of the rule, so it is not "drop every echo". A
                bug report wants to show what was typed as much as what came
                back, so only the echoes at the newest end are the ones the
                developer has already in front of them.
             */
            Run("log-terminal alpha");
            Run("log-terminal beta");
            Run("copy-log");

            string copied = GUIUtility.systemCopyBuffer;
            Assert.That(
                0 <= copied.IndexOf("log-terminal alpha", StringComparison.Ordinal),
                Is.True,
                "An earlier command echo is part of the log and stays in the transcript "
                    + $"(clipboard: {DescribeClipboard(copied)})"
            );
        }

        [Test]
        public void CopyingAnEmptyLogReportsNothingToCopy()
        {
            /* Dispatched rather than echoed, so the log really is empty:
               `Run` writes the typed line into it before dispatching. */
            _shell.RunCommand("copy-last");

            Assert.That(
                Contains(Newest(1), "Nothing to copy: the log is empty."),
                Is.True,
                "An empty log is a report, not a silent no-op"
            );
        }

        [Test]
        public void ALogOfEmptyLinesIsNotReportedAsAClipboardRefusal()
        {
            /*
                The distinction the whole report exists to make. A log whose
                lines are empty has nothing to copy, which is not the same
                failure as a platform that declined a copy, and answering the
                second for the first sends a developer looking at their OS.

                Two lines, because the separators are written whatever the lines
                say: two empty lines still join to one newline, so asking
                whether the copied text was empty called this a successful copy
                of a single character.
             */
            _buffer.HandleLog(string.Empty, TerminalLogType.Message);
            _buffer.HandleLog(string.Empty, TerminalLogType.Message);
            string untouched = GUIUtility.systemCopyBuffer;

            Run("copy-log 2");

            Assert.That(
                Contains(Newest(2), "Nothing to copy: the log holds no text."),
                Is.True,
                "Empty lines are not a platform that refused the copy"
            );
            Assert.That(
                Contains(Newest(2), "did not keep the text"),
                Is.False,
                "The refusal message is reserved for a platform that declined"
            );
            string after = GUIUtility.systemCopyBuffer;
            Assert.That(
                string.Equals(after, untouched, StringComparison.Ordinal),
                Is.True,
                $"A refused copy leaves the clipboard alone (clipboard: {DescribeClipboard(after)})"
            );
        }

        [Test]
        public void AnEmptyLineAmongRealOnesIsStillCopied()
        {
            Run("log-terminal alpha");
            _buffer.HandleLog(string.Empty, TerminalLogType.Message);
            Run("log-terminal beta");
            Run("copy-log 3");

            string[] copied = GUIUtility.systemCopyBuffer.Split(LogCopySeparator);

            AssertCopiedLines(
                copied,
                new[] { string.Empty, "log-terminal beta", "beta" },
                "The three entries above the echo, blank one included"
            );
        }

        [Test]
        public void ALogOfNothingButCommandsSaysSoRatherThanCopyingOne()
        {
            Run("no-op");
            Run("copy-last");

            Assert.That(
                Contains(Newest(2), "Nothing to copy: the log holds no output, only commands."),
                Is.True,
                "The only entries are command echoes, and copying one would hand the developer their own keystroke"
            );
        }

        [Test]
        public void CopyLastTakesTheMessageAndNotTheTrace()
        {
            _buffer.HandleLog("boom", "at Frame", TerminalLogType.Error);
            Run("copy-last");

            string copied = GUIUtility.systemCopyBuffer;
            Assert.That(
                string.Equals(copied, "boom", StringComparison.Ordinal),
                Is.True,
                "A copied line is the line the developer sees; `trace` is how they get a trace "
                    + $"(clipboard: {DescribeClipboard(copied)})"
            );
        }

        /*
            Runs a line the way the console does: the typed line is echoed into
            the log as `Input` first, then dispatched. The echo is not a detail
            of the UI - `EnterCommand` and the palette both write it before any
            handler runs - and a helper that skipped it exercised a path the
            product never takes. `copy-last` was broken for exactly this
            reason: with no echo in the window, the text it copied looked
            right, and from the console it copied the word "copy-last".
         */
        private string Run(string line)
        {
            Terminal.Log(TerminalLogType.Input, line);
            _shell.RunCommand(line);
            return _shell.TryConsumeErrorMessage(out string error) ? error : null;
        }

        /* Newest first, so an assertion can name the entry it means without
           counting back through the window itself. */
        private LogItem[] Newest(int count)
        {
            LogItem[] window = new LogItem[LogCapacity];
            int written = _buffer.CopyTo(window);
            LogItem[] newest = new LogItem[count];
            for (int i = 0; i < count; ++i)
            {
                newest[count - 1 - i] = window[written - 1 - i];
            }

            return newest;
        }
    }
}
