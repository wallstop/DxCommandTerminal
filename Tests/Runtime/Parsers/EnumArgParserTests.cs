namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using Backend.Parsers;
    using NUnit.Framework;

    public sealed class EnumArgParserTests
    {
        private enum Sample
        {
            A = 0,
            B = 1,
            C = 2,
        }

        [Test]
        public void ParsesByNameAndOrdinal()
        {
            Assert.IsTrue(EnumArgParser.TryParse(typeof(Sample), "B", out object v1));
            Assert.AreEqual(Sample.B, v1);

            Assert.IsTrue(EnumArgParser.TryParse(typeof(Sample), "2", out object v2));
            Assert.AreEqual(Sample.C, v2);
        }

        [Test]
        public void ParsesCaseInsensitive()
        {
            Assert.IsTrue(EnumArgParser.TryParse(typeof(Sample), "b", out object v));
            Assert.AreEqual(Sample.B, v);
        }

        [Test]
        public void RejectsInvalid()
        {
            Assert.IsFalse(EnumArgParser.TryParse(typeof(Sample), "Z", out _));
            Assert.IsFalse(EnumArgParser.TryParse(typeof(Sample), "100", out _));
        }
    }
}
