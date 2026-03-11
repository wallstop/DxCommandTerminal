namespace WallstopStudios.DxCommandTerminal.DataStructures
{
    using System;
    using System.Collections;
    using System.Collections.Generic;

    public sealed class ReadOnlyHashSet<T> : IReadOnlyCollection<T>
    {
        public static ReadOnlyHashSet<T> Empty { get; } =
            new ReadOnlyHashSet<T>(Array.Empty<T>());

        private readonly HashSet<T> _set;

        public int Count => _set.Count;

        public IEqualityComparer<T> Comparer => _set.Comparer;

        public ReadOnlyHashSet(IEnumerable<T> source, IEqualityComparer<T> comparer = null)
        {
            _set = new HashSet<T>(source ?? throw new ArgumentNullException(nameof(source)), comparer);
        }

        public bool Contains(T item)
        {
            return _set.Contains(item);
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            return _set.SetEquals(other);
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            return _set.IsSubsetOf(other);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            return _set.IsSupersetOf(other);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            return _set.Overlaps(other);
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            return _set.IsProperSubsetOf(other);
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            return _set.IsProperSupersetOf(other);
        }

        public HashSet<T>.Enumerator GetEnumerator()
        {
            return _set.GetEnumerator();
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
