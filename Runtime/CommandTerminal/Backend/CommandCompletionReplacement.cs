namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     An explicit replacement range for one completion. Both values are
    ///     required and validated at construction; a completion without an
    ///     override carries <c>null</c> and the completion context's range
    ///     applies.
    /// </summary>
    public readonly struct CommandCompletionReplacement
    {
        /// <summary>Start index of the replaced range within the input line.</summary>
        public int Start { get; }

        /// <summary>Length of the replaced range within the input line.</summary>
        public int Length { get; }

        public CommandCompletionReplacement(int start, int length)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(start),
                    start,
                    "Start must not be negative."
                );
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length),
                    length,
                    "Length must not be negative."
                );
            }

            Start = start;
            Length = length;
        }

        public override string ToString()
        {
            return $"[{Start}, {Length})";
        }
    }
}
