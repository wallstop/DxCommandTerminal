namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UnityBoundaryMalformedTests
    {
        [Test]
        public void Vector2IntOutOfRangePositive()
        {
            CommandArg arg = new("2147483648,0"); // int.MaxValue + 1
            Assert.IsFalse(arg.TryGet(out Vector2Int _));
        }

        [Test]
        public void Vector2IntOutOfRangeNegative()
        {
            CommandArg arg = new("-2147483649,0"); // int.MinValue - 1
            Assert.IsFalse(arg.TryGet(out Vector2Int _));
        }

        [Test]
        public void Vector3IntOutOfRangeComponent()
        {
            CommandArg arg = new("0,999999999999999999999,0");
            Assert.IsFalse(arg.TryGet(out Vector3Int _));
        }

        [Test]
        public void RectIntOutOfRangeWidth()
        {
            CommandArg arg = new("0,0,2147483648,10");
            Assert.IsFalse(arg.TryGet(out RectInt _));
        }

        [Test]
        public void RectIntOutOfRangeHeight()
        {
            CommandArg arg = new("0,0,10,2147483648");
            Assert.IsFalse(arg.TryGet(out RectInt _));
        }

        [Test]
        public void QuaternionTooManyComponents()
        {
            CommandArg arg = new("0.1,0.2,0.3,0.4,0.5");
            Assert.IsFalse(arg.TryGet(out Quaternion _));
        }

        [Test]
        public void ColorRgbaTrailingCommaParsesRgb()
        {
            CommandArg arg = new("RGBA(0.1,0.2,0.3,)");
            Assert.IsTrue(arg.TryGet(out Color c));
            Assert.AreEqual(0.1f, c.r, 1e-4f);
            Assert.AreEqual(0.2f, c.g, 1e-4f);
            Assert.AreEqual(0.3f, c.b, 1e-4f);
            Assert.AreEqual(1.0f, c.a, 1e-4f);
        }
    }
}
