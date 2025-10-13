namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;

    public sealed class CommandHistoryTests
    {
        [Test]
        public void FiltersAndOrderWork()
        {
            CommandHistory history = new CommandHistory(10);

            history.Push("a ok", true, true);
            history.Push("b fail", false, false);
            history.Push("c ok but error", true, false);
            history.Push("d ok", true, true);

            // onlySuccess
            System.Collections.Generic.List<string> onlySuccess =
                new System.Collections.Generic.List<string>(history.GetHistory(true, false));
            Assert.AreEqual(3, onlySuccess.Count);
            Assert.AreEqual("a ok", onlySuccess[0]);
            Assert.AreEqual("c ok but error", onlySuccess[1]);
            Assert.AreEqual("d ok", onlySuccess[2]);

            // onlyErrorFree
            System.Collections.Generic.List<string> onlyErrorFree =
                new System.Collections.Generic.List<string>(history.GetHistory(false, true));
            Assert.AreEqual(2, onlyErrorFree.Count);
            Assert.AreEqual("a ok", onlyErrorFree[0]);
            Assert.AreEqual("d ok", onlyErrorFree[1]);

            // both filters
            System.Collections.Generic.List<string> both =
                new System.Collections.Generic.List<string>(history.GetHistory(true, true));
            Assert.AreEqual(2, both.Count);
            Assert.AreEqual("a ok", both[0]);
            Assert.AreEqual("d ok", both[1]);
        }
    }
}
