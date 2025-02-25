namespace DxCommandTerminal.Tests.Tests.Runtime.Components
{
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
    }
}
