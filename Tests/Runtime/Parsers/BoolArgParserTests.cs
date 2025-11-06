namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using Backend.Parsers;
    using NUnit.Framework;

    public sealed class BoolArgParserTests
    {
        [Test]
        public void ParsesTrueFalse()
        {
            Assert.IsTrue(BoolArgParser.Instance.TryParse("true", out object v1));
            Assert.AreEqual(true, v1);
            Assert.IsTrue(BoolArgParser.Instance.TryParse("False", out object v2));
            Assert.AreEqual(false, v2);
        }

        [Test]
        public void RejectsInvalid()
        {
            Assert.IsFalse(BoolArgParser.Instance.TryParse("notabool", out _));
        }
    }
}
