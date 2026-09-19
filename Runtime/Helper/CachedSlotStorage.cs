namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /*
        Slot-indexed storage for cached buffers, keyed by
        <see cref="CachedLease"/>. A slot's buffer is touched only by the
        thread that leased it, so reads and writes are plain array access
        through a Volatile-published outer array; growth copies under a
        lock and publishes, never in place, because a concurrent claim's
        Interlocked target lives in the lease blocks (which never move) -
        a stale outer array at worst costs one lazily recreated buffer.
     */
    internal static class CachedSlotStorage<T>
    {
        private static readonly object GrowthGate = new();
        private static T[] _slots = new T[16];

        public static T Read(int slot)
        {
            return Volatile.Read(ref _slots)[slot];
        }

        public static void Write(int slot, T value)
        {
            Volatile.Read(ref _slots)[slot] = value;
        }

        public static void Ensure(int slot)
        {
            if (slot < Volatile.Read(ref _slots).Length)
            {
                return;
            }

            lock (GrowthGate)
            {
                if (slot < _slots.Length)
                {
                    return;
                }

                int capacity = _slots.Length * 2;
                while (capacity <= slot)
                {
                    capacity *= 2;
                }

                T[] grown = new T[capacity];
                Array.Copy(_slots, grown, _slots.Length);
                Volatile.Write(ref _slots, grown);
            }
        }
    }
}
