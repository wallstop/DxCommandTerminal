namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UntypedUnityParsingFailureTests
    {
        [Test]
        public void Vector3UntypedTwoComponentsParses()
        {
            CommandArg arg = new("1,2");
            Assert.IsTrue(arg.TryGet(typeof(Vector3), out object obj));
            Vector3 v = (Vector3)obj;
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(0f, v.z, 1e-4f);
        }

        [Test]
        public void RectUntypedNonNumeric()
        {
            CommandArg arg = new("1,2,three,4");
            Assert.IsFalse(arg.TryGet(typeof(Rect), out object _));
        }

        [Test]
        public void Vector2IntUntypedNonInteger()
        {
            CommandArg arg = new("1,2.5");
            Assert.IsFalse(arg.TryGet(typeof(Vector2Int), out object _));
        }

        [Test]
        public void QuaternionUntypedTooManyComponents()
        {
            CommandArg arg = new("0.1,0.2,0.3,0.4,0.5");
            Assert.IsFalse(arg.TryGet(typeof(Quaternion), out object _));
        }

        [Test]
        public void ColorUntypedNonNumericRgba()
        {
            CommandArg arg = new("RGBA(0.1, nope, 0.3, 0.4)");
            Assert.IsFalse(arg.TryGet(typeof(Color), out object _));
        }

        [Test]
        public void Vector4UntypedTooManyComponents()
        {
            CommandArg arg = new("1,2,3,4,5");
            Assert.IsFalse(arg.TryGet(typeof(Vector4), out object _));
        }

        [Test]
        public void ColorUntypedTooFewComponents()
        {
            CommandArg arg = new("0.1,0.2");
            Assert.IsFalse(arg.TryGet(typeof(Color), out object _));
        }
    }
}
