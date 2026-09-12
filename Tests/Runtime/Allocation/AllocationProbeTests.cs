namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using NUnit.Framework;

    /// <summary>
    ///     Contract tests for the allocation instrumentation itself: the
    ///     positive control, the no-op control, unavailable instruments,
    ///     throwing subjects, and the unvalidated-window verdict. A red here
    ///     invalidates every allocation claim built on top.
    /// </summary>
    public sealed class AllocationProbeTests
    {
        [TearDown]
        public void TearDown()
        {
            AllocationProbe.ControlDetectionOverride = null;
            AllocationProbe.ResetValidation();
        }

        [Test]
        public void PositiveControlDetectsForcedAllocations()
        {
            AllocationMeasurement measurement = AllocationAssertions.AssertDetectsAllocation(
                "positive control",
                AllocationAssertions.ForceControlAllocation
            );

            /*
                The detection verdict and the byte reader are separate
                capabilities: a working detector does not establish byte
                accuracy, so the byte reading must agree with its capability.
            */
            if (AllocationProbe.ByteCapabilityValid)
            {
                Assert.IsTrue(
                    0 < measurement.AllocatedBytes,
                    "A validated byte reader must see the forced allocation: "
                        + measurement.Describe()
                );
                return;
            }

            Assert.AreEqual(
                AllocationMeasurement.Unavailable,
                measurement.AllocatedBytes,
                "An unvalidated byte reader must report unavailability, never a trusted zero"
            );
        }

        [Test]
        public void NoOpSubjectMeasuresZero()
        {
            AllocationAssertions.AssertZeroAllocations("no-op control", () => { });
        }

        [Test]
        public void UnvalidatedWindowCannotApproveZeroClaims()
        {
            AllocationProbe.ResetValidation();
            try
            {
                AllocationMeasurement measurement = AllocationProbe.Measure(() => { });
                Assert.IsFalse(
                    measurement.IsZeroAllocation,
                    $"An unvalidated window must never map to zero: {measurement.Describe()}"
                );
                Assert.AreEqual(
                    AllocationMeasurementStatus.InstrumentInvalid,
                    measurement.Status,
                    "An unvalidated window reports the invalid verdict"
                );
            }
            finally
            {
                AllocationProbe.ResetValidation();
            }

            AllocationAssertions.AssertZeroAllocations(
                "recovery after unvalidated window",
                () => { }
            );
        }

        [Test]
        public void UnavailableInstrumentCannotApproveZeroClaims()
        {
            AllocationProbe.ControlDetectionOverride = () => false;
            AllocationProbe.ResetValidation();
            try
            {
                Assert.Catch<AssertionException>(
                    () => AllocationAssertions.EnsureInstrumentValidated(),
                    "A required verdict must fail when the instrument cannot measure"
                );
                Assert.Catch<AssertionException>(
                    () => AllocationAssertions.AssertZeroAllocations("sealed window", () => { }),
                    "An inactive instrument must not create a green zero claim"
                );
            }
            finally
            {
                AllocationProbe.ControlDetectionOverride = null;
                AllocationProbe.ResetValidation();
            }

            AllocationAssertions.AssertZeroAllocations(
                "recovery after unavailable instrument",
                () => { }
            );
        }

        [Test]
        public void ThrowingSubjectPropagatesAndKeepsInstrumentUsable()
        {
            AllocationAssertions.EnsureInstrumentValidated();
            Assert.Throws<InvalidOperationException>(
                () =>
                    AllocationProbe.Measure(() =>
                        throw new InvalidOperationException("subject failure")
                    ),
                "The subject's exception must reach the caller"
            );

            AllocationAssertions.AssertZeroAllocations("recovery after throw", () => { });
        }
    }
}
