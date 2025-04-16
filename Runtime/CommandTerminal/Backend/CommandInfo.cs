namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    public readonly struct CommandInfo
    {
        public readonly Action<CommandArg[]> proc;
        public readonly int minArgCount;
        public readonly int maxArgCount;
        public readonly string help;
        public readonly string hint;

        public CommandInfo(
            Action<CommandArg[]> proc,
            int minArgCount,
            int maxArgCount,
            string help,
            string hint
        )
        {
            this.proc = proc;
            this.maxArgCount = maxArgCount;
            this.minArgCount = minArgCount;
            this.help = help;
            this.hint = hint;
        }
    }
}
