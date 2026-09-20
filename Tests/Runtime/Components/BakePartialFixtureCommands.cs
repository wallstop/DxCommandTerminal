namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
    using Attributes;
    using Backend;

    /*
        Player-compatibility bake fixtures: a generated assembly roots private
        handlers only through a partial companion, so these declarations pin
        both companion cases - the rooted valid handler (the bake must not
        preserve it) and the generic sibling the per-method companion cannot
        root (the bake must preserve it).
     */
    internal static partial class BakePartialFixtureCommands
    {
        [RegisterCommand(
            Help = "Bake fixture command rooted by a partial companion.",
            Name = "bake-partial"
        )]
        private static void PartialPrivateCommand(CommandArg[] args) { }

        /*
            Unattributed on purpose: an attributed generic handler is a
            rejected command whose readiness diagnostics queue on every
            shell, breaking the suite's no-error contracts. The rooting seam
            test drives it directly instead.
         */
        private static void PartialGenericCommand<T>(CommandArg[] args) { }
    }
}
