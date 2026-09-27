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
        for gross tail blowups - so two stalled samples cannot fail a
        shared host editor (#170). Editor-only: players cannot emit IL.

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
            The warm tripwire asserts the median at the tier budget and p95
            at this multiple of it (#170). n=30 puts p95 on the 29th sorted
            sample, the second worst, so two stalled samples cross a p95
            bound; the median is an interior statistic that needs 16 stalls
            to move. 4x sits 4.7x above the worst single sample ever
            recorded at the 1,000-command tier, which is the point: this leg
            is a gross-tail bound, and the median leg is what catches a
            whole-distribution regression. It is a 4x loosening at every
            tier; see the budget comment for what that costs where the old
            bound was closest.
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
            then-15 ms tripwire - still 2x over the tightened 8 ms budget) -
            not ordinary scheduler jitter, and not portable across machines.
            The warm bound is split in two, because one bound cannot serve
            both audiences on a shared host (#170): the median is asserted
            at the tier budget, and p95 at TailTripwireMultiplier times it.
            This is a strict relaxation at every tier and it is disclosed
            here rather than buried. The old bound tolerated 1 of 30
            samples at or over budget; the split tolerates 15 of 30, plus
            one more anywhere up to 4x over. So a 2-to-15-sample
            over-budget population that used to fail now passes, and the
            top two tiers lose the most: at the 10,000-command tier the
            tail leg moves from 150 ms to 600 ms against a measured 52 ms
            p95, so only a 2-of-30 population past 11x trips it, and the
            median leg needs 16 of 30. That tier's warm tail is documented
            allocator/GC dominated, so a 4x loosening there is the cost of
            the same change that stops the stall.
            What the split buys is narrower than "more headroom": the
            median leg's headroom is 1.82x on the worst observed median
            (4.403 ms), so a host-wide slowdown of ~1.9x still trips it,
            exactly as it tripped #170. Only 1-2 stalled samples become
            survivable. The regression the budget was sized against (a
            whole-distribution shift to ~19 ms) is caught by the median leg
            at the same 8 ms threshold, on a statistic that needs 16 stalls
            to move instead of 2.
            Session-082 series on the pinned editor (2026-09-27, five runs,
            1,000-command tier): cold 8.598-18.130 ms (budget 40), median
            4.235-4.403 ms (budget 8), p95 4.476-4.662 ms, max 4.592-6.761
            ms (tail bound 32) - the median spread is 4.0% and the max
            spread 47%, and a 300-sample window in the same session carried
            a 13.944 ms single sample against a 0.063 ms median. Measured
            deferred-
            binding numbers the budgets are set from: reflected tiers
            0-1,000 warm p95 ~2.6-10.6 ms (cold 2.5-11.5 ms), 10,000 tier
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

        private static ReadinessReport MeasureReadiness()
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
                return new ReadinessReport(coldShell, coldMilliseconds, samples);
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
            Assert.Less(
                readiness.ColdMilliseconds,
                coldBudgetMilliseconds,
                $"Cold readiness tripwire crossed at {commandCount} commands: "
                    + $"statistic=cold n=1 measured={readiness.ColdMilliseconds:F3} ms "
                    + $"budget={coldBudgetMilliseconds:F3} ms "
                    + $"margin={coldBudgetMilliseconds - readiness.ColdMilliseconds:F3} ms"
            );
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
            Debug.Log(
                $"[DxCommandTerminal][Scale] {detail} "
                    + $"assemblies={AppDomain.CurrentDomain.GetAssemblies().Length} "
                    + $"cold={readiness.ColdMilliseconds:F3}ms "
                    + $"coldBudget={coldBudgetMilliseconds:F3}ms "
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

            ReadinessReport readiness = MeasureReadiness();

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

            ReadinessReport readiness = MeasureReadiness();

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
            ReadinessReport readiness = MeasureReadiness();

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

            private readonly double[] _sortedWarmMilliseconds;

            public ReadinessReport(
                CommandShell shell,
                double coldMilliseconds,
                double[] sortedWarmMilliseconds
            )
            {
                Shell = shell;
                ColdMilliseconds = coldMilliseconds;
                _sortedWarmMilliseconds = sortedWarmMilliseconds;
                SampleCount = sortedWarmMilliseconds.Length;
                Median = Percentile(sortedWarmMilliseconds, 0.5);
                Percentile95 = Percentile(sortedWarmMilliseconds, 0.95);
                Maximum = sortedWarmMilliseconds[sortedWarmMilliseconds.Length - 1];
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
