namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.Text;
    using UnityEngine;

    /// <summary>
    ///     Rents one <see cref="StringBuilder" /> per thread so repeated string
    ///     assembly (log listings, diagnostics) does not allocate on every call.
    ///     Use through <see cref="Scope" /> in a <c>using</c> statement, which
    ///     returns the builder on scope exit without allocating: the scope is a
    ///     stack-only struct, and the rented builder keeps its capacity.
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

        /// <summary>
        ///     Amortized zero-allocation scope for one rental: constructs with
        ///     <see cref="CachedStringBuilder.Rent" /> and returns the builder
        ///     on dispose. Use in a <c>using</c> statement; never store the
        ///     scope or its <see cref="Builder" /> beyond the using block.
        /// </summary>
        public readonly struct Scope : IDisposable
        {
            public StringBuilder Builder => _builder;

            private readonly StringBuilder _builder;

            public Scope(int minimumCapacity)
            {
                _builder = Rent(minimumCapacity);
            }

            public void Dispose()
            {
                _builder?.Clear();
                _cached = _builder;
            }
        }
    }
}
