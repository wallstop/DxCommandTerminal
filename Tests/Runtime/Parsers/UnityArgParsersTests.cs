namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using Backend.Parsers;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UnityArgParsersTests
    {
        [Test]
        public void ParsesVector3FromDelimiters()
        {
            Assert.IsTrue(Vector3ArgParser.Instance.TryParse("1.1,2.2,3.3", out object v));
            Vector3 vec = (Vector3)v;
            Assert.AreEqual(1.1f, vec.x, 1e-4f);
            Assert.AreEqual(2.2f, vec.y, 1e-4f);
            Assert.AreEqual(3.3f, vec.z, 1e-4f);
        }

        [Test]
        public void ParsesColorRgba()
        {
            Assert.IsTrue(ColorArgParser.Instance.TryParse("RGBA(0.1,0.2,0.3,0.4)", out object c));
            Color col = (Color)c;
            Assert.AreEqual(0.1f, col.r, 1e-4f);
            Assert.AreEqual(0.2f, col.g, 1e-4f);
            Assert.AreEqual(0.3f, col.b, 1e-4f);
            Assert.AreEqual(0.4f, col.a, 1e-4f);
        }
    }
}
