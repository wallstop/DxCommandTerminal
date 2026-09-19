namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using Backend;
    using NUnit.Framework;

    /*
        Pins CommandLog.ReduceStackTrace byte-for-byte against the Split +
        Join reference implementation it replaced, over a corpus covering
        the separator shapes and package-marker skip rules. The reference
        is the pre-optimization algorithm kept here verbatim; a failure
        means the single-pass walk diverged from it.
    */
    public sealed class CommandLogTests
    {
        private const string Marker = "WallstopStudios.DxCommandTerminal";

        private static readonly string[] NewlineSeparators = { "\r\n", "\n", "\r" };

        private static readonly string JoinSeparator = Environment.NewLine;

        private CommandLog _log;

        private static IEnumerable<TestCaseData> Corpus()
        {
            yield return new TestCaseData(null).SetName("Corpus.Null");
            yield return new TestCaseData("").SetName("Corpus.Empty");
            yield return new TestCaseData(" ").SetName("Corpus.Whitespace");
            yield return new TestCaseData("UnityEngine.Object:Nothing()").SetName(
                "Corpus.SingleLineOnly"
            );
            yield return new TestCaseData(
                "UnityEngine.Object:Nothing()\n"
                    + $"{Marker}.CommandLog:HandleLog()\n"
                    + $"{Marker}.Terminal:Log()\n"
                    + "GamePlay:Update()"
            ).SetName("Corpus.SkipsCallerAndPackageLines");
            yield return new TestCaseData("A\r\n" + $"{Marker}.X()\r\n" + "B").SetName(
                "Corpus.CrlfSeparators"
            );
            yield return new TestCaseData("A\r" + $"{Marker}.X()\r" + "B").SetName(
                "Corpus.CrOnlySeparators"
            );
            yield return new TestCaseData("A\n" + $"{Marker}.X()\n" + "B\n").SetName(
                "Corpus.TrailingSeparatorKeepsFinalEmptyLine"
            );
            yield return new TestCaseData("A\n\n" + $"{Marker}.X()\n\n" + "B").SetName(
                "Corpus.EmptyLinesBetweenKeptLinesArePreserved"
            );
            yield return new TestCaseData("A\n" + $"{Marker}.X()\n" + "").SetName(
                "Corpus.TrailingEmptyPackageLineDropsToEmpty"
            );
            yield return new TestCaseData("Unity()\n" + $"{Marker}.X()").SetName(
                "Corpus.AllPackageLinesReduceToEmpty"
            );
            yield return new TestCaseData($"{Marker}.X()\n" + "B").SetName(
                "Corpus.MarkerInLineZeroIsUnconditionallyDropped"
            );
            yield return new TestCaseData("A\nfoo " + Marker + " bar\nB").SetName(
                "Corpus.MarkerMidLineStillSkips"
            );
            yield return new TestCaseData("A\nwallstopstudios.dxcommandterminal x\nB").SetName(
                "Corpus.MarkerMatchIsCaseInsensitive"
            );
            yield return new TestCaseData("A\n" + Marker + "\nB\n" + Marker + "\nC").SetName(
                "Corpus.NonContiguousPackageLinesAllDrop"
            );
            yield return new TestCaseData("A\r\n\nB").SetName("Corpus.EmptyLineAfterCrlfIsKept");
        }

        /*
            The pre-optimization algorithm, kept verbatim: split on CRLF, LF,
            or CR, drop line 0, keep dropping while lines carry the package
            marker (case-insensitively, matching the original Contains
            overload), then join the rest with Environment.NewLine.
         */
        private static string ReduceReference(string fullStackTrace)
        {
            if (string.IsNullOrWhiteSpace(fullStackTrace))
            {
                return fullStackTrace;
            }

            string[] lines = fullStackTrace.Split(NewlineSeparators, StringSplitOptions.None);

            int startIndex = 1;
            while (
                startIndex < lines.Length
                && 0 <= lines[startIndex].IndexOf(Marker, StringComparison.OrdinalIgnoreCase)
            )
            {
                ++startIndex;
            }

            return lines.Length <= startIndex
                ? string.Empty
                : string.Join(JoinSeparator, lines, startIndex, lines.Length - startIndex);
        }

        private static IEnumerable<TestCaseData> StackTraceModeCases()
        {
            /*
                Rows: (mode, type, expected trace capture). Diagnostic types
                capture in every mode except Disabled; routine types capture only
                under All. Both entry points (extraction and caller-supplied
                traces) enforce the same rule.
             */
            yield return new TestCaseData(
                TerminalStackTraceMode.All,
                TerminalLogType.ShellMessage,
                true
            ).SetName("Mode.All.CapturesRoutine");
            yield return new TestCaseData(
                TerminalStackTraceMode.All,
                TerminalLogType.Error,
                true
            ).SetName("Mode.All.CapturesError");
            yield return new TestCaseData(
                (TerminalStackTraceMode)0,
                TerminalLogType.ShellMessage,
                true
            ).SetName("Mode.StaleZero.CapturesLikeAll.Routine");
            yield return new TestCaseData(
                (TerminalStackTraceMode)0,
                TerminalLogType.Error,
                true
            ).SetName("Mode.StaleZero.CapturesLikeAll.Error");
            yield return new TestCaseData(
                TerminalStackTraceMode.ErrorsAndWarnings,
                TerminalLogType.ShellMessage,
                false
            ).SetName("Mode.ErrorsAndWarnings.SkipsRoutine");
            yield return new TestCaseData(
                TerminalStackTraceMode.ErrorsAndWarnings,
                TerminalLogType.Input,
                false
            ).SetName("Mode.ErrorsAndWarnings.SkipsInput");
            yield return new TestCaseData(
                TerminalStackTraceMode.ErrorsAndWarnings,
                TerminalLogType.Message,
                false
            ).SetName("Mode.ErrorsAndWarnings.SkipsMessage");
            yield return new TestCaseData(
                TerminalStackTraceMode.ErrorsAndWarnings,
                TerminalLogType.Error,
                true
            ).SetName("Mode.ErrorsAndWarnings.CapturesError");
            yield return new TestCaseData(
                TerminalStackTraceMode.ErrorsAndWarnings,
                TerminalLogType.Warning,
                true
            ).SetName("Mode.ErrorsAndWarnings.CapturesWarning");
            yield return new TestCaseData(
                TerminalStackTraceMode.ErrorsAndWarnings,
                TerminalLogType.Assert,
                true
            ).SetName("Mode.ErrorsAndWarnings.CapturesAssert");
            yield return new TestCaseData(
                TerminalStackTraceMode.ErrorsAndWarnings,
                TerminalLogType.Exception,
                true
            ).SetName("Mode.ErrorsAndWarnings.CapturesException");
            yield return new TestCaseData(
                TerminalStackTraceMode.Disabled,
                TerminalLogType.Error,
                false
            ).SetName("Mode.Disabled.SkipsError");
            yield return new TestCaseData(
                TerminalStackTraceMode.Disabled,
                TerminalLogType.ShellMessage,
                false
            ).SetName("Mode.Disabled.SkipsRoutine");
        }

        [SetUp]
        public void SetUp()
        {
            _log = new CommandLog(16);
        }

        [TestCaseSource(nameof(Corpus))]
        public void ReduceMatchesSplitJoinReference(string fullStackTrace)
        {
            string actual = _log.ReduceStackTrace(fullStackTrace);
            string expected = ReduceReference(fullStackTrace);
            Assert.AreEqual(expected, actual, $"Input: {fullStackTrace ?? "<null>"}");
        }

        [Test]
        public void ReducePassesThroughNullLikeInputs()
        {
            Assert.AreEqual(null, _log.ReduceStackTrace(null));
            Assert.AreEqual(string.Empty, _log.ReduceStackTrace(string.Empty));
            Assert.AreEqual(" ", _log.ReduceStackTrace(" "));
        }

        [TestCaseSource(nameof(StackTraceModeCases))]
        public void CaptureModeRulesByType(
            TerminalStackTraceMode mode,
            TerminalLogType type,
            bool expectedCapture
        )
        {
            CommandLog log = new(16) { stackTraceMode = mode };

            Assert.IsTrue(
                log.HandleLog("extraction", type),
                "Sanity: the extraction write must land"
            );
            LogItem extracted = log.Logs[log.Logs.Count - 1];
            Assert.AreEqual(
                expectedCapture,
                !string.IsNullOrEmpty(extracted.stackTrace),
                "Extraction-path capture must follow the mode rules"
            );

            log.HandleLog("supplied", "frame-at-call-site", type);
            LogItem supplied = log.Logs[log.Logs.Count - 1];
            Assert.AreEqual(
                expectedCapture ? "frame-at-call-site" : string.Empty,
                supplied.stackTrace,
                "Caller-supplied traces must follow the same mode rules"
            );
        }

        [Test]
        public void DefaultStackTraceModeIsAll()
        {
            Assert.AreEqual(
                TerminalStackTraceMode.All,
                _log.stackTraceMode,
                "Buffers created without an explicit mode must keep the capture-all behavior"
            );
            _log.HandleLog("traced", TerminalLogType.ShellMessage);
            Assert.AreNotEqual(
                string.Empty,
                _log.Logs[_log.Logs.Count - 1].stackTrace,
                "Default buffers must capture traces exactly as before"
            );
        }
    }
}
