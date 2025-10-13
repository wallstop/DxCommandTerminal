namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UnityLabeledParsingTests
    {
        [Test]
        public void ParsesVector3WithLabeledComponents()
        {
            CommandArg arg = new CommandArg("x:1.1 y:2.2 z:3.3");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.AreEqual(1.1f, v.x, 1e-4f);
            Assert.AreEqual(2.2f, v.y, 1e-4f);
            Assert.AreEqual(3.3f, v.z, 1e-4f);
        }

        [Test]
        public void ParsesRectWithLabeledComponents()
        {
            CommandArg arg = new CommandArg("x:10 y:20 width:100 height:50");
            Assert.IsTrue(arg.TryGet(out Rect r));
            Assert.AreEqual(10f, r.x, 1e-4f);
            Assert.AreEqual(20f, r.y, 1e-4f);
            Assert.AreEqual(100f, r.width, 1e-4f);
            Assert.AreEqual(50f, r.height, 1e-4f);
        }

        [Test]
        public void ParsesQuaternionWithLabeledComponents()
        {
            CommandArg arg = new CommandArg("x:0.1,y:0.2,z:0.3,w:0.4");
            Assert.IsTrue(arg.TryGet(out Quaternion q));
            Assert.AreEqual(0.1f, q.x, 1e-4f);
            Assert.AreEqual(0.2f, q.y, 1e-4f);
            Assert.AreEqual(0.3f, q.z, 1e-4f);
            Assert.AreEqual(0.4f, q.w, 1e-4f);
        }

        [Test]
        public void ParsesVector2IntWithLabeledComponents()
        {
            CommandArg arg = new CommandArg("x:-3 y:5");
            Assert.IsTrue(arg.TryGet(out Vector2Int v));
            Assert.AreEqual(-3, v.x);
            Assert.AreEqual(5, v.y);
        }
    }
}
