namespace WallstopStudios.DxCommandTerminal.Extensions
{
    using System;
    using System.Collections.Generic;
    using Object = UnityEngine.Object;

    internal sealed class UnityObjectNameComparer : IComparer<Object>
    {
        public static readonly UnityObjectNameComparer Instance = new();

        private UnityObjectNameComparer() { }

        public int Compare(Object x, Object y)
        {
            if (x == y)
            {
                return 0;
            }

            if (y == null)
            {
                return 1;
            }

            if (x == null)
            {
                return -1;
            }

            return string.Compare(x.name, y.name, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal static class IListExtensions
    {
        internal static void Shift<T>(this IList<T> list, int amount)
        {
            int count = list.Count;
            if (count <= 1)
            {
                return;
            }

            amount %= count;
            amount += count;
            amount %= count;

            if (amount == 0)
            {
                return;
            }

            Reverse(list, 0, count - 1);
            Reverse(list, 0, amount - 1);
            Reverse(list, amount, count - 1);
        }

        internal static void Reverse<T>(this IList<T> list, int start, int end)
        {
            while (start < end)
            {
                (list[start], list[end]) = (list[end], list[start]);
                start++;
                end--;
            }
        }

        internal static void SortByName<T>(this List<T> list)
            where T : Object
        {
            list.Sort(UnityObjectNameComparer.Instance);
        }

        internal static bool IsSorted<T>(this IList<T> list, IComparer<T> comparer = null)
        {
            if (list.Count <= 1)
            {
                return true;
            }

            comparer ??= Comparer<T>.Default;

            T previous = list[0];
            for (int i = 1; i < list.Count; ++i)
            {
                T current = list[i];
                if (comparer.Compare(previous, current) > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
