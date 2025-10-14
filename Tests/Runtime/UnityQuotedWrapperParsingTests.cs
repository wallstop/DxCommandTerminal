namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UnityQuotedWrapperParsingTests
    {
        [Test]
        public void ParsesVector3WrappedInSingleQuotes()
        {
            CommandArg arg = new("'[(1,2,3)]'");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
        }

        [Test]
        public void ParsesRectWrappedInSingleQuotes()
        {
            CommandArg arg = new("'{1;2;3;4}'");
            Assert.IsTrue(arg.TryGet(out Rect r));
            Assert.AreEqual(1f, r.x, 1e-4f);
            Assert.AreEqual(2f, r.y, 1e-4f);
            Assert.AreEqual(3f, r.width, 1e-4f);
            Assert.AreEqual(4f, r.height, 1e-4f);
        }
    }
}
