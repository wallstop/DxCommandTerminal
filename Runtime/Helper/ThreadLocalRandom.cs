namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.Threading;

    internal static class ThreadLocalRandom
    {
        internal static Random Instance => LocalInstance.Value;

        internal static readonly ThreadLocal<Random> LocalInstance = new(() => new Random());
    }
}
