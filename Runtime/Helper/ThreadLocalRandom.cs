namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.Threading;

    public static class ThreadLocalRandom
    {
        public static Random Instance => LocalInstance.Value;

        public static readonly ThreadLocal<Random> LocalInstance = new(() => new Random());
    }
}
