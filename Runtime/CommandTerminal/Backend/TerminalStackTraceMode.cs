namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Controls when a <see cref="CommandLog"/> captures caller stack traces.
    ///     Extraction dominates per-log cost (measured 0.229 ms of the 0.293 ms
    ///     write on Unity 6000.4.6f1), so skipping it for routine messages is the
    ///     single largest standard-operations saving available.
    /// </summary>
    public enum TerminalStackTraceMode
    {
        [Obsolete("Use a valid value")]
        Unknown = 0,

        /// <summary>Capture a caller stack trace for every log entry (default).</summary>
        All = 1,

        /// <summary>
        ///     Capture traces only for <see cref="TerminalLogType.Error"/>,
        ///     <see cref="TerminalLogType.Assert"/>, <see cref="TerminalLogType.Exception"/>,
        ///     and <see cref="TerminalLogType.Warning"/> entries; routine
        ///     <see cref="TerminalLogType.ShellMessage"/>, <see cref="TerminalLogType.Input"/>,
        ///     and <see cref="TerminalLogType.Message"/> entries store no trace.
        /// </summary>
        ErrorsAndWarnings = 2,

        /// <summary>Never capture caller stack traces.</summary>
        Disabled = 3,
    }
}
