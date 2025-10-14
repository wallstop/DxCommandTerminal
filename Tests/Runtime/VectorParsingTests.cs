namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class VectorParsingTests
    {
        [Test]
        public void Vector3ParsesVariousDelimiters()
        {
            CommandArg arg = new("1.1,2.2,3.3");
            Assert.IsTrue(arg.TryGet(out Vector3 v1));
            Assert.AreEqual(1.1f, v1.x, 1e-4f);
            Assert.AreEqual(2.2f, v1.y, 1e-4f);
            Assert.AreEqual(3.3f, v1.z, 1e-4f);

            arg = new CommandArg("(1.1;2.2;3.3)");
            Assert.IsTrue(arg.TryGet(out Vector3 v2));
            Assert.AreEqual(1.1f, v2.x, 1e-4f);
            Assert.AreEqual(2.2f, v2.y, 1e-4f);
            Assert.AreEqual(3.3f, v2.z, 1e-4f);
        }

        [Test]
        public void ColorParsesRgba()
        {
            CommandArg arg = new("RGBA(0.1,0.2,0.3,0.4)");
            Assert.IsTrue(arg.TryGet(out Color c));
            Assert.AreEqual(0.1f, c.r, 1e-4f);
            Assert.AreEqual(0.2f, c.g, 1e-4f);
            Assert.AreEqual(0.3f, c.b, 1e-4f);
            Assert.AreEqual(0.4f, c.a, 1e-4f);
        }
    }
}
