namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using NUnit.Framework;

    /// <summary>
    ///     Assertion helpers over <see cref="AllocationProbe"/>. Every
    ///     required verdict first runs the positive control: an instrument
    ///     that cannot see a forced allocation cannot approve a zero claim,
    ///     and such a window fails instead of passing.
    /// </summary>
    internal static class AllocationAssertions
    {
        /// <summary>Control-loop iteration count; small enough to be cheap,
        /// large enough to rise above recorder noise.</summary>
        public const int ControlIterations = 256;

        public const int DefaultWarmupIterations = 8;

        private static string _controlSink;

        /// <summary>
        ///     Validates the instrumentation, failing the test when the
        ///     instrument cannot prove it detects a forced allocation.
        /// </summary>
        public static void EnsureInstrumentValidated()
        {
            AllocationProbe.Validate();
            if (!AllocationProbe.InstrumentValid)
            {
                Assert.Fail(
                    "Allocation instrument cannot detect a forced allocation on this platform; "
                        + "zero-allocation claims would be unproven"
                );
            }
        }

        /// <summary>
        ///     Warms up and then measures <paramref name="subject"/>, failing
        ///     unless a validated instrument reports zero allocations. Warm-up
        ///     runs outside the window; only the final invocation is measured.
        /// </summary>
        public static void AssertZeroAllocations(
            string label,
            Action subject,
            int warmupIterations = DefaultWarmupIterations
        )
        {
            EnsureInstrumentValidated();
            for (int index = 0; index < warmupIterations; ++index)
            {
                subject();
            }

            AllocationMeasurement measurement = AllocationProbe.Measure(subject);
            Assert.IsTrue(
                measurement.IsZeroAllocation,
                $"{label} must not allocate: {measurement.Describe()}"
            );
        }

        /// <summary>
        ///     Measures <paramref name="subject"/>, failing when no allocation
        ///     is observed. The returned measurement supports further
        ///     capability checks.
        /// </summary>
        public static AllocationMeasurement AssertDetectsAllocation(string label, Action subject)
        {
            EnsureInstrumentValidated();
            AllocationMeasurement measurement = AllocationProbe.Measure(subject);
            Assert.IsTrue(
                measurement.Status == AllocationMeasurementStatus.Measured
                    && measurement.DetectedAllocations,
                $"{label} must report allocations: {measurement.Describe()}"
            );
            return measurement;
        }

        /// <summary>Forces a real allocation into a static sink; the positive
        /// control and instrument validation both use this. The sink is
        /// static so nothing can prove the allocation dead and optimize it
        /// away.</summary>
        public static void ForceControlAllocation()
        {
            for (int index = 0; index < ControlIterations; ++index)
            {
                _controlSink = new string('x', 8 + (index & 7));
            }
        }
    }
}
