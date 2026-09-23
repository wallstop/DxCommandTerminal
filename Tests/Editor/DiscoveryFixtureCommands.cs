namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Attributes;
    using Backend;

    /*
        Compiled attributed fixtures for the Editor test assembly: the
        generated catalog, the TypeCache claim, and the catalog-less provider
        exclusion all key off attributed commands in the assembly itself.
     */
    internal static class DiscoveryFixtureCommands
    {
        [RegisterCommand(Help = "Editor test fixture with an inferred name.")]
        private static void EditorFixtureCommand(CommandArg[] args) { }

        [RegisterCommand(
            Help = "Editor test fixture with an explicit name.",
            Name = "editorfixture-named"
        )]
        private static void EditorFixtureNamed(CommandArg[] args) { }
    }
}
