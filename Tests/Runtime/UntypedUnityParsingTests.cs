namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UntypedUnityParsingTests
    {
        [Test]
        public void ParsesUntypedUnityTypes()
        {
            CommandArg v3Arg = new CommandArg("1,2,3");
            Assert.IsTrue(v3Arg.TryGet(typeof(Vector3), out object v3Obj));
            Vector3 v3 = (Vector3)v3Obj;
            Assert.AreEqual(1f, v3.x, 1e-4f);
            Assert.AreEqual(2f, v3.y, 1e-4f);
            Assert.AreEqual(3f, v3.z, 1e-4f);

            CommandArg rectArg = new CommandArg("1,2,3,4");
            Assert.IsTrue(rectArg.TryGet(typeof(Rect), out object rectObj));
            Rect r = (Rect)rectObj;
            Assert.AreEqual(1f, r.x, 1e-4f);
            Assert.AreEqual(2f, r.y, 1e-4f);
            Assert.AreEqual(3f, r.width, 1e-4f);
            Assert.AreEqual(4f, r.height, 1e-4f);

            CommandArg qArg = new CommandArg("0.1,0.2,0.3,0.4");
            Assert.IsTrue(qArg.TryGet(typeof(Quaternion), out object qObj));
            Quaternion q = (Quaternion)qObj;
            Assert.AreEqual(0.1f, q.x, 1e-4f);
            Assert.AreEqual(0.2f, q.y, 1e-4f);
            Assert.AreEqual(0.3f, q.z, 1e-4f);
            Assert.AreEqual(0.4f, q.w, 1e-4f);

            CommandArg riArg = new CommandArg("1,2,3,4");
            Assert.IsTrue(riArg.TryGet(typeof(RectInt), out object riObj));
            RectInt ri = (RectInt)riObj;
            Assert.AreEqual(1, ri.x);
            Assert.AreEqual(2, ri.y);
            Assert.AreEqual(3, ri.width);
            Assert.AreEqual(4, ri.height);
        }

        [Test]
        public void ParsesMoreUntypedUnityTypes()
        {
            CommandArg v4Arg = new CommandArg("1,2,3,4");
            Assert.IsTrue(v4Arg.TryGet(typeof(Vector4), out object v4Obj));
            Vector4 v4 = (Vector4)v4Obj;
            Assert.AreEqual(1f, v4.x, 1e-4f);
            Assert.AreEqual(2f, v4.y, 1e-4f);
            Assert.AreEqual(3f, v4.z, 1e-4f);
            Assert.AreEqual(4f, v4.w, 1e-4f);

            CommandArg v2iArg = new CommandArg("-1,2");
            Assert.IsTrue(v2iArg.TryGet(typeof(Vector2Int), out object v2iObj));
            Vector2Int v2i = (Vector2Int)v2iObj;
            Assert.AreEqual(-1, v2i.x);
            Assert.AreEqual(2, v2i.y);

            CommandArg v3iArg = new CommandArg("7,8,9");
            Assert.IsTrue(v3iArg.TryGet(typeof(Vector3Int), out object v3iObj));
            Vector3Int v3i = (Vector3Int)v3iObj;
            Assert.AreEqual(7, v3i.x);
            Assert.AreEqual(8, v3i.y);
            Assert.AreEqual(9, v3i.z);

            CommandArg colorArg = new CommandArg("RGBA(0.1,0.2,0.3,0.4)");
            Assert.IsTrue(colorArg.TryGet(typeof(Color), out object cObj));
            Color c = (Color)cObj;
            Assert.AreEqual(0.1f, c.r, 1e-4f);
            Assert.AreEqual(0.2f, c.g, 1e-4f);
            Assert.AreEqual(0.3f, c.b, 1e-4f);
            Assert.AreEqual(0.4f, c.a, 1e-4f);
        }
    }
}
