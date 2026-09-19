namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Backend;
    using NUnit.Framework;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

    /*
        Standard-operations timing baselines for issue #108 (performance and
        allocation campaign): backend startup, typing completion, command
        execution, logging, history traversal, and provider completion. The
        Measures... methods log evidence under [DxCommandTerminal][Scale]
        and pin no budgets. The ...StaysUnderTripwire methods are generous
        regression tripwires sized from this suite's own [DxCommandTerminal]
        [Scale] evidence on the pinned local editor (Unity 6000.4.6f1); they
        are environment-specific and are not the plan's numeric gates. All
        windows are warmed steady state. Allocation claims live in the
        allocation suite, never here.
    */
    public sealed class StandardOperationsBenchmarkTests
    {
        private const int WarmupIterations = 30;
        private const int DefaultSampleCount = 300;
        private const int StressSampleCount = 50;

        private const int HistoryCapacity = 256;
        private const int LogCapacity = 64;
        private const int SelectiveMatchCount = 100;
        private const int ProviderCandidateCount = 1000;

        /*
            Tripwires sized from the first recorded run (Unity 6000.4.6f1,
            maintainer editor, 2026-09-19). Measured p95s land one to three
            orders of magnitude below each budget (see the progress session
            log for the raw series), so the gates guard against order-of-
            magnitude regressions, not noise.
        */
        private const float TypingTripwireMilliseconds = 2f;
        private const float ExecutionTripwireMilliseconds = 1f;
        private const float ProviderCompletionTripwireMilliseconds = 2f;
        private const float StartupReuseTripwireMilliseconds = 1f;

        private const string NoopCommandName = "bench-noop";
        private const string NoHistoryCommandName = "bench-nohist";
        private const string ProviderCommandName = "bench-complete";
        private const string TypedAllMatchPrefix = "bench";
        private const string TypedSelectivePrefix = "bench-cmd-00";

        private readonly List<string> _completionBuffer = new(ProviderCandidateCount);
        private readonly List<CommandCompletion> _providerResults = new(ProviderCandidateCount);
        private readonly List<string> _providerCandidates = new(ProviderCandidateCount);

        private CommandAutoComplete _autoComplete;
        private CommandHistory _history;
        private CommandLog _log;
        private CommandShell _shell;

        private static IEnumerable<TestCaseData> TypingCases()
        {
            yield return new TestCaseData(0, TypedAllMatchPrefix, DefaultSampleCount).SetName(
                "Tier.Ambient.AllMatch"
            );
            yield return new TestCaseData(100, TypedAllMatchPrefix, DefaultSampleCount).SetName(
                "Tier.Hundred.AllMatch"
            );
            yield return new TestCaseData(100, TypedSelectivePrefix, DefaultSampleCount).SetName(
                "Tier.Hundred.Selective"
            );
            yield return new TestCaseData(1000, TypedAllMatchPrefix, StressSampleCount).SetName(
                "Tier.Thousand.AllMatch"
            );
            yield return new TestCaseData(1000, TypedSelectivePrefix, DefaultSampleCount).SetName(
                "Tier.Thousand.Selective"
            );
        }

        private static IEnumerable<TestCaseData> ExecutionCases()
        {
            yield return new TestCaseData(true, DefaultSampleCount).SetName("History.On");
            yield return new TestCaseData(false, DefaultSampleCount).SetName("History.Off");
        }

        private static IEnumerable<TestCaseData> TraversalCases()
        {
            yield return new TestCaseData(100, DefaultSampleCount).SetName("Tier.Hundred");
            yield return new TestCaseData(1000, StressSampleCount).SetName("Tier.Thousand");
        }

        private static OperationReport Measure(Action subject, int sampleCount)
        {
            for (int i = 0; i < WarmupIterations; ++i)
            {
                subject();
            }

            long[] samples = new long[sampleCount];
            int gen0Start = GC.CollectionCount(0);
            for (int i = 0; i < sampleCount; ++i)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                subject();
                stopwatch.Stop();
                samples[i] = stopwatch.ElapsedTicks;
            }

            int gen0Collections = GC.CollectionCount(0) - gen0Start;
            long[] sorted = (long[])samples.Clone();
            Array.Sort(sorted);
            double ticksToMilliseconds = 1000.0 / Stopwatch.Frequency;
            return new OperationReport(
                sorted[sampleCount / 2] * ticksToMilliseconds,
                sorted[(int)Math.Ceiling(sampleCount * 0.95) - 1] * ticksToMilliseconds,
                sorted[sampleCount - 1] * ticksToMilliseconds,
                gen0Collections
            );
        }

        private static void LogScale(string operation, string detail, OperationReport report)
        {
            Debug.Log(
                $"[DxCommandTerminal][Scale] op={operation} {detail} "
                    + $"median={report.MedianMilliseconds:F3}ms "
                    + $"p95={report.Percentile95Milliseconds:F3}ms "
                    + $"max={report.MaximumMilliseconds:F3}ms gen0={report.Gen0Collections}"
            );
        }

        private static void HandleNoop(CommandArg[] arguments) { }

        [SetUp]
        public void SetUp()
        {
            _history = new CommandHistory(HistoryCapacity);
            _log = new CommandLog(LogCapacity);
            _shell = new CommandShell(_history);
            _autoComplete = new CommandAutoComplete(_history, _shell);
            _providerCandidates.Clear();
            for (int i = 0; i < ProviderCandidateCount; ++i)
            {
                _providerCandidates.Add($"candidate-{i:D4}");
            }
        }

        [Test]
        public void MeasuresBackendStartupCost()
        {
            TerminalSession.Config config = new(
                LogCapacity,
                HistoryCapacity,
                null,
                null,
                ignoreDefaultCommands: false
            );

            OperationReport create = Measure(
                () => new TerminalSession().Apply(config, force: false),
                DefaultSampleCount
            );
            TerminalSession reused = new();
            reused.Apply(config, force: false);
            OperationReport reuse = Measure(
                () => reused.Apply(config, force: false),
                DefaultSampleCount
            );

            Assert.That(reused.Shell != null, "Sanity: reuse must keep the backend alive");
            Assert.That(reused.AutoComplete != null, "Sanity: reuse must keep autocomplete");
            LogScale("startup-create", "backends=4", create);
            LogScale("startup-reuse", "backends=4", reuse);
        }

        [Test]
        public void MeasuresLogHandling()
        {
            FillLogToCapacity();
            OperationReport withStack = Measure(
                () => _log.HandleLog("bench message", TerminalLogType.ShellMessage),
                DefaultSampleCount
            );
            OperationReport withoutStack = Measure(
                () => _log.HandleLog("bench message", string.Empty, TerminalLogType.ShellMessage),
                DefaultSampleCount
            );

            Assert.AreEqual(
                LogCapacity,
                _log.Logs.Count,
                "Sanity: every measured write must land in the wrapped buffer"
            );
            LogScale("log-write", "stackTrace=true", withStack);
            LogScale("log-write", "stackTrace=false", withoutStack);
        }

        [TestCaseSource(nameof(ExecutionCases))]
        public void MeasuresCommandExecution(bool addToHistory, int sampleCount)
        {
            CreateBackends(10);
            string line = addToHistory ? "bench-cmd-0007 5" : $"{NoHistoryCommandName} 5";
            int historyBaseline = _history.Count;

            OperationReport report = Measure(() => _shell.RunCommand(line), sampleCount);

            Assert.IsTrue(
                _shell.Commands.ContainsKey("bench-cmd-0007"),
                "Sanity: the executed command must be registered"
            );
            if (addToHistory)
            {
                Assert.Less(
                    historyBaseline,
                    _history.Count,
                    "Sanity: history-on execution must push an entry"
                );
            }
            else
            {
                Assert.AreEqual(
                    historyBaseline,
                    _history.Count,
                    "Sanity: history-off execution must not push"
                );
            }

            LogScale("command-execution", $"history={addToHistory}", report);
        }

        [TestCaseSource(nameof(TraversalCases))]
        public void MeasuresHistoryTraversal(int entryCount, int sampleCount)
        {
            _history = new CommandHistory(entryCount);
            for (int i = 0; i < entryCount; ++i)
            {
                Assert.IsTrue(
                    _history.Push($"bench-entry-{i:D5}", true, true),
                    $"History should accept entry {i}"
                );
            }

            Assert.AreEqual(
                $"bench-entry-{entryCount - 1:D5}",
                _history.Previous(true),
                "Sanity: traversal must start at the newest entry"
            );

            /*
                The first sweep normalizes the traversal position; every later
                Previous x N / Next x N cycle returns to the same state, so
                the warmed samples measure one steady state.
             */
            Action sweep = () =>
            {
                for (int i = 0; i < entryCount; ++i)
                {
                    _history.Previous(true);
                }

                for (int i = 0; i < entryCount; ++i)
                {
                    _history.Next(true);
                }
            };

            OperationReport report = Measure(sweep, sampleCount);
            LogScale(
                "history-traversal",
                $"entries={entryCount} calls={entryCount * 2} "
                    + $"perCallUs={report.MedianMilliseconds * 1000.0 / (entryCount * 2):F3}",
                report
            );
        }

        [TestCaseSource(nameof(TypingCases))]
        public void MeasuresTypingCompletion(int commandCount, string token, int sampleCount)
        {
            CreateBackends(commandCount);

            OperationReport report = Measure(
                () => _autoComplete.Complete(token, _completionBuffer),
                sampleCount
            );

            int minimumMatches = Math.Min(commandCount, SelectiveMatchCount);
            Assert.LessOrEqual(
                minimumMatches,
                _completionBuffer.Count,
                $"Sanity: typed prefix '{token}' should match its tier commands"
            );
            LogScale(
                "typing-completion",
                $"commands={commandCount} token='{token}' matches={_completionBuffer.Count}",
                report
            );
        }

        [Test]
        public void MeasuresProviderCompletion()
        {
            CreateBackends(0);
            RegisterProviderCommand();

            string input = $"{ProviderCommandName} candidate-00";
            bool completed = _shell.TryComplete(
                CommandExecutionContext.Current,
                input,
                input.Length,
                _providerResults,
                out _
            );

            Assert.IsTrue(completed, "Sanity: the provider command must answer completion");
            Assert.AreEqual(
                SelectiveMatchCount,
                _providerResults.Count,
                "Sanity: the selective prefix must match its tier candidates"
            );

            OperationReport report = Measure(
                () =>
                    _shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        input.Length,
                        _providerResults,
                        out _
                    ),
                DefaultSampleCount
            );
            LogScale(
                "provider-completion",
                $"candidates={ProviderCandidateCount} matches={_providerResults.Count}",
                report
            );
        }

        [Test]
        public void TypingCompletionStaysUnderTripwire()
        {
            CreateBackends(1000);
            OperationReport report = Measure(
                () => _autoComplete.Complete(TypedSelectivePrefix, _completionBuffer),
                DefaultSampleCount
            );

            Assert.AreEqual(
                SelectiveMatchCount,
                _completionBuffer.Count,
                "Every selective-tier command should complete"
            );
            Assert.Less(
                report.Percentile95Milliseconds,
                TypingTripwireMilliseconds,
                $"Typing completion p95 exceeded the tripwire "
                    + $"({report.Percentile95Milliseconds:F3} ms >= {TypingTripwireMilliseconds} ms)"
            );
        }

        [Test]
        public void TextCommandExecutionStaysUnderTripwire()
        {
            CreateBackends(10);
            OperationReport report = Measure(
                () => _shell.RunCommand("bench-cmd-0007 5"),
                DefaultSampleCount
            );

            Assert.Less(0, _history.Count, "Sanity: the executed command must push history");
            Assert.Less(
                report.Percentile95Milliseconds,
                ExecutionTripwireMilliseconds,
                $"Text execution p95 exceeded the tripwire "
                    + $"({report.Percentile95Milliseconds:F3} ms >= {ExecutionTripwireMilliseconds} ms)"
            );
        }

        [Test]
        public void ProviderCompletionStaysUnderTripwire()
        {
            CreateBackends(0);
            RegisterProviderCommand();

            string input = $"{ProviderCommandName} candidate-00";
            OperationReport report = Measure(
                () =>
                    _shell.TryComplete(
                        CommandExecutionContext.Current,
                        input,
                        input.Length,
                        _providerResults,
                        out _
                    ),
                DefaultSampleCount
            );

            Assert.AreEqual(
                SelectiveMatchCount,
                _providerResults.Count,
                "Every selective-tier candidate should complete"
            );
            Assert.Less(
                report.Percentile95Milliseconds,
                ProviderCompletionTripwireMilliseconds,
                $"Provider completion p95 exceeded the tripwire "
                    + $"({report.Percentile95Milliseconds:F3} ms >= "
                    + $"{ProviderCompletionTripwireMilliseconds} ms)"
            );
        }

        [Test]
        public void BackendStartupReuseStaysUnderTripwire()
        {
            TerminalSession.Config config = new(
                LogCapacity,
                HistoryCapacity,
                null,
                null,
                ignoreDefaultCommands: false
            );
            TerminalSession reused = new();
            reused.Apply(config, force: false);

            OperationReport report = Measure(
                () => reused.Apply(config, force: false),
                DefaultSampleCount
            );

            Assert.Less(
                report.Percentile95Milliseconds,
                StartupReuseTripwireMilliseconds,
                $"Backend reuse p95 exceeded the tripwire "
                    + $"({report.Percentile95Milliseconds:F3} ms >= {StartupReuseTripwireMilliseconds} ms)"
            );
        }

        private void CreateBackends(int commandCount)
        {
            _completionBuffer.Clear();
            _providerResults.Clear();
            _shell.AddCommand(NoopCommandName, HandleNoop);
            _shell.AddCommand(NoHistoryCommandName, HandleNoop, 1, addToHistory: false);
            for (int i = 0; i < commandCount; ++i)
            {
                _shell.AddCommand($"bench-cmd-{i:D4}", HandleNoop, 1);
            }

            /*
                The first request pays deferred registration over the whole
                editor domain; warm it here so measured windows never include
                discovery, and silence the one-time readiness log line.
             */
            bool logsEnabled = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try
            {
                Assert.IsTrue(
                    _shell.RunCommand(NoopCommandName),
                    "The warmed no-op command must run"
                );
            }
            finally
            {
                Debug.unityLogger.logEnabled = logsEnabled;
            }
        }

        private void RegisterProviderCommand()
        {
            CommandBuilder builder = CommandBuilder
                .Create(ProviderCommandName)
                .Arg<string>(
                    "candidate",
                    spec => spec.Required().Choices(context => _providerCandidates, value => value)
                )
                .Handler((context, arguments) => { });
            Assert.IsTrue(
                _shell.AddCommand(builder, out _),
                "The provider command should register"
            );
        }

        private void FillLogToCapacity()
        {
            for (int i = 0; i < LogCapacity; ++i)
            {
                _log.HandleLog($"bench fill {i}", string.Empty, TerminalLogType.ShellMessage);
            }
        }

        private readonly struct OperationReport
        {
            public double MedianMilliseconds { get; }

            public double Percentile95Milliseconds { get; }

            public double MaximumMilliseconds { get; }

            public int Gen0Collections { get; }

            public OperationReport(
                double medianMilliseconds,
                double percentile95Milliseconds,
                double maximumMilliseconds,
                int gen0Collections
            )
            {
                MedianMilliseconds = medianMilliseconds;
                Percentile95Milliseconds = percentile95Milliseconds;
                MaximumMilliseconds = maximumMilliseconds;
                Gen0Collections = gen0Collections;
            }
        }
    }
}
