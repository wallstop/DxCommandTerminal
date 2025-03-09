namespace DxCommandTerminal.Tests.Tests.Runtime.Components
{
    using System;
    using System.Linq;
    using Attributes;
    using CommandTerminal;

    public static class TestCommands
    {
        [RegisterCommand]
        public static void TestCommand(CommandArg[] args) { }

        [RegisterCommand]
        public static void InvalidTestCommand1() { }

        [RegisterCommand]
        public static void InvalidTestCommand2(string args) { }

        [RegisterCommand]
        public static void InvalidTestCommand3(string args, string[] args2) { }

        [RegisterCommand(MinArgCount = 0, MaxArgCount = 1, Name = "generate-test-data")]
        public static void GenerateTestData(CommandArg[] args)
        {
            if (args.Length != 1 || !args.Single().TryGet(out int count))
            {
                count = 50;
            }

            Random random = new();
            foreach (int i in Enumerable.Range(0, count).OrderBy(_ => random.Next()))
            {
                Terminal.Shell?.RunCommand(
                    "log " + string.Join("a", Enumerable.Range(0, i).Select(_ => string.Empty))
                );
            }

            Terminal.Shell?.RunCommand(
                "log " + string.Join("a", Enumerable.Range(0, 1_000).Select(_ => string.Empty))
            );
        }
    }
}
