/*
    NUnitLite entry point. The tests compile into this assembly (see
    testhost.csproj), so discovery needs no assembly argument: NUnitLite
    walks the entry assembly, runs every fixture, and the exit code is
    the number of failures - a red fixture fails the job, which is the
    point of the lane (issue #193).
 */
namespace WallstopStudios.DxCommandTerminal.TestHost
{
    using NUnitLite;

    public static class Program
    {
        public static int Main(string[] args)
        {
            return new AutoRun().Execute(args);
        }
    }
}
