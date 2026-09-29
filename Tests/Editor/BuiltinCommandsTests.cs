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

            Assert.That(
                GUIUtility.systemCopyBuffer,
                Is.EqualTo("second"),
                "`copy-last` copies the newest line, which is the one above the echo"
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

            Assert.That(
                GUIUtility.systemCopyBuffer,
                Does.Contain("alpha"),
                "`copy-log` with no count copies every buffered line"
            );
            Assert.That(
                GUIUtility.systemCopyBuffer,
                Does.Contain("beta"),
                "`copy-log` with no count copies every buffered line"
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
            Assert.That(
                copied[copied.Length - 1],
                Is.EqualTo("line4"),
                "The window is the newest lines, and its own end is the newest of them"
            );
        }

        [Test]
        public void CopyLogRejectsACountItCannotRead()
        {
            string untouched = GUIUtility.systemCopyBuffer;
            Run("log-terminal alpha");
            Run("copy-log not-a-number");

            Assert.That(
                GUIUtility.systemCopyBuffer,
                Is.EqualTo(untouched),
                "A count that does not parse must not fall through to copying everything"
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

            Assert.That(
                GUIUtility.systemCopyBuffer,
                Is.EqualTo(untouched),
                "`copy-log 0` copies nothing rather than everything"
            );
            Assert.That(
                Contains(Newest(2), "Line count must be at least 1."),
                Is.True,
                "A zero count is reported"
            );
        }

        [Test]
        public void CopyingAnEmptyLogReportsNothingToCopy()
        {
            Run("copy-last");

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
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(untouched));
        }

        [Test]
        public void AnEmptyLineAmongRealOnesIsStillCopied()
        {
            Run("log-terminal alpha");
            _buffer.HandleLog(string.Empty, TerminalLogType.Message);
            Run("log-terminal beta");

            Run("copy-log 3");

            Assert.That(
                GUIUtility.systemCopyBuffer,
                Does.Contain("alpha").And.Contain("beta"),
                "One empty line is a line like any other; the count is what was asked for"
            );
        }

        [Test]
        public void CopyLastTakesTheMessageAndNotTheTrace()
        {
            _buffer.HandleLog("boom", "at Frame", TerminalLogType.Error);
            Run("copy-last");

            Assert.That(
                GUIUtility.systemCopyBuffer,
                Is.EqualTo("boom"),
                "A copied line is the line the developer sees; `trace` is how they get a trace"
            );
        }

        private string Run(string line)
        {
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
