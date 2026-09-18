namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using Backend;
    using NUnit.Framework;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

    /*
        Scaling evidence for scene-object completion (#66/T02/T03): the
        end-to-end shell completion path (tokenize, provider query, candidate
        formatting, dedup) measured over scene sizes around the 1,000-candidate
        reference workload and a 10,000-object stress tier, plus the
        execution-path name resolution. Numbers land in the test log for the
        session report; the asserted budgets are generous regression
        tripwires, not the plan's completion gate.
     */
    public sealed class SceneObjectCompletionScalingTests
    {
        private const string CommandName = "scale-target";

        private const string NamePrefix = "DxScale-";

        private const string TypedToken = "DxS";

        private const string SelectiveToken = "DxScale-0004";

        private const int WarmupIterations = 30;

        private GameObject _root;

        private static IEnumerable<TestCaseData> ScalingCases()
        {
            yield return new TestCaseData(0, string.Empty, 300).SetName("EmptyToken.EmptyScene");
            yield return new TestCaseData(0, TypedToken, 300).SetName("TypedToken.EmptyScene");
            yield return new TestCaseData(0, SelectiveToken, 300).SetName(
                "SelectiveToken.EmptyScene"
            );
            yield return new TestCaseData(100, string.Empty, 300).SetName(
                "EmptyToken.HundredObjects"
            );
            yield return new TestCaseData(100, TypedToken, 300).SetName(
                "TypedToken.HundredObjects"
            );
            yield return new TestCaseData(100, SelectiveToken, 300).SetName(
                "SelectiveToken.HundredObjects"
            );
            yield return new TestCaseData(1000, string.Empty, 300).SetName(
                "EmptyToken.ThousandObjects"
            );
            yield return new TestCaseData(1000, TypedToken, 300).SetName(
                "TypedToken.ThousandObjects"
            );
            yield return new TestCaseData(1000, SelectiveToken, 300).SetName(
                "SelectiveToken.ThousandObjects"
            );
            yield return new TestCaseData(10000, string.Empty, 50).SetName(
                "EmptyToken.TenThousandObjects"
            );
            yield return new TestCaseData(10000, TypedToken, 50).SetName(
                "TypedToken.TenThousandObjects"
            );
            yield return new TestCaseData(10000, SelectiveToken, 50).SetName(
                "SelectiveToken.TenThousandObjects"
            );
        }

        private static IEnumerable<TestCaseData> BudgetCases()
        {
            yield return new TestCaseData(0, 5f).SetName("Budget.EmptyScene");
            yield return new TestCaseData(100, 10f).SetName("Budget.HundredObjects");
            yield return new TestCaseData(1000, 50f).SetName("Budget.ThousandObjects");
            yield return new TestCaseData(10000, 250f).SetName("Budget.TenThousandObjects");
        }

        private static IEnumerable<TestCaseData> ParseCases()
        {
            yield return new TestCaseData(0, 300).SetName("Parse.EmptyScene");
            yield return new TestCaseData(100, 300).SetName("Parse.HundredObjects");
            yield return new TestCaseData(1000, 300).SetName("Parse.ThousandObjects");
            yield return new TestCaseData(10000, 50).SetName("Parse.TenThousandObjects");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        [TestCaseSource(nameof(ScalingCases))]
        public void MeasuresEndToEndCompletionScaling(
            int objectCount,
            string token,
            int sampleCount
        )
        {
            CommandShell shell = CreateScaledShell(objectCount);
            List<CommandCompletion> results = new();

            /*
               The trailing space matters: with it the caret opens a new
               argument, so the provider query runs; trimming it would turn the
               empty-token case into command-name completion and skip the
               provider entirely.
             */
            string input = $"{CommandName} {token}";
            int gen0Collections = System.GC.CollectionCount(0);

            for (int i = 0; i < WarmupIterations; ++i)
            {
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    input,
                    input.Length,
                    results,
                    out _
                );
            }

            long[] samples = new long[sampleCount];
            for (int i = 0; i < sampleCount; ++i)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    input,
                    input.Length,
                    results,
                    out _
                );
                samples[i] = stopwatch.ElapsedTicks;
            }

            long[] sorted = (long[])samples.Clone();
            System.Array.Sort(sorted);
            double ticksToMilliseconds = 1000.0 / Stopwatch.Frequency;
            double median = sorted[sampleCount / 2] * ticksToMilliseconds;
            double p95 =
                sorted[(int)System.Math.Ceiling(sampleCount * 0.95) - 1] * ticksToMilliseconds;
            double max = sorted[sampleCount - 1] * ticksToMilliseconds;
            int gen0Delta = System.GC.CollectionCount(0) - gen0Collections;

            Debug.Log(
                $"[DxCommandTerminal][Scale] objects={objectCount} token='{token}' "
                    + $"results={results.Count} samples={sampleCount} "
                    + $"median={median:F3}ms p95={p95:F3}ms max={max:F3}ms gen0={gen0Delta}"
            );
        }

        [TestCaseSource(nameof(BudgetCases))]
        public void CompletionStaysUnderRegressionBudget(int objectCount, float budgetMilliseconds)
        {
            CommandShell shell = CreateScaledShell(objectCount);
            List<CommandCompletion> results = new();
            string input = $"{CommandName} {TypedToken}";

            for (int i = 0; i < WarmupIterations; ++i)
            {
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    input,
                    input.Length,
                    results,
                    out _
                );
            }

            long[] samples = new long[100];
            Stopwatch stopwatch = new();
            for (int i = 0; i < samples.Length; ++i)
            {
                stopwatch.Restart();
                shell.TryComplete(
                    CommandExecutionContext.Current,
                    input,
                    input.Length,
                    results,
                    out _
                );
                samples[i] = stopwatch.ElapsedTicks;
            }

            System.Array.Sort(samples);
            double p95 =
                samples[(int)System.Math.Ceiling(samples.Length * 0.95) - 1]
                * 1000.0
                / Stopwatch.Frequency;

            Assert.AreEqual(
                objectCount,
                results.Count,
                "Every fixture object should complete for the typed prefix"
            );
            Assert.Less(
                p95,
                budgetMilliseconds,
                $"Typed-prefix completion p95 exceeded the regression budget "
                    + $"({p95:F3} ms >= {budgetMilliseconds} ms at {objectCount} objects)"
            );
        }

        [TestCaseSource(nameof(ParseCases))]
        public void MeasuresNameResolutionScaling(int objectCount, int sampleCount)
        {
            SceneObjectArgumentAdapter<GameObject> adapter = new();
            CreateScene(objectCount);
            int targetIndex = objectCount / 2;
            string targetName = $"{NamePrefix}{targetIndex:D6}";
            int gen0Collections = System.GC.CollectionCount(0);

            for (int i = 0; i < WarmupIterations; ++i)
            {
                adapter.TryParse(targetName, out _);
            }

            long[] samples = new long[sampleCount];
            for (int i = 0; i < sampleCount; ++i)
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                adapter.TryParse(targetName, out _);
                samples[i] = stopwatch.ElapsedTicks;
            }

            long[] sorted = (long[])samples.Clone();
            System.Array.Sort(sorted);
            double ticksToMilliseconds = 1000.0 / Stopwatch.Frequency;
            double median = sorted[sampleCount / 2] * ticksToMilliseconds;
            double p95 =
                sorted[(int)System.Math.Ceiling(sampleCount * 0.95) - 1] * ticksToMilliseconds;
            double max = sorted[sampleCount - 1] * ticksToMilliseconds;
            int gen0Delta = System.GC.CollectionCount(0) - gen0Collections;

            Debug.Log(
                $"[DxCommandTerminal][Scale] parse objects={objectCount} "
                    + $"target='{targetName}' samples={sampleCount} "
                    + $"median={median:F3}ms p95={p95:F3}ms max={max:F3}ms gen0={gen0Delta}"
            );
        }

        private void CreateScene(int objectCount)
        {
            _root = new GameObject("DxBenchContainer");
            for (int i = 0; i < objectCount; ++i)
            {
                GameObject child = new($"{NamePrefix}{i:D6}");
                child.transform.SetParent(_root.transform);
            }
        }

        private CommandShell CreateScaledShell(int objectCount)
        {
            CreateScene(objectCount);
            CommandShell shell = new(new CommandHistory(16));
            SceneObjectArgumentAdapter<GameObject> adapter = new();
            Assert.IsTrue(
                shell.AddCommand(
                    CommandBuilder
                        .Create(CommandName)
                        .Contexts(CommandExecutionContextSets.All)
                        .Arg<GameObject>(
                            "target",
                            spec =>
                                spec.Required()
                                    .RawParser(adapter.TryParse)
                                    .Choices(adapter.GetChoices, adapter.FormatChoice)
                        )
                        .Handler((context, arguments) => { }),
                    out _
                )
            );
            return shell;
        }
    }
}
