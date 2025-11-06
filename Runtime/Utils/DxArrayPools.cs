namespace WallstopStudios.DxCommandTerminal.Utils
{
    using System;
    using System.Collections.Concurrent;

    internal static class DxArrayPool<T>
    {
        private static readonly ConcurrentDictionary<int, ConcurrentStack<T[]>> Pool = new();
        private static readonly Action<T[]> OnDispose = Release;

        internal static ArrayLease<T> Get(int size)
        {
            return Get(size, out _);
        }

        internal static ArrayLease<T> Get(int size, out T[] array)
        {
            switch (size)
            {
                case < 0:
                    throw new ArgumentOutOfRangeException(
                        nameof(size),
                        size,
                        "Must be non-negative."
                    );
                case 0:
                    array = Array.Empty<T>();
                    return new ArrayLease<T>(array, _ => { });
            }

            ConcurrentStack<T[]> stack = Pool.GetOrAdd(size, _ => new ConcurrentStack<T[]>());
            if (!stack.TryPop(out array))
            {
                array = new T[size];
            }

            return new ArrayLease<T>(array, OnDispose);
        }

        private static void Release(T[] array)
        {
            int length = array.Length;
            if (length == 0)
            {
                return;
            }
            Array.Clear(array, 0, length);
            Pool.GetOrAdd(length, _ => new ConcurrentStack<T[]>()).Push(array);
        }
    }

    internal static class DxFastArrayPool<T>
    {
        private static readonly ConcurrentDictionary<int, ConcurrentStack<T[]>> Pool = new();
        private static readonly Action<T[]> OnDispose = Release;

        internal static ArrayLease<T> Get(int size)
        {
            return Get(size, out _);
        }

        internal static ArrayLease<T> Get(int size, out T[] array)
        {
            switch (size)
            {
                case < 0:
                    throw new ArgumentOutOfRangeException(
                        nameof(size),
                        size,
                        "Must be non-negative."
                    );
                case 0:
                    array = Array.Empty<T>();
                    return new ArrayLease<T>(array, _ => { });
            }

            ConcurrentStack<T[]> stack = Pool.GetOrAdd(size, _ => new ConcurrentStack<T[]>());
            if (!stack.TryPop(out array))
            {
                array = new T[size];
            }

            return new ArrayLease<T>(array, OnDispose);
        }

        private static void Release(T[] array)
        {
            int length = array.Length;
            if (length == 0)
            {
                return;
            }
            Pool.GetOrAdd(length, _ => new ConcurrentStack<T[]>()).Push(array);
        }
    }

    internal readonly struct ArrayLease<T> : IDisposable
    {
        public readonly T[] Array;
        private readonly Action<T[]> _onDispose;
        private readonly bool _initialized;

        internal ArrayLease(T[] array, Action<T[]> onDispose)
        {
            _initialized = true;
            Array = array ?? System.Array.Empty<T>();
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            if (!_initialized)
            {
                return;
            }
            _onDispose?.Invoke(Array);
        }
    }
}
