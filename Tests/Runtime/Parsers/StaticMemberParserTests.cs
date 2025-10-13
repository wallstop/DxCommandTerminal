namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using System.Net;
    using Backend.Parsers;
    using NUnit.Framework;

    public sealed class StaticMemberParserTests
    {
        [Test]
        public void ParsesStaticMembersByName()
        {
            Assert.IsTrue(StaticMemberParser<int>.TryParse("MaxValue", out int imax));
            Assert.AreEqual(int.MaxValue, imax);

            Assert.IsTrue(StaticMemberParser<IPAddress>.TryParse("Any", out IPAddress ipAny));
            Assert.AreEqual(IPAddress.Any, ipAny);
        }

        [Test]
        public void TryGetUsesStaticMemberParser()
        {
            // Ensure no object parser interferes for IPAddress
            // (StaticMemberParser should still work)
            Assert.IsTrue(new Backend.CommandArg("Any").TryGet(out IPAddress any));
            Assert.AreEqual(IPAddress.Any, any);
        }
    }
}
