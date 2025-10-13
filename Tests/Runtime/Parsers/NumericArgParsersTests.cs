namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using Backend.Parsers;
    using NUnit.Framework;

    public sealed class NumericArgParsersTests
    {
        [Test]
        public void ParsesIntegers()
        {
            Assert.IsTrue(IntArgParser.Instance.TryParse("123", out object i));
            Assert.AreEqual(123, i);

            Assert.IsTrue(UIntArgParser.Instance.TryParse("456", out object ui));
            Assert.AreEqual(456u, ui);

            Assert.IsTrue(LongArgParser.Instance.TryParse("9223372036854775807", out object l));
            Assert.AreEqual(9223372036854775807L, l);
        }

        [Test]
        public void ParsesFloatsDoubles()
        {
            Assert.IsTrue(FloatArgParser.Instance.TryParse("3.14", out object f));
            Assert.AreEqual(3.14f, (float)f, 1e-4f);

            Assert.IsTrue(DoubleArgParser.Instance.TryParse("2.71828", out object d));
            Assert.AreEqual(2.71828d, d);
        }

        [Test]
        public void RejectsInvalidNumbers()
        {
            Assert.IsFalse(IntArgParser.Instance.TryParse("12x", out _));
            Assert.IsFalse(FloatArgParser.Instance.TryParse("x.y", out _));
        }
    }
}
