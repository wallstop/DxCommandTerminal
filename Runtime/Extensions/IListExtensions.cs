namespace WallstopStudios.DxCommandTerminal.Extensions
{
    using System.Collections.Generic;

    public static class IListExtensions
    {
        public static void Shift<T>(this IList<T> list, int amount)
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

        public static void Reverse<T>(this IList<T> list, int start, int end)
        {
            while (start < end)
            {
                (list[start], list[end]) = (list[end], list[start]);
                start++;
                end--;
            }
        }
    }
}
