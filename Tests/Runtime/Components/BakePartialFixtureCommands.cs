namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
    using Attributes;
    using Backend;

    /*
        Player-compatibility bake fixture: a generated assembly roots private
        handlers only through a partial companion, so this declaration pins
        the companion-rooted case (the bake must not preserve it).
     */
    internal static partial class BakePartialFixtureCommands
    {
        [RegisterCommand(
            Help = "Bake fixture command rooted by a partial companion.",
            Name = "bake-partial"
        )]
        private static void PartialPrivateCommand(CommandArg[] args) { }
    }
}
