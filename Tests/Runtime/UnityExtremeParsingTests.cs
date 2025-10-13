namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UnityExtremeParsingTests
    {
        [Test]
        public void ExtremeVector3LargeMagnitudes()
        {
            CommandArg arg = new CommandArg("1e20,-2e20,3.4e10");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.IsFalse(float.IsNaN(v.x) || float.IsInfinity(v.x));
            Assert.IsFalse(float.IsNaN(v.y) || float.IsInfinity(v.y));
            Assert.IsFalse(float.IsNaN(v.z) || float.IsInfinity(v.z));
            Assert.Greater(v.x, 0f);
            Assert.Less(v.y, 0f);
            Assert.Greater(v.z, 0f);
        }

        [Test]
        public void ExtremeVector4LargeMagnitudes()
        {
            CommandArg arg = new CommandArg("-5e15,6e15,-7e15,8e15");
            Assert.IsTrue(arg.TryGet(out Vector4 v));
            Assert.IsFalse(float.IsNaN(v.x) || float.IsInfinity(v.x));
            Assert.IsFalse(float.IsNaN(v.y) || float.IsInfinity(v.y));
            Assert.IsFalse(float.IsNaN(v.z) || float.IsInfinity(v.z));
            Assert.IsFalse(float.IsNaN(v.w) || float.IsInfinity(v.w));
            Assert.Less(v.x, 0f);
            Assert.Greater(v.y, 0f);
            Assert.Less(v.z, 0f);
            Assert.Greater(v.w, 0f);
        }

        [Test]
        public void ExtremeRectLargeMagnitudes()
        {
            CommandArg arg = new CommandArg("1e10,-1e10,2e10,3e10");
            Assert.IsTrue(arg.TryGet(out Rect r));
            Assert.IsFalse(float.IsNaN(r.x) || float.IsInfinity(r.x));
            Assert.IsFalse(float.IsNaN(r.y) || float.IsInfinity(r.y));
            Assert.IsFalse(float.IsNaN(r.width) || float.IsInfinity(r.width));
            Assert.IsFalse(float.IsNaN(r.height) || float.IsInfinity(r.height));
            Assert.Greater(r.x, 0f);
            Assert.Less(r.y, 0f);
            Assert.Greater(r.width, 0f);
            Assert.Greater(r.height, 0f);
        }

        [Test]
        public void RectAcceptsNegativeDimensions()
        {
            CommandArg arg = new CommandArg("10,20,-5,-7");
            Assert.IsTrue(arg.TryGet(out Rect r));
            Assert.AreEqual(10f, r.x, 1e-4f);
            Assert.AreEqual(20f, r.y, 1e-4f);
            Assert.AreEqual(-5f, r.width, 1e-4f);
            Assert.AreEqual(-7f, r.height, 1e-4f);
        }

        [Test]
        public void ExtremeQuaternionLargeMagnitudes()
        {
            CommandArg arg = new CommandArg("1e10,2e10,3e10,4e10");
            Assert.IsTrue(arg.TryGet(out Quaternion q));
            Assert.IsFalse(float.IsNaN(q.x) || float.IsInfinity(q.x));
            Assert.IsFalse(float.IsNaN(q.y) || float.IsInfinity(q.y));
            Assert.IsFalse(float.IsNaN(q.z) || float.IsInfinity(q.z));
            Assert.IsFalse(float.IsNaN(q.w) || float.IsInfinity(q.w));
            Assert.Greater(q.x, 0f);
            Assert.Greater(q.y, 0f);
            Assert.Greater(q.z, 0f);
            Assert.Greater(q.w, 0f);
        }

        [Test]
        public void ReorderedLabelsVector3()
        {
            CommandArg arg = new CommandArg("z:3 y:2 x:1");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
        }

        [Test]
        public void ReorderedLabelsRect()
        {
            CommandArg arg = new CommandArg("width:100 height:50 y:20 x:10");
            Assert.IsTrue(arg.TryGet(out Rect r));
            Assert.AreEqual(10f, r.x, 1e-4f);
            Assert.AreEqual(20f, r.y, 1e-4f);
            Assert.AreEqual(100f, r.width, 1e-4f);
            Assert.AreEqual(50f, r.height, 1e-4f);
        }

        [Test]
        public void ReorderedLabelsQuaternion()
        {
            CommandArg arg = new CommandArg("w:0.4 z:0.3 y:0.2 x:0.1");
            Assert.IsTrue(arg.TryGet(out Quaternion q));
            Assert.AreEqual(0.1f, q.x, 1e-4f);
            Assert.AreEqual(0.2f, q.y, 1e-4f);
            Assert.AreEqual(0.3f, q.z, 1e-4f);
            Assert.AreEqual(0.4f, q.w, 1e-4f);
        }
    }
}
