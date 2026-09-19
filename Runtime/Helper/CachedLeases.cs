namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;

    /*
        The right to return a rented buffer exactly once, carried by value
        so a struct scope can hold it without allocating. Adapted from
        unity-helpers' DisposalLease/DisposalLeases (MIT,
        github.com/ambiguous-interactive/unity-helpers).

        Why this exists: a struct scope that tracks disposal in one of its
        own fields is not copy-safe - a copy carries its own flag, so
        disposing both copies runs the disposal twice (returning one buffer
        to two live callers). Here the generation lives outside the struct,
        so every copy reads the same one and exactly one copy wins the
        claim. Each Acquire also hands out a distinct slot, so nested rents
        never share a buffer (re-entrancy), and slots recycle through a
        per-thread free list, so the warm path allocates nothing.
     */
    internal readonly struct CachedLease
    {
        /// <summary>
        /// True while this lease is still the current holder of its slot:
        /// not default, not yet claimed, and not superseded by another copy
        /// that claimed first.
        /// </summary>
        public bool IsHeld
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _slot != 0 && CachedLeases.CurrentGeneration(_slot) == _generation;
        }

        /// <summary>The slot behind this lease, for slot-indexed storage.</summary>
        internal int Slot => _slot;

        // Slot 0 is never handed out, so default reads as "not held".
        private readonly int _slot;
        private readonly long _generation;

        internal CachedLease(int slot, long generation)
        {
            _slot = slot;
            _generation = generation;
        }

        /// <summary>
        /// Claims the right to dispose and hands the slot back for reuse,
        /// for the one copy still holding the current generation. Every
        /// other copy of this lease claims nothing, forever.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryClaim()
        {
            if (_slot == 0 || !CachedLeases.TryClaim(_slot, _generation))
            {
                return false;
            }

            CachedLeases.Release(_slot);
            return true;
        }
    }

    /// <summary>
    /// The generations behind every <see cref="CachedLease"/>, stored so
    /// that acquiring, claiming, and recycling a lease all allocate
    /// nothing. Generations live in fixed-size blocks that are never
    /// reallocated (an Interlocked target must not move); growth appends a
    /// block. Slot recycling is per-thread through an intrusive free list,
    /// so balanced rent/return on one thread touches no atomics.
    /// </summary>
    internal static class CachedLeases
    {
        private const int BlockShift = 10;
        private const int BlockSize = 1 << BlockShift;
        private const int BlockMask = BlockSize - 1;

        // Slot 0 is burned so it can mean "not held"; _nextNewSlot starts at 1.
        private static long[][] _generations = { new long[BlockSize] };
        private static int[][] _freeNext = { new int[BlockSize] };
        private static int _blockCount = 1;
        private static int _nextNewSlot = 1;

        private static readonly object GrowthGate = new();

        [ThreadStatic]
        private static int _freeHead;

        public static CachedLease Acquire()
        {
            int slot = _freeHead;
            if (slot != 0)
            {
                _freeHead = FreeNextOf(slot);
            }
            else
            {
                slot = Interlocked.Increment(ref _nextNewSlot) - 1;
                EnsureBlock(slot >> BlockShift);
            }

            ref long generation = ref GenerationRef(slot);
            long acquired = generation + 1;
            Volatile.Write(ref generation, acquired);
            return new CachedLease(slot, acquired);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long CurrentGeneration(int slot)
        {
            return Interlocked.Read(ref GenerationRef(slot));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryClaim(int slot, long generation)
        {
            return Interlocked.CompareExchange(ref GenerationRef(slot), generation + 1, generation)
                == generation;
        }

        /*
            Unsynchronized by design: _freeHead is ThreadStatic, so this
            touches only the calling thread's list. A slot reaches this list
            only through a winning TryClaim, which elects exactly one owner,
            so two threads can never write the same free-next entry.
         */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Release(int slot)
        {
            SetFreeNext(slot, _freeHead);
            _freeHead = slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref long GenerationRef(int slot)
        {
            return ref Volatile.Read(ref _generations)[slot >> BlockShift][slot & BlockMask];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FreeNextOf(int slot)
        {
            return Volatile.Read(ref _freeNext)[slot >> BlockShift][slot & BlockMask];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetFreeNext(int slot, int next)
        {
            Volatile.Read(ref _freeNext)[slot >> BlockShift][slot & BlockMask] = next;
        }

        private static void EnsureBlock(int block)
        {
            if (block < Volatile.Read(ref _blockCount))
            {
                return;
            }

            lock (GrowthGate)
            {
                int required = block + 1;
                if (required <= _blockCount)
                {
                    return;
                }

                if (_generations.Length < required)
                {
                    int capacity = _generations.Length * 2;
                    while (capacity < required)
                    {
                        capacity *= 2;
                    }

                    long[][] grownGenerations = new long[capacity][];
                    int[][] grownFree = new int[capacity][];
                    Array.Copy(_generations, grownGenerations, _blockCount);
                    Array.Copy(_freeNext, grownFree, _blockCount);
                    for (int i = _blockCount; i < required; ++i)
                    {
                        grownGenerations[i] = new long[BlockSize];
                        grownFree[i] = new int[BlockSize];
                    }

                    Volatile.Write(ref _generations, grownGenerations);
                    Volatile.Write(ref _freeNext, grownFree);
                }
                else
                {
                    for (int i = _blockCount; i < required; ++i)
                    {
                        _generations[i] = new long[BlockSize];
                        _freeNext[i] = new int[BlockSize];
                    }
                }

                Volatile.Write(ref _blockCount, required);
            }
        }
    }
}
