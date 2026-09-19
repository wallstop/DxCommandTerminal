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
        gate, which stays a pinned-environment measurement. Editor-only:
        players cannot emit IL.

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

        private static readonly Assembly[] FillerAssemblies = CreateFillerAssemblies();

        private readonly List<IDisposable> _handles = new();

        /*
            Budgets are tripwires, set from measured Unity 6000.4.6f1 numbers
            and sized to catch the #36-class broad-scan regressions and gross
            per-command binding slowdowns, not ordinary scheduler jitter.
            Measured at the 1,000-command gate tier: warm median ~12 ms,
            p95 ~19 ms; the 10,000-command stress tier: cold ~88 ms, warm
            p95 ~112 ms.
         */
        private static IEnumerable<TestCaseData> VolumeCases()
        {
            yield return new TestCaseData(0, 250f, 100f).SetName("Volume.Empty");
            yield return new TestCaseData(10, 250f, 100f).SetName("Volume.TenCommands");
            yield return new TestCaseData(100, 250f, 100f).SetName("Volume.HundredCommands");
            yield return new TestCaseData(1000, 250f, 100f).SetName("Volume.ThousandCommands");
            yield return new TestCaseData(10000, 500f, 250f).SetName("Volume.TenThousandCommands");
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
             */
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
            float warmP95BudgetMilliseconds
        )
        {
            Assert.Less(
                readiness.ColdMilliseconds,
                coldBudgetMilliseconds,
                $"Cold readiness exceeded the tripwire ({readiness.ColdMilliseconds:F3} ms "
                    + $">= {coldBudgetMilliseconds} ms) at {commandCount} commands"
            );
            Assert.Less(
                readiness.Percentile95,
                warmP95BudgetMilliseconds,
                $"Warm readiness p95 exceeded the tripwire ({readiness.Percentile95:F3} ms "
                    + $">= {warmP95BudgetMilliseconds} ms) at {commandCount} commands"
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
            float warmP95BudgetMilliseconds
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
                warmP95BudgetMilliseconds
            );

            Debug.Log(
                $"[DxCommandTerminal][Scale] readiness path=reflected commands={commandCount} "
                    + $"assemblies={AppDomain.CurrentDomain.GetAssemblies().Length} "
                    + $"registered={readiness.Shell.AutoRegisteredCommands.Count} "
                    + $"cold={readiness.ColdMilliseconds:F3}ms warmSamples={WarmShellCount} "
                    + $"median={readiness.Median:F3}ms p95={readiness.Percentile95:F3}ms "
                    + $"max={readiness.Maximum:F3}ms"
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
            AssertReadinessUnderTripwires(readiness, GateTierCommandCount, 250f, 100f);

            Debug.Log(
                $"[DxCommandTerminal][Scale] readiness path=provider commands={GateTierCommandCount} "
                    + $"assemblies={AppDomain.CurrentDomain.GetAssemblies().Length} "
                    + $"registered={readiness.Shell.AutoRegisteredCommands.Count} "
                    + $"cold={readiness.ColdMilliseconds:F3}ms warmSamples={WarmShellCount} "
                    + $"median={readiness.Median:F3}ms p95={readiness.Percentile95:F3}ms "
                    + $"max={readiness.Maximum:F3}ms"
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
                $"Classifying {FillerAssemblyCount} filler assemblies took "
                    + $"{classificationMilliseconds:F3} ms >= {ClassificationTripwireMilliseconds} ms"
            );

            Assembly volumeAssembly = CreateVolumeAssembly(GateTierCommandCount);
            _handles.Add(CommandShell.IncludeDiscoveryAssembly(volumeAssembly));
            ReadinessReport readiness = MeasureReadiness();

            Assert.GreaterOrEqual(
                readiness.Shell.AutoRegisteredCommands.Count,
                GateTierCommandCount,
                "Every volume command should register in the inflated domain"
            );
            AssertReadinessUnderTripwires(readiness, GateTierCommandCount, 250f, 100f);

            Debug.Log(
                $"[DxCommandTerminal][Scale] domain fillers={FillerAssemblyCount} "
                    + $"assemblies={AppDomain.CurrentDomain.GetAssemblies().Length} "
                    + $"classification={classificationMilliseconds:F3}ms "
                    + $"(all {FillerAssemblyCount} fillers, "
                    + $"{classificationMilliseconds * 1000.0 / FillerAssemblyCount:F2}us each) "
                    + $"cold={readiness.ColdMilliseconds:F3}ms warmSamples={WarmShellCount} "
                    + $"median={readiness.Median:F3}ms p95={readiness.Percentile95:F3}ms "
                    + $"max={readiness.Maximum:F3}ms"
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

            public ReadinessReport(
                CommandShell shell,
                double coldMilliseconds,
                double[] sortedWarmMilliseconds
            )
            {
                Shell = shell;
                ColdMilliseconds = coldMilliseconds;
                Median = Percentile(sortedWarmMilliseconds, 0.5);
                Percentile95 = Percentile(sortedWarmMilliseconds, 0.95);
                Maximum = sortedWarmMilliseconds[sortedWarmMilliseconds.Length - 1];
            }
        }
    }
#endif
}
