namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System.Text;

    /// <summary>
    ///     One observed allocation probe window. A zero-allocation claim is
    ///     sound only through <see cref="IsZeroAllocation"/>: a validated
    ///     instrument observed no allocations, and no trusted byte reading
    ///     contradicts that. Unavailable capabilities weaken the claim
    ///     instead of silently supporting it.
    /// </summary>
    internal readonly struct AllocationMeasurement
    {
        /// <summary>Sentinel for a capability that could not be read.</summary>
        public const long Unavailable = -1;

        public AllocationMeasurementStatus Status { get; }

        /// <summary>Whether the validated instrument observed allocations in
        /// the window. Meaningful only when the window was measured.</summary>
        public bool DetectedAllocations { get; }

        /// <summary>Trusted bytes allocated on the measured thread while the
        /// window was open, or <see cref="Unavailable"/> when the byte reader
        /// could not be trusted for this window. An unvalidated reading is
        /// never mapped to zero.</summary>
        public long AllocatedBytes { get; }

        /// <summary>
        ///     Whether this measurement proves zero allocations: a validated
        ///     instrument observed no allocations and no trusted reading says
        ///     otherwise. Byte readings are evidence only when the byte
        ///     capability validated; an unavailable reading leaves the claim
        ///     resting on the validated instrument alone.
        /// </summary>
        public bool IsZeroAllocation
        {
            get
            {
                return Status == AllocationMeasurementStatus.Measured
                    && !DetectedAllocations
                    && (AllocatedBytes == Unavailable || AllocatedBytes == 0);
            }
        }

        private AllocationMeasurement(
            AllocationMeasurementStatus status,
            bool detectedAllocations,
            long allocatedBytes
        )
        {
            Status = status;
            DetectedAllocations = detectedAllocations;
            AllocatedBytes = allocatedBytes;
        }

        public static AllocationMeasurement Measured(bool detectedAllocations, long allocatedBytes)
        {
            return new AllocationMeasurement(
                AllocationMeasurementStatus.Measured,
                detectedAllocations,
                allocatedBytes
            );
        }

        public static AllocationMeasurement Invalid(long diagnosticBytes)
        {
            return new AllocationMeasurement(
                AllocationMeasurementStatus.InstrumentInvalid,
                false,
                diagnosticBytes
            );
        }

        public string Describe()
        {
            switch (Status)
            {
                case AllocationMeasurementStatus.Measured:
                {
                    return DetectedAllocations
                        ? $"measured: allocations detected ({AllocatedBytes} trusted byte(s))"
                        : "measured: no allocations detected";
                }

                default:
                {
                    return "instrument unvalidated for this domain; readings are "
                        + $"diagnostics only ({AllocatedBytes} byte(s))";
                }
            }
        }
    }
}
