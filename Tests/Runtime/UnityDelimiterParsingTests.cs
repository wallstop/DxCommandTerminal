namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UnityDelimiterParsingTests
    {
        [Test]
        public void ParsesVector2WithUnderscores()
        {
            CommandArg arg = new CommandArg("1.5_2.5");
            Assert.IsTrue(arg.TryGet(out Vector2 v));
            Assert.AreEqual(1.5f, v.x, 1e-4f);
            Assert.AreEqual(2.5f, v.y, 1e-4f);
        }

        [Test]
        public void ParsesVector3WithForwardSlashes()
        {
            CommandArg arg = new CommandArg("1/2/3");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
        }

        [Test]
        public void ParsesVector3WithBackslashes()
        {
            CommandArg arg = new CommandArg("1\\2\\3");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
        }

        [Test]
        public void ParsesVector4WithIgnoredCharacters()
        {
            CommandArg arg = new CommandArg("`{(1|2|3|4)}`");
            Assert.IsTrue(arg.TryGet(out Vector4 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
            Assert.AreEqual(4f, v.w, 1e-4f);
        }

        [Test]
        public void ParsesVector3WithLabeledAndMixedWrappers()
        {
            CommandArg arg = new CommandArg("[(x:1;y:2;z:3)]");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
        }

        [Test]
        public void ParsesRectWithLabeledAndMixedDelimiters()
        {
            CommandArg arg = new CommandArg("{x:10;y:20|width:100,height:50}");
            Assert.IsTrue(arg.TryGet(out Rect r));
            Assert.AreEqual(10f, r.x, 1e-4f);
            Assert.AreEqual(20f, r.y, 1e-4f);
            Assert.AreEqual(100f, r.width, 1e-4f);
            Assert.AreEqual(50f, r.height, 1e-4f);
        }

        [Test]
        public void ParsesQuaternionWithLabelsAndWrappers()
        {
            CommandArg arg = new CommandArg("<(x:0.1;y:0.2|z:0.3,w:0.4)>");
            Assert.IsTrue(arg.TryGet(out Quaternion q));
            Assert.AreEqual(0.1f, q.x, 1e-4f);
            Assert.AreEqual(0.2f, q.y, 1e-4f);
            Assert.AreEqual(0.3f, q.z, 1e-4f);
            Assert.AreEqual(0.4f, q.w, 1e-4f);
        }

        [Test]
        public void ParsesVector2IntWithWrappersAndUnderscore()
        {
            CommandArg arg = new CommandArg("<x:-1_y:2>");
            Assert.IsTrue(arg.TryGet(out Vector2Int v));
            Assert.AreEqual(-1, v.x);
            Assert.AreEqual(2, v.y);
        }

        [Test]
        public void ParsesVector3WithSemicolons()
        {
            CommandArg arg = new CommandArg("1;2;3");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
        }

        [Test]
        public void ParsesVector2WithColons()
        {
            CommandArg arg = new CommandArg("1:2");
            Assert.IsTrue(arg.TryGet(out Vector2 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
        }

        [Test]
        public void ParsesRectWithSemicolons()
        {
            CommandArg arg = new CommandArg("1;2;3;4");
            Assert.IsTrue(arg.TryGet(out Rect r));
            Assert.AreEqual(1f, r.x, 1e-4f);
            Assert.AreEqual(2f, r.y, 1e-4f);
            Assert.AreEqual(3f, r.width, 1e-4f);
            Assert.AreEqual(4f, r.height, 1e-4f);
        }

        [Test]
        public void ParsesVector3WithMixedDelimiters()
        {
            CommandArg arg = new CommandArg("1;2,3");
            Assert.IsTrue(arg.TryGet(out Vector3 v));
            Assert.AreEqual(1f, v.x, 1e-4f);
            Assert.AreEqual(2f, v.y, 1e-4f);
            Assert.AreEqual(3f, v.z, 1e-4f);
        }
    }
}
