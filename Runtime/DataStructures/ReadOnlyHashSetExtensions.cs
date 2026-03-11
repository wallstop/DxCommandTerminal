namespace WallstopStudios.DxCommandTerminal.DataStructures
{
    using System.Collections.Generic;

    public static class ReadOnlyHashSetExtensions
    {
        public static ReadOnlyHashSet<T> ToReadOnlyHashSet<T>(
            this IEnumerable<T> source,
            IEqualityComparer<T> comparer = null
        )
        {
            return new ReadOnlyHashSet<T>(source, comparer);
        }
    }
}
