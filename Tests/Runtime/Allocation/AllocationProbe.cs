namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using System.Runtime.ExceptionServices;
    using NUnit.Framework;
    using Is = UnityEngine.TestTools.Constraints.Is;

    /// <summary>
    ///     Allocation instrumentation for this domain. The primary
    ///     instrument is Unity's own allocation constraint over the profiler
    ///     "Allocated In Frame" counter; the byte reader reads
    ///     <see cref="GC.GetAllocatedBytesForCurrentThread"/> as a secondary
    ///     capability, windowed around the subject call only so probe
    ///     overhead is never attributed to the subject. Nothing is trusted
    ///     before the positive control proves it detects a deliberate
    ///     allocation (adapted from unity-helpers, MIT).
    /// </summary>
    internal static class AllocationProbe
    {
        /// <summary>Whether the validated instrument can observe windows.</summary>
        public static bool InstrumentValid => _instrumentValid;

        /// <summary>Whether the thread byte reader proved it detects
        /// allocations. Independent of the primary instrument.</summary>
        public static bool ByteCapabilityValid => _byteCapabilityValid;

        /// <summary>
        ///     Test seam replacing the live positive-control verdict. Null
        ///     runs the real measurement.
        /// </summary>
        internal static Func<bool> ControlDetectionOverride;

        private static bool _validated;
        private static bool _instrumentValid;
        private static bool _byteCapabilityValid;

        /// <summary>
        ///     Runs the positive control once per domain and caches the
        ///     capability verdicts. A forced allocation must be detected for
        ///     the instrument to count as valid; the byte capability is
        ///     validated independently and may legitimately stay unavailable.
        /// </summary>
        public static void Validate()
        {
            if (_validated)
            {
                return;
            }

            if (ControlDetectionOverride != null)
            {
                bool overrideVerdict = ControlDetectionOverride.Invoke();
                _instrumentValid = overrideVerdict;
                _byteCapabilityValid = overrideVerdict;
                _validated = true;
                return;
            }

            /*
    Warm the control loop first so JIT and first-touch costs stay
    outside the measured window. One window then validates both
    capabilities independently: the detection verdict gates the
    instrument, and the subject-scoped byte delta gates the byte
    reader. Gating the control's bytes on the capability being
    validated would make that capability impossible to approve.
*/
            AllocationAssertions.ForceControlAllocation();
            bool detected = DetectsAllocations(
                AllocationAssertions.ForceControlAllocation,
                out long controlBytes
            );
            _instrumentValid = detected;
            _byteCapabilityValid =
                controlBytes != AllocationMeasurement.Unavailable && 0 < controlBytes;
            _validated = true;
        }

        /// <summary>Clears cached capability verdicts; tests use this so a
        /// simulated unavailable instrument never leaks into other tests.</summary>
        public static void ResetValidation()
        {
            _validated = false;
            _instrumentValid = false;
            _byteCapabilityValid = false;
        }

        /// <summary>
        ///     Runs one window around <paramref name="subject"/> and returns
        ///     what was observed. Without a validated instrument the readings
        ///     are diagnostics only (<see
        ///     cref="AllocationMeasurementStatus.InstrumentInvalid"/>) and a
        ///     measurement can never map to a zero claim.
        /// </summary>
        public static AllocationMeasurement Measure(Action subject)
        {
            if (!_instrumentValid)
            {
                TryReadThreadAllocatedBytes(out long diagnosticBytes);
                return AllocationMeasurement.Invalid(diagnosticBytes);
            }

            return MeasureWindow(subject);
        }

        /// <summary>
        ///     Reads bytes allocated on the current thread since its creation.
        ///     Existence of the API does not establish accuracy; the positive
        ///     control decides whether readings can be trusted
        ///     (<see cref="ByteCapabilityValid"/>).
        /// </summary>
        private static bool TryReadThreadAllocatedBytes(out long bytes)
        {
            try
            {
                bytes = GC.GetAllocatedBytesForCurrentThread();
                return true;
            }
            catch (Exception)
            {
                bytes = AllocationMeasurement.Unavailable;
                return false;
            }
        }

        private static AllocationMeasurement MeasureWindow(Action subject)
        {
            bool detected = DetectsAllocations(subject, out long subjectBytes);

            long allocatedBytes = AllocationMeasurement.Unavailable;
            if (_byteCapabilityValid && subjectBytes != AllocationMeasurement.Unavailable)
            {
                allocatedBytes = subjectBytes;
            }

            return AllocationMeasurement.Measured(detected, allocatedBytes);
        }

        private static bool DetectsAllocations(Action subject, out long subjectBytes)
        {
            /*
                The constraint captures the delegate's exceptions into its own
                failure verdict, so guard the subject and rethrow afterwards:
                a throwing subject must reach the caller, not masquerade as a
                clean no-allocation window.

                The byte reads bracket only the subject call, inside the
                guarded delegate. Probe overhead (the guard closure, the
                TestDelegate, the constraint, NUnit machinery) is allocated
                before the delegate runs, so a subject-scoped window never
                attributes it to the subject.
            */
            Exception subjectFailure = null;
            long innerStart = AllocationMeasurement.Unavailable;
            long innerEnd = AllocationMeasurement.Unavailable;
            Action guardedSubject = () =>
            {
                TryReadThreadAllocatedBytes(out innerStart);
                try
                {
                    subject();
                }
                catch (Exception failure)
                {
                    subjectFailure = failure;
                }

                TryReadThreadAllocatedBytes(out innerEnd);
            };

            bool detected;
            try
            {
                Assert.That(new TestDelegate(guardedSubject), Is.AllocatingGCMemory());
                detected = true;
            }
            catch (AssertionException)
            {
                detected = false;
            }

            if (subjectFailure != null)
            {
                ExceptionDispatchInfo.Capture(subjectFailure).Throw();
            }

            subjectBytes =
                innerStart != AllocationMeasurement.Unavailable
                && innerEnd != AllocationMeasurement.Unavailable
                    ? innerEnd - innerStart
                    : AllocationMeasurement.Unavailable;
            return detected;
        }
    }
}
