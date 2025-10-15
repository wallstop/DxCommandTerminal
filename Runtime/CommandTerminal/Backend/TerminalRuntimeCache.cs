namespace WallstopStudios.DxCommandTerminal.Backend
{
    internal static class TerminalRuntimeCache
    {
        private static TerminalRuntime _cachedRuntime;

        public static bool TryAcquire(out TerminalRuntime runtime)
        {
            runtime = _cachedRuntime;
            _cachedRuntime = null;
            return runtime != null;
        }

        public static void Store(TerminalRuntime runtime)
        {
            if (runtime == null)
            {
                return;
            }

            _cachedRuntime = runtime;
        }

        public static void Clear()
        {
            _cachedRuntime = null;
        }
    }
}
