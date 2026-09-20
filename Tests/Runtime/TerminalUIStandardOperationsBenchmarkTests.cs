namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Debug = UnityEngine.Debug;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        TerminalUI-level standard-operations timing baselines for issue #111
        (phase 3 of the #108 campaign): the per-keystroke input pipeline, Tab
        completion cycles, steady-state refresh passes, and the open/close +
        first-open tree build. Windows cover the sync package-owned work
        only: UITK raises its ChangeEvent and lands layout outside or astride
        these windows, and each row names what it includes. The Measures...
        methods log evidence under [DxCommandTerminal][Scale] and pin no
        budgets. The ...StaysUnderTripwire methods are generous regression
        tripwires sized from this suite's own recorded evidence on the pinned
        local editor (Unity 6000.4.6f1); they are environment-specific and
        are not the plan's numeric gates. All windows are warmed steady
        state. Allocation claims live in the allocation suite, never here.
        Tiers loop inside each UnityTest because the Unity Test Runner cannot
        parameterize UnityTest methods with TestCaseSource.
    */
    public sealed class TerminalUIStandardOperationsBenchmarkTests
    {
        private const int WarmupIterations = 30;
        private const int DefaultSampleCount = 300;
        private const int StressSampleCount = 50;

        /*
            Tripwires sized from the first recorded run (Unity 6000.4.6f1,
            maintainer editor, 2026-09-20). Measured p95s land one to three
            orders of magnitude below each budget (see the progress session
            log for the raw series), so the gates guard against order-of-
            magnitude regressions, not noise.
        */
        private const float KeystrokeTripwireMilliseconds = 2f;
        private const float SteadyRefreshTripwireMilliseconds = 1f;

        private const int SelectiveMatchCount = 100;
        private const int ProviderCandidateCount = 1000;
        private const int SteadyStateLogCount = 64;

        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const string NoopCommandName = "bench-noop";
        private const string NoHistoryCommandName = "bench-nohist";
        private const string ProviderCommandName = "bench-complete";
        private const string SingleMatchToken = "bench-cmd-0001";
        private const string SelectiveToken = "bench-cmd-00";

        private static readonly BenchmarkTier[] KeystrokeTiers =
        {
            new BenchmarkTier(0, SingleMatchToken, DefaultSampleCount),
            new BenchmarkTier(100, SelectiveToken, DefaultSampleCount),
            new BenchmarkTier(1000, SelectiveToken, DefaultSampleCount),
            new BenchmarkTier(1000, SingleMatchToken, DefaultSampleCount),
            new BenchmarkTier(10000, SelectiveToken, StressSampleCount),
            new BenchmarkTier(10000, SingleMatchToken, StressSampleCount),
        };

        private static readonly BenchmarkTier[] TabTiers =
        {
            new BenchmarkTier(0, SingleMatchToken, DefaultSampleCount),
            new BenchmarkTier(100, SelectiveToken, DefaultSampleCount),
            new BenchmarkTier(1000, SelectiveToken, DefaultSampleCount),
            new BenchmarkTier(10000, SelectiveToken, StressSampleCount),
        };

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

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

        private static T LoadAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.That(asset != null, $"Expected the test asset at {PackageRoot}/{relativePath}");
            return asset;
        }

        [TearDown]
        public void TearDown()
        {
            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }

            Terminal.Shell?.ClearCustomCommands();
        }

        [UnityTest]
        public IEnumerator MeasuresPerKeystrokeInputPipeline()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);

            foreach (BenchmarkTier tier in KeystrokeTiers)
            {
                PrepareTier(tier.Commands);

                /*
                    The field write is the real per-keystroke entry: UITK
                    raises its ChangeEvent synchronously (Unity-owned
                    allocation), the package callback syncs the input
                    abstraction and runs the hint sweep plus the
                    buffer-equivalence scan.
                 */
                _terminal.hintDisplayMode = HintDisplayMode.Always;
                _terminal._commandInput.value = tier.Token;
                yield return null;
                OperationReport sweep = Measure(() => _terminal.ResetAutoComplete(), tier.Samples);
                LogScale(
                    "ui-keystroke-sweep",
                    $"commands={tier.Commands} token='{tier.Token}' mode=Always",
                    sweep
                );

                /*
                    Empty the hint buffer so no player-loop frame
                    materializes a hint row per match between windows.
                 */
                ParkCompletionState();
                yield return null;

                string alternate = tier.Token + "x";
                int toggle = 0;
                _terminal.hintDisplayMode = HintDisplayMode.Always;
                OperationReport field = Measure(
                    () =>
                    {
                        toggle ^= 1;
                        _terminal._commandInput.value = toggle == 1 ? tier.Token : alternate;
                    },
                    tier.Samples
                );
                string finalValue = tier.Samples % 2 == 1 ? tier.Token : alternate;
                Assert.AreEqual(
                    finalValue,
                    _terminal._commandInput.value,
                    "Sanity: every measured keystroke must reach the field"
                );
                LogScale(
                    "ui-keystroke-field",
                    $"commands={tier.Commands} token='{tier.Token}' mode=Always",
                    field
                );
                ParkCompletionState();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator MeasuresTabCompletionLegacy()
        {
            yield return SpawnTerminal(HintDisplayMode.AutoCompleteOnly);

            foreach (BenchmarkTier tier in TabTiers)
            {
                PrepareTier(tier.Commands);

                _terminal._commandInput.value = tier.Token;
                yield return null;

                OperationReport report = Measure(
                    () => _terminal.CompleteCommand(true),
                    tier.Samples
                );
                Assert.That(
                    _terminal._commandInput.value.StartsWith(tier.Token, StringComparison.Ordinal),
                    "Sanity: the legacy cycle must keep the input on a prefix completion"
                );
                LogScale("ui-tab-legacy", $"commands={tier.Commands} token='{tier.Token}'", report);
                ParkCompletionState();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator MeasuresTabCompletionProvider()
        {
            yield return SpawnTerminal(HintDisplayMode.AutoCompleteOnly);

            foreach (BenchmarkTier tier in TabTiers)
            {
                PrepareTier(tier.Commands);
                RegisterProviderCommand();

                string input = $"{ProviderCommandName} candidate-00";
                _terminal._commandInput.value = input;
                yield return null;

                OperationReport report = Measure(
                    () => _terminal.CompleteCommand(true),
                    tier.Samples
                );
                Assert.That(
                    _terminal._commandInput.value.StartsWith(
                        ProviderCommandName,
                        StringComparison.Ordinal
                    ),
                    "Sanity: the provider cycle must apply token completions"
                );
                LogScale(
                    "ui-tab-provider",
                    $"commands={tier.Commands} candidates={ProviderCandidateCount}",
                    report
                );
                ParkCompletionState();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator PerKeystrokeInputPipelineStaysUnderTripwire()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            PrepareTier(1000);

            _terminal.hintDisplayMode = HintDisplayMode.Always;
            _terminal._commandInput.value = SelectiveToken;
            yield return null;

            string alternate = SelectiveToken + "x";
            int toggle = 0;
            OperationReport report = Measure(
                () =>
                {
                    toggle ^= 1;
                    _terminal._commandInput.value = toggle == 1 ? SelectiveToken : alternate;
                },
                DefaultSampleCount
            );

            Assert.AreEqual(
                alternate,
                _terminal._commandInput.value,
                "Sanity: every measured keystroke must reach the field"
            );
            Assert.Less(
                report.Percentile95Milliseconds,
                KeystrokeTripwireMilliseconds,
                $"Per-keystroke input p95 exceeded the tripwire "
                    + $"({report.Percentile95Milliseconds:F3} ms >= "
                    + $"{KeystrokeTripwireMilliseconds} ms)"
            );
        }

        [UnityTest]
        public IEnumerator SteadyRefreshPassStaysUnderTripwire()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            PrepareTier(1000);

            for (int i = 0; i < SteadyStateLogCount; ++i)
            {
                Terminal.Buffer.HandleLog($"bench log {i}", string.Empty, TerminalLogType.Message);
            }

            _terminal._commandInput.value = SelectiveToken;
            yield return null;
            yield return null;

            OperationReport report = Measure(() => _terminal.RefreshUI(), DefaultSampleCount);
            Assert.Less(
                report.Percentile95Milliseconds,
                SteadyRefreshTripwireMilliseconds,
                $"Steady refresh p95 exceeded the tripwire "
                    + $"({report.Percentile95Milliseconds:F3} ms >= "
                    + $"{SteadyRefreshTripwireMilliseconds} ms)"
            );
        }

        [UnityTest]
        public IEnumerator MeasuresSteadyStateRefresh()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            PrepareTier(1000);

            for (int i = 0; i < SteadyStateLogCount; ++i)
            {
                Terminal.Buffer.HandleLog($"bench log {i}", string.Empty, TerminalLogType.Message);
            }

            _terminal._commandInput.value = SelectiveToken;
            yield return null;
            yield return null;

            OperationReport clean = Measure(() => _terminal.RefreshUI(), DefaultSampleCount);
            LogScale(
                "ui-refresh-clean",
                $"state=open hints={SelectiveMatchCount} logs={SteadyStateLogCount}",
                clean
            );

            int logSequence = 0;
            OperationReport logSync = Measure(
                () =>
                {
                    Terminal.Buffer.HandleLog(
                        $"bench live {logSequence++}",
                        string.Empty,
                        TerminalLogType.Message
                    );
                    _terminal.RefreshUI();
                },
                DefaultSampleCount
            );
            IReadOnlyList<LogItem> logs = Terminal.Buffer.Logs;
            Assert.AreEqual(
                $"bench live {logSequence - 1}",
                logs[logs.Count - 1].message,
                "Sanity: every measured write must land in the buffer"
            );
            LogScale("ui-refresh-logsync", "state=open adds=1/pass", logSync);

            ParkCompletionState();
            _terminal.Close();
            int frameBudget = 600;
            while (0 < frameBudget-- && !_terminal.IsClosed)
            {
                yield return null;
            }

            Assert.IsTrue(
                _terminal.IsClosed,
                "Sanity: the terminal must settle closed before the idle row"
            );
            OperationReport closed = Measure(() => _terminal.RefreshUI(), DefaultSampleCount);
            LogScale("ui-refresh-closed", "gate=LateUpdate-skips-this-pass", closed);
        }

        [UnityTest]
        public IEnumerator MeasuresOpenCloseAndFirstOpenBuild()
        {
            yield return SpawnTerminal(HintDisplayMode.AutoCompleteOnly);
            PrepareTier(100);
            yield return null;

            /*
                First-open build: tear the visual tree down (what OnDisable
                does) and rebuild it (what the first open pays), measured
                separately from backend readiness.
             */
            OperationReport firstOpen = Measure(
                () =>
                {
                    _terminal.TeardownUI();
                    _terminal.EnsureUI();
                },
                StressSampleCount
            );
            LogScale("ui-first-open-build", "tree=full rebuild", firstOpen);

            OperationReport openToggle = Measure(
                () => _terminal.SetState(TerminalState.OpenFull),
                DefaultSampleCount
            );
            LogScale("ui-open-toggle", "tree=prebuilt", openToggle);

            OperationReport closeToggle = Measure(
                () => _terminal.SetState(TerminalState.Closed),
                DefaultSampleCount
            );
            Assert.That(
                _terminal._commandInput != null,
                "Sanity: closing keeps the built tree (teardown happens on disable)"
            );
            LogScale("ui-close-toggle", "tree=kept", closeToggle);
        }

        private IEnumerator SpawnTerminal(HintDisplayMode hintMode)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUIStandardOperations");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.hintDisplayMode = hintMode;
            _terminal._logBufferSize = SteadyStateLogCount;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("TerminalUI-level benchmarks run in the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);
            yield return null;

            int frameBudget = 600;
            while (
                0 < frameBudget--
                && (
                    _terminal._commandInput == null
                    || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex
                )
            )
            {
                yield return null;
            }

            Assert.That(
                _terminal._commandInput != null,
                "The terminal input field should exist after the terminal opens"
            );
        }

        /*
            Replaces the shell's custom commands with the tier's command set
            and pays deferred discovery once, outside every measured window.
         */
        private void PrepareTier(int commandCount)
        {
            Terminal.Shell.ClearCustomCommands();
            Terminal.Shell.AddCommand(NoopCommandName, HandleNoop);
            Terminal.Shell.AddCommand(NoHistoryCommandName, HandleNoop, 1, addToHistory: false);
            for (int i = 0; i < commandCount; ++i)
            {
                Terminal.Shell.AddCommand($"bench-cmd-{i:D4}", HandleNoop, 1);
            }

            bool logsEnabled = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try
            {
                Assert.IsTrue(
                    Terminal.Shell.RunCommand(NoopCommandName),
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
            List<string> candidates = new List<string>(ProviderCandidateCount);
            for (int i = 0; i < ProviderCandidateCount; ++i)
            {
                candidates.Add($"candidate-{i:D4}");
            }

            CommandBuilder builder = CommandBuilder
                .Create(ProviderCommandName)
                .Arg<string>(
                    "candidate",
                    spec => spec.Required().Choices(context => candidates, value => value)
                )
                .Handler((context, arguments) => { });
            Assert.IsTrue(
                Terminal.Shell.AddCommand(builder, out _),
                "The provider command should register"
            );
        }

        /*
            Leaves the completion buffer empty in a hint-hiding mode so no
            player-loop frame between windows builds a hint row per match.
         */
        private void ParkCompletionState()
        {
            _terminal.hintDisplayMode = HintDisplayMode.AutoCompleteOnly;
            _terminal.ResetAutoComplete();
            _terminal.hintDisplayMode = HintDisplayMode.Never;
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

        private readonly struct BenchmarkTier
        {
            public int Commands { get; }

            public string Token { get; }

            public int Samples { get; }

            public BenchmarkTier(int commands, string token, int samples)
            {
                Commands = commands;
                Token = token;
                Samples = samples;
            }
        }
    }
}
