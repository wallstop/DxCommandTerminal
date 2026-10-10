namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Reflection;
    using System.Reflection.Emit;
    using Attributes;
    using Backend;
    using NUnit.Framework;
    using Debug = UnityEngine.Debug;

#if UNITY_EDITOR
    /*
        Large-domain and command-volume scaling evidence for deferred
        command registration (#38/T05, modeling the #36 consumer report):
        readiness over ~500 filler assemblies plus the live editor domain,
        and over 0/10/100/1,000/10,000-command tiers through the reflected
        and provider pipelines. Numbers land in the log
        ([DxCommandTerminal][Scale]) as session evidence; asserted budgets
        are generous tripwires that catch a return to broad-domain scans
        (issue #36's multi-second stalls), not the plan's 5 ms readiness
        gate, which stays a pinned-environment measurement. Each warm
        budget is asserted twice - on the median, and on a loose p95 bound
        for gross tail blowups - so two stalled samples under 4x the budget
        cannot fail a shared host editor (#170). The cold budget stays a
        single sample of the first registration cycle - that window happens
        once per session by definition - and a breach fails when either
        immediate first-touch confirmation repeats it (#171): three sessions
        recorded single cold samples at 1.5-4x a budget that never
        approached it again, so the trip answers "regression" and a
        non-repeating spike is recorded as a stall. Editor-only: players
        cannot emit IL.

        Filler assemblies are dynamic, so classification exercises the
        IsDynamic skip rather than metadata reads; the live editor domain
        (its own non-dynamic assemblies) carries the metadata-read cost in
        every measured window. The first registration cycle this suite runs
        carries the true cold-domain number (NUnit runs these fixtures
        alphabetically, so the inflated-domain test usually runs first);
        later tiers' cold numbers cover first-touch of their tier assembly
        only - domain caches warm across the session, and the 0-command
        tier separates whole-domain scan cost from per-command cost. The
        10,000-command tier is a documented scaling stress tier whose warm
        tail is allocator/GC dominated. Filler assemblies persist for the
        whole play session (dynamic assemblies are non-collectible); other
        fixtures must not assert absolute domain assembly counts. Measured
        windows exclude the shell's readiness log line (the editor logger
        is disabled around them and restored in finally).
     */
    public sealed class CommandDiscoveryScalingTests
    {
        private const string VolumeCommandPrefix = "scale-volume-";

        private const int FillerAssemblyCount = 500;

        private const int GateTierCommandCount = 1000;

        private const int WarmupShellCount = 3;

        private const int WarmShellCount = 30;

        private const float ClassificationTripwireMilliseconds = 25f;

        private const float GateTierColdBudgetMilliseconds = 60f;

        private const float GateTierWarmBudgetMilliseconds = 40f;

        /*
            How many first-touch re-measurements a cold breach must survive
            before it fails (#171): the trip needs any one of them to repeat
            the breach, so a lone stall cannot confirm itself, and a
            sustained breach cannot hide. On breach the pair costs one extra
            registration cycle per measurement - at the 10,000 tier that is
            the tier's own cold cost, twice, and only ever paid on a breach.
         */
        private const int ColdConfirmationCount = 2;

        /*
            The warm tripwire asserts the median at the tier budget and p95
            at this multiple of it (#170). n=30 puts p95 on the 29th sorted
            sample, the second worst, so two stalled samples cross a p95
            bound; the median is an interior statistic that needs 16 stalls
            to move. 4x is 4.7x above the worst sample in session-082's
            five-run series and ~3x above the worst 1,000-tier warm p95 ever
            recorded (10.6 ms, session-041, before the session-044
            classification memo). It is a 4x loosening at every tier; see the
            budget comment for what that costs where the old bound was
            closest.
         */
        private const float TailTripwireMultiplier = 4f;

        private static readonly Assembly[] FillerAssemblies = CreateFillerAssemblies();

        private readonly List<IDisposable> _handles = new();

        /*
            Budgets are absolute tripwires for the pinned local editor
            environment (Unity 6000.4.6f1, ~750-assembly domain), sized to
            catch the #36-class broad-scan regressions and the return of
            eager per-command Delegate.CreateDelegate binding at readiness
            (measured warm p95 ~19 ms at the 1,000-command tier, over the
            then-15 ms tripwire - still 2.4x over the tightened 8 ms budget) -
            not ordinary scheduler jitter, and not portable across machines.
            The warm bound is split in two, because one bound cannot serve
            both audiences on a shared host (#170): the median is asserted
            at the tier budget, and p95 at TailTripwireMultiplier times it.
            This is a strict relaxation at every tier, disclosed here rather
            than buried. The old bound tolerated 1 of 30 samples at or over
            budget. The split tolerates 15 of 30, of which at most one may sit
            past 4x - and that one is unbounded, because p95 is the 29th of
            30 and never reads the 30th. So the 2nd through 15th
            over-budget sample, and one arbitrarily large sample, now pass
            where the old bound caught them.
            The 1,000- and 10,000-command tiers lose the most: they had the
            least old-bound headroom (1.78x and 2.88x). At the 10,000 tier
            the tail leg moves from 150 ms to 600 ms against a measured 52 ms
            p95, so only a 2-of-30 population past 11.5x trips it, and the
            median leg needs 16 of 30. That tier's warm tail is documented
            allocator/GC dominated, so a 4x loosening there is the cost of
            the same change that stops the stall.
            What the split buys is narrower than "more headroom": the median
            leg's headroom is 1.82x on the worst median in session-082's
            series, so a host-wide slowdown of 1.82x or more still trips it.
            Whether that is what tripped #170 is unrecoverable - the original
            failure line was lost with the editor's console ring buffer and
            printed no median (#173). The regression the budget was sized
            against (a whole-distribution shift to ~19 ms) is caught by the
            median leg at the same 8 ms threshold, on a statistic that needs
            16 stalls to move instead of 2.
            Session-082 series on the pinned editor (2026-09-27; four
            filtered runs plus one inside a 363-test full EditMode run; the
            1,000-command reflected tier): cold 8.598-18.130 ms (budget 40),
            median 4.235-4.403 ms (budget 8), p95 4.476-4.662 ms, max
            4.592-6.761 ms (tail bound 32) - the median spread is 4.0% and
            the max spread 47%. Separately, a 300-sample window in that
            session carried a 13.944 ms single sample against a 0.063 ms
            median in SceneObjectCompletionScalingTests (objects=100,
            token='DxS') - a different suite and tier, quoted only as this
            host's stall scale. Measured deferred-binding numbers the
            budgets are set from: reflected tiers
            0-1,000 warm p95 ~2.6-10.6 ms (cold 2.5-11.5 ms; the small tiers
            settle at 0.3-0.6 ms), 10,000 tier
            warm p95 ~55 ms (cold ~87-112 ms), provider gate tier warm p95
            ~9.1-11.5 ms (cold ~14-16 ms), inflated-domain gate tier warm
            p95 ~6.5-7.8 ms (cold ~18-20 ms).
            Session-044 (assembly-classification memo in CommandShell: the
            immutable per-assembly metadata reads no longer repeat on every
            warm cycle): 0-100 tiers warm p95 ~0.3-0.9 ms (were ~2.6-3,
            classification-dominated), gate tier median 4.357 / p95 4.498 ms
            (cold 9.0) - the plan's 5 ms readiness gate is met at the 1,000-
            command reference, and the 1,000-tier warm budget tightened
            15 -> 8 ms from that series; 10,000 tier median 49.5 / p95 52.1 ms
            (cold 86.8); provider gate tier median 7.2 / p95 7.6 ms; 500-
            filler classification 0.082 ms. A fold-equivalent comparer
            replacement for the BCL OrdinalIgnoreCase comparer was measured
            in the same session (live A/B: 10,000-tree inserts 65.6 ms vs
            BCL 40.2 ms) and rejected - the BCL comparer is faster on Mono.
         */
        private static IEnumerable<TestCaseData> VolumeCases()
        {
            yield return new TestCaseData(0, 40f, 15f).SetName("Volume.Empty");
            yield return new TestCaseData(10, 40f, 15f).SetName("Volume.TenCommands");
            yield return new TestCaseData(100, 40f, 15f).SetName("Volume.HundredCommands");
            yield return new TestCaseData(1000, 40f, 8f).SetName("Volume.ThousandCommands");
            yield return new TestCaseData(10000, 200f, 150f).SetName("Volume.TenThousandCommands");
        }

        private static Assembly[] CreateFillerAssemblies()
        {
            Assembly[] fillers = new Assembly[FillerAssemblyCount];
            for (int i = 0; i < FillerAssemblyCount; ++i)
            {
                AssemblyName name = new($"DiscoveryScalingFiller-{i:D4}-{Guid.NewGuid():N}");
                AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                    name,
                    AssemblyBuilderAccess.Run
                );
                ModuleBuilder module = assembly.DefineDynamicModule("Filler");
                TypeBuilder type = module.DefineType(
                    $"FillerType{i:D4}",
                    TypeAttributes.Public | TypeAttributes.Sealed
                );
                MethodBuilder method = type.DefineMethod(
                    "FillerMethod",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(int),
                    Type.EmptyTypes
                );
                ILGenerator body = method.GetILGenerator();
                body.Emit(OpCodes.Ldc_I4, i);
                body.Emit(OpCodes.Ret);
                type.CreateType();
                fillers[i] = assembly;
            }

            return fillers;
        }

        private static Assembly CreateVolumeAssembly(int commandCount)
        {
            AssemblyName name = new($"DiscoveryScalingVolume-{commandCount:D5}-{Guid.NewGuid():N}");
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                name,
                AssemblyBuilderAccess.Run
            );
            ModuleBuilder module = assembly.DefineDynamicModule("VolumeCommands");
            TypeBuilder type = module.DefineType(
                "WallstopStudios.DxCommandTerminal.Tests.VolumeCommands",
                TypeAttributes.Public | TypeAttributes.Sealed
            );
            ConstructorInfo attributeConstructor = typeof(RegisterCommandAttribute).GetConstructor(
                new[] { typeof(string) }
            );
            for (int i = 0; i < commandCount; ++i)
            {
                MethodBuilder method = type.DefineMethod(
                    $"VolumeCommand{i:D6}",
                    MethodAttributes.Public | MethodAttributes.Static,
                    typeof(void),
                    new[] { typeof(CommandArg[]) }
                );
                method.DefineParameter(1, ParameterAttributes.None, "args");
                method.GetILGenerator().Emit(OpCodes.Ret);
                method.SetCustomAttribute(
                    new CustomAttributeBuilder(
                        attributeConstructor,
                        new object[] { VolumeCommandName(i) }
                    )
                );
            }

            type.CreateType();
            return assembly;
        }

        private static string VolumeCommandName(int index)
        {
            return $"{VolumeCommandPrefix}{index:D6}";
        }

        private static CommandShell CreateDeferredShell()
        {
            CommandShell shell = new CommandShell(new CommandHistory(16));
            shell.InitializeAutoRegisteredCommands(deferRegistration: true);
            return shell;
        }

        private static ReadinessReport MeasureReadiness(double coldBudgetMilliseconds)
        {
            /*
                The shell logs every registration cycle in the editor; that
                log I/O would sit inside every measured window (and dominate
                the small tiers), so it is silenced around the measurement.
                A full collection first keeps cross-fixture heap pressure
                out of the warm tail: in full-suite runs the p95 at the
                1,000-command tier otherwise rides gen0 pauses past the
                tripwire even though isolated runs sit well under it.
             */
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            bool logsEnabled = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            try
            {
                CommandShell coldShell = CreateDeferredShell();
                Stopwatch stopwatch = Stopwatch.StartNew();
                coldShell.EnsureAutoCommandsRegistered();
                stopwatch.Stop();
                double coldMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

                /*
                    The cold window is one sample by definition (the first
                    registration cycle after the domain reload), so a one-off
                    host stall can cross a budget no real regression needs
                    (#171: three sessions recorded single samples at 1.5-4x
                    a budget that never approached it again). When the cold
                    sample sits at or over the budget, the breach is measured
                    again before it can fail: two immediate first-touch
                    cycles, the coldest samples available without a reload.
                    A genuine registration regression shows in every cycle;
                    a stall does not reproduce. The budget is unchanged - the
                    breach now has to repeat to count.
                 */
                double[] coldConfirmations = null;
                if (coldBudgetMilliseconds <= coldMilliseconds)
                {
                    coldConfirmations = new double[ColdConfirmationCount];
                    for (int i = 0; i < ColdConfirmationCount; ++i)
                    {
                        CommandShell confirmationShell = CreateDeferredShell();
                        stopwatch.Restart();
                        confirmationShell.EnsureAutoCommandsRegistered();
                        stopwatch.Stop();
                        coldConfirmations[i] = stopwatch.Elapsed.TotalMilliseconds;
                    }
                }

                for (int i = 0; i < WarmupShellCount; ++i)
                {
                    CreateDeferredShell().EnsureAutoCommandsRegistered();
                }

                double[] samples = new double[WarmShellCount];
                for (int i = 0; i < WarmShellCount; ++i)
                {
                    CommandShell shell = CreateDeferredShell();
                    stopwatch.Restart();
                    shell.EnsureAutoCommandsRegistered();
                    stopwatch.Stop();
                    samples[i] = stopwatch.Elapsed.TotalMilliseconds;
                }

                Array.Sort(samples);
                return new ReadinessReport(coldShell, coldMilliseconds, coldConfirmations, samples);
            }
            finally
            {
                Debug.unityLogger.logEnabled = logsEnabled;
            }
        }

        private static double Percentile(double[] sortedMilliseconds, double fraction)
        {
            int index = (int)Math.Ceiling(fraction * sortedMilliseconds.Length) - 1;
            if (index < 0)
            {
                index = 0;
            }

            return sortedMilliseconds[index];
        }

        private static void AssertReadinessUnderTripwires(
            ReadinessReport readiness,
            int commandCount,
            float coldBudgetMilliseconds,
            float warmBudgetMilliseconds
        )
        {
            /*
                The cold tripwire stays absolute; a breach counts only when
                a first-touch re-measurement crosses the same budget (#171).
                The message names the original sample, its confirmations, and
                the margin each left, so a trip still says what it cost. A
                breach no confirmation repeats passes this assert and is
                recorded in the log line instead - a host stall, not a
                regression.
             */
            if (readiness.ColdTripwireFails(coldBudgetMilliseconds))
            {
                string confirmations = string.Join(", ", readiness.ColdConfirmationMilliseconds);
                Assert.Less(
                    readiness.ColdMilliseconds,
                    coldBudgetMilliseconds,
                    $"Cold readiness tripwire crossed at {commandCount} commands: "
                        + $"statistic=cold n=1 measured={readiness.ColdMilliseconds:F3} ms "
                        + $"budget={coldBudgetMilliseconds:F3} ms "
                        + $"margin={coldBudgetMilliseconds - readiness.ColdMilliseconds:F3} ms "
                        + $"confirmations=[{confirmations}] "
                        + $"(n={readiness.ColdConfirmationMilliseconds.Length} at the same budget)"
                );
            }

            AssertWarmStatisticUnderTripwire(
                readiness,
                commandCount,
                "median",
                readiness.Median,
                warmBudgetMilliseconds
            );
            AssertWarmStatisticUnderTripwire(
                readiness,
                commandCount,
                "p95",
                readiness.Percentile95,
                warmBudgetMilliseconds * TailTripwireMultiplier
            );
        }

        private static void AssertWarmStatisticUnderTripwire(
            ReadinessReport readiness,
            int commandCount,
            string statistic,
            double measuredMilliseconds,
            float budgetMilliseconds
        )
        {
            Assert.Less(
                measuredMilliseconds,
                budgetMilliseconds,
                $"Warm readiness tripwire crossed at {commandCount} commands: "
                    + $"statistic={statistic} n={readiness.SampleCount} "
                    + $"measured={measuredMilliseconds:F3} ms "
                    + $"budget={budgetMilliseconds:F3} ms "
                    + $"margin={budgetMilliseconds - measuredMilliseconds:F3} ms "
                    + $"samplesOver={readiness.SamplesOver(budgetMilliseconds)} | "
                    + $"median={readiness.Median:F3} ms p95={readiness.Percentile95:F3} ms "
                    + $"max={readiness.Maximum:F3} ms"
            );
        }

        private static void LogReadiness(
            string detail,
            ReadinessReport readiness,
            float coldBudgetMilliseconds,
            float warmBudgetMilliseconds
        )
        {
            float tailBudgetMilliseconds = warmBudgetMilliseconds * TailTripwireMultiplier;
            string coldConfirmations =
                readiness.ColdConfirmationMilliseconds.Length == 0
                    ? "none"
                    : string.Join(", ", readiness.ColdConfirmationMilliseconds);
            Debug.Log(
                $"[DxCommandTerminal][Scale] {detail} "
                    + $"assemblies={AppDomain.CurrentDomain.GetAssemblies().Length} "
                    + $"cold={readiness.ColdMilliseconds:F3}ms "
                    + $"coldBudget={coldBudgetMilliseconds:F3}ms "
                    + $"coldConfirmations=[{coldConfirmations}] "
                    + $"warmSamples={readiness.SampleCount} "
                    + $"median={readiness.Median:F3}ms p95={readiness.Percentile95:F3}ms "
                    + $"max={readiness.Maximum:F3}ms warmBudget={warmBudgetMilliseconds:F3}ms "
                    + $"tailBudget={tailBudgetMilliseconds:F3}ms "
                    + $"overWarmBudget={readiness.SamplesOver(warmBudgetMilliseconds)} "
                    + $"overTailBudget={readiness.SamplesOver(tailBudgetMilliseconds)}"
            );
        }

        [TearDown]
        public void TearDown()
        {
            foreach (IDisposable handle in _handles)
            {
                handle.Dispose();
            }

            _handles.Clear();
        }

        [TestCaseSource(nameof(VolumeCases))]
        public void MeasuresReadinessAcrossCommandVolume(
            int commandCount,
            float coldBudgetMilliseconds,
            float warmBudgetMilliseconds
        )
        {
            Assembly volumeAssembly = CreateVolumeAssembly(commandCount);
            _handles.Add(CommandShell.IncludeDiscoveryAssembly(volumeAssembly));

            ReadinessReport readiness = MeasureReadiness(coldBudgetMilliseconds);

            Assert.IsTrue(
                readiness.Shell.AutoCommandsRegistered,
                "Sanity: expected the cold registration cycle to complete"
            );
            Assert.GreaterOrEqual(
                readiness.Shell.AutoRegisteredCommands.Count,
                commandCount,
                "Every volume command should register alongside the domain baseline"
            );
            if (0 < commandCount)
            {
                string firstName = VolumeCommandName(0);
                Assert.IsTrue(
                    readiness.Shell.Commands.ContainsKey(firstName),
                    "The first volume command should be registered"
                );
                Assert.IsTrue(
                    readiness.Shell.RunCommand(firstName),
                    "The first volume command should run after readiness"
                );
            }

            AssertReadinessUnderTripwires(
                readiness,
                commandCount,
                coldBudgetMilliseconds,
                warmBudgetMilliseconds
            );

            LogReadiness(
                $"readiness path=reflected commands={commandCount} "
                    + $"registered={readiness.Shell.AutoRegisteredCommands.Count}",
                readiness,
                coldBudgetMilliseconds,
                warmBudgetMilliseconds
            );
        }

        [Test]
        public void ProviderPathServesTheGateTier()
        {
            Assembly volumeAssembly = CreateVolumeAssembly(GateTierCommandCount);
            _handles.Add(CommandShell.IncludeDiscoveryAssembly(volumeAssembly));
            _handles.Add(
                CommandShell.RegisterDiscoveryProvider(new VolumeProvider(volumeAssembly))
            );

            List<CommandShell.AutoCommand> collected = new();
            Assert.AreEqual(
                CommandShell.AutoCommandSource.Provider,
                CommandShell.CollectAutoCommands(volumeAssembly, collected),
                "Sanity: the volume provider should claim the tier assembly"
            );
            Assert.AreEqual(
                GateTierCommandCount,
                collected.Count,
                "Sanity: the provider should serve every volume command"
            );

            ReadinessReport readiness = MeasureReadiness(GateTierColdBudgetMilliseconds);

            Assert.GreaterOrEqual(
                readiness.Shell.AutoRegisteredCommands.Count,
                GateTierCommandCount,
                "Every provider-served command should register"
            );
            AssertReadinessUnderTripwires(
                readiness,
                GateTierCommandCount,
                GateTierColdBudgetMilliseconds,
                GateTierWarmBudgetMilliseconds
            );

            LogReadiness(
                $"readiness path=provider commands={GateTierCommandCount} "
                    + $"registered={readiness.Shell.AutoRegisteredCommands.Count}",
                readiness,
                GateTierColdBudgetMilliseconds,
                GateTierWarmBudgetMilliseconds
            );
        }

        [Test]
        public void InflatedDomainKeepsClassificationAndReadinessBounded()
        {
            AssemblyName runtimeAssembly = typeof(BuiltInCommands).Assembly.GetName();
            int leakingFillers = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();
            foreach (Assembly filler in FillerAssemblies)
            {
                if (CommandShell.MayContainCommands(filler, runtimeAssembly))
                {
                    leakingFillers++;
                }
            }

            stopwatch.Stop();
            double classificationMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            Assert.AreEqual(0, leakingFillers, "No filler assembly may pass the reference filter");
            Assert.Less(
                classificationMilliseconds,
                ClassificationTripwireMilliseconds,
                $"Classification tripwire crossed: statistic=total n=1 "
                    + $"measured={classificationMilliseconds:F3} ms "
                    + $"budget={ClassificationTripwireMilliseconds:F3} ms "
                    + $"margin={ClassificationTripwireMilliseconds - classificationMilliseconds:F3} ms "
                    + $"| assemblies={FillerAssemblyCount} "
                    + $"perAssembly={classificationMilliseconds * 1000.0 / FillerAssemblyCount:F3} us"
            );

            Assembly volumeAssembly = CreateVolumeAssembly(GateTierCommandCount);
            _handles.Add(CommandShell.IncludeDiscoveryAssembly(volumeAssembly));
            ReadinessReport readiness = MeasureReadiness(GateTierColdBudgetMilliseconds);

            Assert.GreaterOrEqual(
                readiness.Shell.AutoRegisteredCommands.Count,
                GateTierCommandCount,
                "Every volume command should register in the inflated domain"
            );
            AssertReadinessUnderTripwires(
                readiness,
                GateTierCommandCount,
                GateTierColdBudgetMilliseconds,
                GateTierWarmBudgetMilliseconds
            );

            LogReadiness(
                $"domain fillers={FillerAssemblyCount} "
                    + $"classification={classificationMilliseconds:F3}ms "
                    + $"(all {FillerAssemblyCount} fillers, "
                    + $"{classificationMilliseconds * 1000.0 / FillerAssemblyCount:F2}us each) "
                    + $"commands={GateTierCommandCount} "
                    + $"registered={readiness.Shell.AutoRegisteredCommands.Count}",
                readiness,
                GateTierColdBudgetMilliseconds,
                GateTierWarmBudgetMilliseconds
            );
        }

        /*
            Serves the tier assembly through the provider stage. Deliberately
            uncached: the shell never caches provider results (providers own
            their caching), so its warm numbers measure the uncached walk
            and are not directly comparable to the shell-cached reflected
            path.
         */
        private sealed class VolumeProvider : ICommandDiscoveryProvider
        {
            private readonly Assembly _claimedAssembly;

            public VolumeProvider(Assembly claimedAssembly)
            {
                _claimedAssembly = claimedAssembly;
            }

            public bool TryCollect(Assembly assembly, List<CommandShell.AutoCommand> commands)
            {
                if (!ReferenceEquals(assembly, _claimedAssembly))
                {
                    return false;
                }

                foreach (Type type in assembly.GetTypes())
                {
                    foreach (
                        MethodInfo method in type.GetMethods(
                            BindingFlags.Public | BindingFlags.Static
                        )
                    )
                    {
                        if (!method.IsDefined(typeof(RegisterCommandAttribute), false))
                        {
                            continue;
                        }

                        if (
                            Attribute.GetCustomAttribute(method, typeof(RegisterCommandAttribute))
                            is RegisterCommandAttribute attribute
                        )
                        {
                            commands.Add(CommandShell.AutoCommand.FromReflected(method, attribute));
                        }
                    }
                }

                return true;
            }
        }

        private readonly struct ReadinessReport
        {
            public CommandShell Shell { get; }
            public double ColdMilliseconds { get; }
            public double Median { get; }
            public double Percentile95 { get; }
            public double Maximum { get; }
            public int SampleCount { get; }

            /*
                Empty unless the cold sample sat at or over its budget, in
                which case it holds the first-touch re-measurements the
                breach had to repeat to count (#171).
             */
            public double[] ColdConfirmationMilliseconds { get; }

            private readonly double[] _sortedWarmMilliseconds;

            public ReadinessReport(
                CommandShell shell,
                double coldMilliseconds,
                double[] coldConfirmations,
                double[] sortedWarmMilliseconds
            )
            {
                Shell = shell;
                ColdMilliseconds = coldMilliseconds;
                ColdConfirmationMilliseconds = coldConfirmations ?? Array.Empty<double>();
                _sortedWarmMilliseconds = sortedWarmMilliseconds;
                SampleCount = sortedWarmMilliseconds.Length;
                Median = Percentile(sortedWarmMilliseconds, 0.5);
                Percentile95 = Percentile(sortedWarmMilliseconds, 0.95);
                Maximum = sortedWarmMilliseconds[sortedWarmMilliseconds.Length - 1];
            }

            /*
                Whether the cold tripwire fails (#171): the sample is a
                breach at or over the budget, and a breach fails when a
                confirmation repeats it - the trip is a regression, not a
                stall. A breach no confirmation repeats is recorded in the
                log line and the assert passes. A breach with no
                confirmations behind it is a measurement bug (MeasureReadiness
                measures them whenever the sample breaches), so it fails too:
                the gate may answer "stall", never "unknown".
             */
            public bool ColdTripwireFails(double coldBudgetMilliseconds)
            {
                if (ColdMilliseconds < coldBudgetMilliseconds)
                {
                    return false;
                }

                if (ColdConfirmationMilliseconds.Length == 0)
                {
                    return true;
                }

                foreach (double confirmation in ColdConfirmationMilliseconds)
                {
                    if (coldBudgetMilliseconds <= confirmation)
                    {
                        return true;
                    }
                }

                return false;
            }

            /*
                Counts samples at or over the budget. That is the set
                Assert.Less rejects when the asserted statistic is itself a
                sample (p95, max); for the median leg it is the over-budget
                population, which is the useful diagnostic - a count of 1 or
                2 means a stall, a count near 16 means a distribution
                shift. Takes a double so a fractional float budget promotes
                exactly instead of comparing as a float.
             */
            public int SamplesOver(double budgetMilliseconds)
            {
                int over = 0;
                foreach (double sample in _sortedWarmMilliseconds)
                {
                    if (budgetMilliseconds <= sample)
                    {
                        over++;
                    }
                }

                return over;
            }
        }
    }
#endif
}
