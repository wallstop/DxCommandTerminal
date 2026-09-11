namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.Text;
    using UnityEngine;

    /// <summary>
    ///     Rents one <see cref="StringBuilder" /> per thread so repeated string
    ///     assembly (log listings, diagnostics) does not allocate on every call.
    ///     Rent before appending and Return in a finally block; the returned
    ///     builder keeps its capacity and is cleared on return.
    /// </summary>
    internal static class CachedStringBuilder
    {
        /*
            A static builder shared across calls would race between threads;
            ThreadStatic keeps each thread's buffer private.
         */
        [ThreadStatic]
        private static StringBuilder _cached;

        public static StringBuilder Rent(int minimumCapacity)
        {
            StringBuilder builder = _cached;
            _cached = null;
            if (builder == null)
            {
                return new StringBuilder(Mathf.Max(minimumCapacity, 16));
            }

            builder.EnsureCapacity(minimumCapacity);
            return builder;
        }

        public static void Return(StringBuilder builder)
        {
            builder.Clear();
            _cached = builder;
        }
    }
}
