namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.Text;
    using UnityEngine;

    /*
        Rents one <see cref="StringBuilder" /> per lease so repeated string
        assembly (log listings, diagnostics) does not allocate on every
        call. Use through <see cref="Scope" /> in a <c>using</c> statement:
        the scope is a value that returns its builder on dispose, so call
        sites never hand-roll try/finally.

        The scope is copy-safe (the exactly-once return is leased through
        <see cref="CachedLease" />, whose generation lives outside the
        struct) and re-entrant (every rent leases a distinct slot, so
        nested scopes get distinct builders instead of one shared slot
        whose last disposer wins).

        Reclamation: a returned builder whose capacity exceeds
        <see cref="MaxRetainedBuilderCapacity" /> is dropped for the GC
        instead of being pooled, so a one-off large build (a long log
        trace, a huge theme listing) is not pinned forever. Retained
        memory is bounded by (rented slots) x (max retained capacity), and
        everything reclaims with process teardown of the static - which is
        why member collections are preferred wherever a single instance
        owns the work.
     */
    internal static class CachedStringBuilder
    {
        /// <summary>
        /// Builders above this capacity are not pooled on return; they are
        /// left for the GC, so one-off spikes reclaim instead of leaking.
        /// </summary>
        internal const int MaxRetainedBuilderCapacity = 8192;

        public static Scope Rent(int minimumCapacity)
        {
            CachedLease lease = CachedLeases.Acquire();
            int slot = lease.Slot;
            CachedSlotStorage<StringBuilder>.Ensure(slot);
            StringBuilder builder = CachedSlotStorage<StringBuilder>.Read(slot);
            if (builder == null)
            {
                builder = new StringBuilder(Mathf.Max(minimumCapacity, 16));
                CachedSlotStorage<StringBuilder>.Write(slot, builder);
            }
            else
            {
                builder.EnsureCapacity(minimumCapacity);
            }

            return new Scope(builder, lease);
        }

        public readonly struct Scope : IDisposable
        {
            public StringBuilder Builder => _builder;

            private readonly StringBuilder _builder;
            private readonly CachedLease _lease;

            public Scope(int minimumCapacity)
            {
                Scope rented = Rent(minimumCapacity);
                _builder = rented._builder;
                _lease = rented._lease;
            }

            internal Scope(StringBuilder builder, CachedLease lease)
            {
                _builder = builder;
                _lease = lease;
            }

            public void Dispose()
            {
                if (!_lease.TryClaim())
                {
                    return;
                }

                if (_builder.Capacity <= MaxRetainedBuilderCapacity)
                {
                    _builder.Clear();
                    return;
                }

                // One-off spike: reclaim the oversized buffer instead of pinning it.
                CachedSlotStorage<StringBuilder>.Write(_lease.Slot, null);
            }
        }
    }
}
