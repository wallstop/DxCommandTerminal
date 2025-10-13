namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using System;
    using System.Net;
    using Backend.Parsers;
    using NUnit.Framework;

    public sealed class MiscArgParsersTests
    {
        [Test]
        public void ParsesGuidAndTime()
        {
            Guid g = Guid.NewGuid();
            Assert.IsTrue(GuidArgParser.Instance.TryParse(g.ToString(), out object gv));
            Assert.AreEqual(g, gv);

            Assert.IsTrue(TimeSpanArgParser.Instance.TryParse("01:02:03", out object ts));
            Assert.AreEqual(TimeSpan.Parse("01:02:03"), ts);
        }

        [Test]
        public void ParsesDateTimeAndOffset()
        {
            string dt = "2021-08-01T12:34:56Z";
            Assert.IsTrue(DateTimeArgParser.Instance.TryParse(dt, out object dv));
            Assert.IsTrue(DateTimeOffsetArgParser.Instance.TryParse(dt, out object dov));
        }

        [Test]
        public void ParsesVersionAndIPAddress()
        {
            Assert.IsTrue(VersionArgParser.Instance.TryParse("1.2.3.4", out object v));
            Assert.AreEqual(new Version(1, 2, 3, 4), v);

            Assert.IsTrue(IPAddressArgParser.Instance.TryParse("127.0.0.1", out object ip));
            Assert.AreEqual(IPAddress.Parse("127.0.0.1"), ip);
        }
    }
}
