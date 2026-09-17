namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;

    /// <summary>
    ///     Structured verdict for one allocation probe window. Only
    ///     <see cref="Measured"/> can support a zero-allocation claim; the
    ///     other states are measurement verdicts, never mapped to zero.
    /// </summary>
    internal enum AllocationMeasurementStatus
    {
        [Obsolete("Use a measured or invalid verdict")]
        Unknown = 0,

        /// <summary>A validated instrument observed the window.</summary>
        Measured = 2,

        /// <summary>The instrument was not validated for this domain, so its
        /// readings are diagnostics only.</summary>
        InstrumentInvalid = 1,
    }
}
