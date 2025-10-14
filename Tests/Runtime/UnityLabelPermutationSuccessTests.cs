namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UnityLabelPermutationSuccessTests
    {
        [Test]
        public void Vector2ReorderedLabels()
        {
            CommandArg arg = new("y:2 x:1");
            Assert.IsTrue(arg.TryGet(out Vector2 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
        }

        [Test]
        public void Vector4ReorderedLabels()
        {
            CommandArg arg = new("w:4 z:3 y:2 x:1");
            Assert.IsTrue(arg.TryGet(out Vector4 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
            Assert.AreEqual(4f, v.w, 1e-4f);
        }

        [Test]
        public void ColorWithLabeledComponentsAndWrappers()
        {
            CommandArg arg = new("{r:0.1 g:0.2 b:0.3 a:0.4}");
            Assert.IsTrue(arg.TryGet(out Color c));
            Assert.AreEqual(0.1f, c.r, 1e-4f);
            Assert.AreEqual(0.2f, c.g, 1e-4f);
            Assert.AreEqual(0.3f, c.b, 1e-4f);
            Assert.AreEqual(0.4f, c.a, 1e-4f);
        }

        [Test]
        public void Vector2IntReorderedLabels()
        {
            CommandArg arg = new("y:5 x:-3");
            Assert.IsTrue(arg.TryGet(out Vector2Int v));
            Assert.AreEqual(-3, v.x);
            Assert.AreEqual(5, v.y);
        }

        [Test]
        public void Vector3IntReorderedLabels()
        {
            CommandArg arg = new("z:9 y:8 x:7");
            Assert.IsTrue(arg.TryGet(out Vector3Int v));
            Assert.AreEqual(7, v.x);
            Assert.AreEqual(8, v.y);
            Assert.AreEqual(9, v.z);
        }
    }
}
