namespace WallstopStudios.DxCommandTerminal.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Extensions;

    /*
        A fixed-capacity ring that is safe to write from a thread other than
        the one reading it.

        Unity's threaded log callback, and any user logging off the main
        thread, write here while the terminal reads the ring every frame. The
        ring's state is three coupled values - the backing list, the write
        position, and the count - and every one of them is a separate
        read-modify-write. Unsynchronized, a reader can compute an index from
        a position and a list length that no longer agree, which is an
        IndexOutOfRangeException on a frame with nothing in the message
        pointing at threads. Every member therefore reads and writes its state
        under one lock, and Add's three updates become one atomic step.

        Read a whole window with CopyTo rather than indexing entry by entry:
        the per-frame reader wants a consistent view, and one lock acquisition
        per frame is cheaper than one per line.
     */
    [Serializable]
    internal sealed class CyclicBuffer<T> : IReadOnlyList<T>
    {
        public int Capacity
        {
            get
            {
                lock (SyncRoot)
                {
                    return _capacity;
                }
            }
        }

        public int Count
        {
            get
            {
                lock (SyncRoot)
                {
                    return _count;
                }
            }
        }

        public T this[int index]
        {
            get
            {
                lock (SyncRoot)
                {
                    BoundsCheck(index);
                    return _buffer[AdjustedIndexFor(index)];
                }
            }
            set
            {
                lock (SyncRoot)
                {
                    BoundsCheck(index);
                    _buffer[AdjustedIndexFor(index)] = value;
                }
            }
        }

        /*
            The lock every member shares. Named because a nested
            read-modify-write that has to span two members must take this
            same object - a second lock object would not exclude the first.
         */
        internal object SyncRoot { get; } = new();

        private readonly List<T> _buffer;
        private int _capacity;
        private int _count;
        private int _position;

        public CyclicBuffer(int capacity, IEnumerable<T> initialContents = null)
        {
            if (capacity < 0)
            {
                throw new ArgumentException(nameof(capacity));
            }

            _capacity = capacity;
            _position = 0;
            _count = 0;
            _buffer = new List<T>();
            foreach (T item in initialContents ?? Array.Empty<T>())
            {
                Add(item);
            }
        }

        public CyclicBufferEnumerator GetEnumerator()
        {
            return new CyclicBufferEnumerator(this);
        }

        public void Add(T item)
        {
            lock (SyncRoot)
            {
                if (_capacity == 0)
                {
                    return;
                }

                if (_position < _buffer.Count)
                {
                    _buffer[_position] = item;
                }
                else
                {
                    _buffer.Add(item);
                }

                _position = (_position + 1) % _capacity;
                if (_count < _capacity)
                {
                    ++_count;
                }
            }
        }

        public void Clear()
        {
            lock (SyncRoot)
            {
                /* Simply reset state */
                _count = 0;
                _position = 0;
                _buffer.Clear();
            }
        }

        public void Resize(int newCapacity)
        {
            if (newCapacity < 0)
            {
                throw new ArgumentException(nameof(newCapacity));
            }

            lock (SyncRoot)
            {
                _capacity = newCapacity;
                _buffer.Shift(-_position);
                if (newCapacity < _buffer.Count)
                {
                    _buffer.RemoveRange(newCapacity, _buffer.Count - newCapacity);
                }

                /*
                    The write position is the slot the next Add fills, so it
                    is a slot index into the backing list and never past its
                    end. Deriving it from _buffer.Count alone put it one past
                    the last slot whenever the ring was already full, and the
                    next Add then took the "list is full" branch and grew the
                    list past the capacity - after which the index arithmetic
                    ran off the end of it. Resizing to the capacity a full
                    ring already has reached that, with no thread involved.
                    Capacity 0 has no slot to write, which Add already
                    handles, so it parks at 0.
                 */
                _position = _capacity == 0 ? 0 : _buffer.Count % _capacity;
                _count = Math.Min(newCapacity, _count);
            }
        }

        public bool Contains(T item)
        {
            lock (SyncRoot)
            {
                return _buffer.Contains(item);
            }
        }

        /*
            Copies the visible window oldest-first into a caller-owned array
            and returns how many entries it wrote, so one read pass is one
            consistent view instead of a count followed by independent
            per-entry reads that a concurrent write could move underneath. A
            destination shorter than the window truncates; sizing it to
            Capacity always fits, because Count never exceeds it.
         */
        internal int CopyTo(T[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            lock (SyncRoot)
            {
                int count = Math.Min(_count, destination.Length);
                for (int i = 0; i < count; ++i)
                {
                    destination[i] = _buffer[AdjustedIndexFor(i)];
                }

                return count;
            }
        }

        /*
            Callers already hold the lock (every public member takes it), and
            every read of the three coupled values happens here, so the index
            is computed against one coherent state.
         */
        private int AdjustedIndexFor(int index)
        {
            long longCapacity = _capacity;
            if (longCapacity == 0L)
            {
                return 0;
            }
            unchecked
            {
                int adjustedIndex = (int)(
                    (_position - 1L + longCapacity - (_buffer.Count - 1 - index)) % longCapacity
                );
                return adjustedIndex;
            }
        }

        private void BoundsCheck(int index)
        {
            if (!InBounds(index))
            {
                throw new IndexOutOfRangeException($"{index} is outside of bounds [0, {_count})");
            }
        }

        private bool InBounds(int index)
        {
            return 0 <= index && index < _count;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct CyclicBufferEnumerator : IEnumerator<T>
        {
            public T Current => _current;

            object IEnumerator.Current => Current;

            private readonly CyclicBuffer<T> _buffer;

            private int _index;
            private T _current;

            internal CyclicBufferEnumerator(CyclicBuffer<T> buffer)
            {
                _buffer = buffer;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                lock (_buffer.SyncRoot)
                {
                    if (++_index < _buffer._count)
                    {
                        _current = _buffer._buffer[_buffer.AdjustedIndexFor(_index)];
                        return true;
                    }

                    _current = default;
                    return false;
                }
            }

            public void Reset()
            {
                _index = -1;
                _current = default;
            }

            public void Dispose() { }
        }
    }
}
