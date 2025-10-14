namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using Backend;
    using NUnit.Framework;

    public sealed class ParserDiscoveryTests
    {
        [Test]
        public void DiscoversAndRegistersBuiltInParsers()
        {
            int removed = CommandArg.UnregisterAllObjectParsers();
            Assert.GreaterOrEqual(removed, 0);

            // Without object parsers, numeric parsing should fail
            CommandArg arg = new("42");
            Assert.IsFalse(arg.TryGet(out int _));

            // Discover and register all IArgParser implementations in loaded assemblies
            int added = CommandArg.DiscoverAndRegisterParsers(replaceExisting: true);
            Assert.Greater(added, 0);

            // Now parsing should succeed via discovered parsers
            Assert.IsTrue(arg.TryGet(out int value));
            Assert.AreEqual(42, value);
        }
    }
}
