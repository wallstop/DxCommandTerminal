namespace WallstopStudios.DxCommandTerminal.SourceGenerators.Tests
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using Microsoft.CodeAnalysis.CSharp;
    using Xunit;
    using Xunit.Abstractions;

    /*
        The PLAN.md T05 generator timing gate: p95 < 25 ms of generator
        execution per affected assembly at the reference workload (1,000
        commands). The fixture mixes the shapes a real assembly carries -
        public commands, private commands in partial holders, and rejected
        signatures - so the walk, emission, and companion grouping are all in
        the measured window. Compilation and fixture construction stay
        outside it; only RunGenerators is timed, warmed up first per the
        plan's steady-state sampling rules.

        The non-parallelized collection keeps every other test compilation
        off the CPU cores during the measured window; on two-core CI runners
        the default collection parallelism put the p95 over the gate.
     */
    [CollectionDefinition("GeneratorTimingGateSequential", DisableParallelization = true)]
    public sealed class GeneratorTimingGateSequentialDefinition { }

    [Collection("GeneratorTimingGateSequential")]
    public sealed class GeneratorTimingGateTests
    {
        private const int ReferenceCommandCount = 1000;
        private const int WarmupRuns = 20;
        private const int SampleCount = 1000;
        private const double GateMilliseconds = 25.0;

        private readonly ITestOutputHelper _output;

        public GeneratorTimingGateTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static double RunOnce(CSharpGeneratorDriver driver, CSharpCompilation compilation)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        private static double Percentile(double[] sortedSamples, double fraction)
        {
            int index = (int)Math.Ceiling(fraction * sortedSamples.Length) - 1;
            if (index < 0)
            {
                index = 0;
            }

            return sortedSamples[index];
        }

        /*
            One attributed method per command: every 50th is private inside a
            partial holder (companion path), every 100th has a rejected
            signature (accessor path), the rest are public (direct path).
        */
        private static string BuildReferenceFixture()
        {
            StringBuilder fixture = new StringBuilder(ReferenceCommandCount * 96);
            fixture.AppendLine("namespace Fixtures");
            fixture.AppendLine("{");
            fixture.AppendLine("    using WallstopStudios.DxCommandTerminal.Attributes;");
            fixture.AppendLine("    using WallstopStudios.DxCommandTerminal.Backend;");
            fixture.AppendLine();
            fixture.AppendLine("    public static partial class ReferenceCommands");
            fixture.AppendLine("    {");
            for (int i = 0; i < ReferenceCommandCount; ++i)
            {
                if (i % 100 == 99)
                {
                    fixture.AppendLine(
                        "        [RegisterCommand] public static void Rejected"
                            + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + "(int wrong) { }"
                    );
                }
                else if (i % 50 == 49)
                {
                    fixture.AppendLine(
                        "        [RegisterCommand] private static void Private"
                            + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + "(CommandArg[] args) { }"
                    );
                }
                else
                {
                    fixture.AppendLine(
                        "        [RegisterCommand] public static void Public"
                            + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            + "(CommandArg[] args) { }"
                    );
                }
            }

            fixture.AppendLine("    }");
            fixture.AppendLine("}");
            return fixture.ToString();
        }

        [Fact]
        public void GeneratorExecutionStaysUnderTheTimingGate()
        {
            CSharpCompilation compilation = TestCompilationFactory.CreateCompilation(
                "GeneratorTimingGate",
                BuildReferenceFixture()
            );

            CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(
                new WallstopStudios.DxCommandTerminal.SourceGenerators.CommandCatalogGenerator()
            );

            for (int i = 0; i < WarmupRuns; ++i)
            {
                RunOnce(driver, compilation);
            }

            double[] samples = new double[SampleCount];
            for (int i = 0; i < SampleCount; ++i)
            {
                samples[i] = RunOnce(driver, compilation);
            }

            Array.Sort(samples);
            double median = Percentile(samples, 0.5);
            double p95 = Percentile(samples, 0.95);
            double max = samples[samples.Length - 1];
            _output.WriteLine(
                $"Generator execution over {ReferenceCommandCount} commands, "
                    + $"{SampleCount} warmed samples: "
                    + $"median {median:F3} ms, p95 {p95:F3} ms, max {max:F3} ms "
                    + $"(gate p95 < {GateMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} ms)"
            );

            Assert.True(
                p95 < GateMilliseconds,
                $"Generator execution p95 {p95:F3} ms exceeded the "
                    + $"{GateMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} ms gate "
                    + $"(median {median:F3} ms, max {max:F3} ms)"
            );
        }
    }
}
