namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using Backend.Parsers;
    using NUnit.Framework;

    public sealed class ParserTests
    {
        private sealed class StaticLike
        {
            public static StaticLike Alpha = new(1);
            public static StaticLike Beta = new(2);

            public int Value { get; }

            private StaticLike(int value)
            {
                Value = value;
            }
        }

        private enum SampleEnum
        {
            Zero,
            One,
            Two,
        }

        [Test]
        public void StaticMemberParserFindsFields()
        {
            Assert.IsTrue(StaticMemberParser<StaticLike>.TryParse("Alpha", out StaticLike a));
            Assert.IsNotNull(a);
            Assert.AreEqual(1, a.Value);

            Assert.IsTrue(StaticMemberParser<StaticLike>.TryParse("Beta", out StaticLike b));
            Assert.IsNotNull(b);
            Assert.AreEqual(2, b.Value);
        }

        [Test]
        public void EnumArgParserParsesNamesAndOrdinals()
        {
            Assert.IsTrue(EnumArgParser.TryParse(typeof(SampleEnum), "One", out object nameVal));
            Assert.AreEqual(SampleEnum.One, nameVal);

            Assert.IsTrue(EnumArgParser.TryParse(typeof(SampleEnum), "2", out object ordVal));
            Assert.AreEqual(SampleEnum.Two, ordVal);
        }
    }
}
