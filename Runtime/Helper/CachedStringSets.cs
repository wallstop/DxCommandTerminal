namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /*
        Pooled case-insensitive string sets. Adapted from unity-helpers'
        SetBuffers<T>/PooledResource<T> (MIT,
        github.com/ambiguous-interactive/unity-helpers): rent through a
        using statement and the scope returns the set to the pool on
        dispose, so call sites never hand-roll try/finally, and the
        ConcurrentStack pool gives every concurrent or nested rent its own
        set instead of a single shared slot.
     */
    internal static class CachedStringSets
    {
        private static readonly ConcurrentStack<HashSet<string>> IgnoreCasePool =
            new ConcurrentStack<HashSet<string>>();

        public static StringSetScope RentIgnoreCase()
        {
            if (!IgnoreCasePool.TryPop(out HashSet<string> set))
            {
                return new StringSetScope(new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            return new StringSetScope(set);
        }

        /*
            Value-based scope: the using statement disposes exactly the one
            variable it declares, so keep it uncopied - a copy shares the
            same set, and each copy's Dispose would run once per copy.
         */
        public struct StringSetScope : IDisposable
        {
            public HashSet<string> Set => _set;

            private HashSet<string> _set;
            private bool _returned;

            internal StringSetScope(HashSet<string> set)
            {
                _set = set;
                _returned = false;
            }

            public void Dispose()
            {
                HashSet<string> set = _set;
                if (set == null || _returned)
                {
                    return;
                }

                _returned = true;
                _set = null;
                set.Clear();
                IgnoreCasePool.Push(set);
            }
        }
    }
}
