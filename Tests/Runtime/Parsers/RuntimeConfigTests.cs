namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using Backend;
    using NUnit.Framework;

    public sealed class RuntimeConfigTests
    {
        [Test]
        public void HasFlagNoAlloc_Works()
        {
            TerminalRuntimeModeFlags m =
                TerminalRuntimeModeFlags.Editor | TerminalRuntimeModeFlags.Development;
            Assert.IsTrue(TerminalRuntimeConfig.HasFlagNoAlloc(m, TerminalRuntimeModeFlags.Editor));
            Assert.IsTrue(
                TerminalRuntimeConfig.HasFlagNoAlloc(m, TerminalRuntimeModeFlags.Development)
            );
            Assert.IsFalse(
                TerminalRuntimeConfig.HasFlagNoAlloc(m, TerminalRuntimeModeFlags.Production)
            );
        }

        [Test]
        public void AutoDiscovery_GatedByConfig()
        {
            // Clean state
            CommandArg.UnregisterAllObjectParsers();

            TerminalRuntimeConfig.SetMode(TerminalRuntimeModeFlags.Editor);
            TerminalRuntimeConfig.EditorAutoDiscover = false;
            int added = TerminalRuntimeConfig.TryAutoDiscoverParsers();
            Assert.AreEqual(0, added);

            TerminalRuntimeConfig.EditorAutoDiscover = true;
            added = TerminalRuntimeConfig.TryAutoDiscoverParsers();
            Assert.Greater(added, 0);

            // Validate a simple parse now succeeds via discovered parsers
            CommandArg arg = new("123");
            Assert.IsTrue(arg.TryGet(out int value));
            Assert.AreEqual(123, value);
        }
    }
}
