namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Globalization;
    using System.Threading;
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class CultureParsingTests
    {
        [Test]
        public void ParsesInvariantFloatsAndVectorsUnderNonUsCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");

                CommandArg arg = new("3.14159");
                Assert.IsTrue(arg.TryGet(out float f));
                Assert.AreEqual(3.14159f, f, 1e-5f);

                arg = new CommandArg("1.5, 2.5, 3.5");
                Assert.IsTrue(arg.TryGet(out Vector3 v));
                Assert.AreEqual(1.5f, v.x, 1e-5f);
                Assert.AreEqual(2.5f, v.y, 1e-5f);
                Assert.AreEqual(3.5f, v.z, 1e-5f);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        [Test]
        public void ParsesRectQuaternionAndColorUnderNonUsCulture()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");

                CommandArg rectArg = new("1.5, 2.5, 3.5, 4.5");
                Assert.IsTrue(rectArg.TryGet(out Rect r));
                Assert.AreEqual(1.5f, r.x, 1e-5f);
                Assert.AreEqual(2.5f, r.y, 1e-5f);
                Assert.AreEqual(3.5f, r.width, 1e-5f);
                Assert.AreEqual(4.5f, r.height, 1e-5f);

                CommandArg quatArg = new("0.1, 0.2, 0.3, 0.4");
                Assert.IsTrue(quatArg.TryGet(out Quaternion q));
                Assert.AreEqual(0.1f, q.x, 1e-5f);
                Assert.AreEqual(0.2f, q.y, 1e-5f);
                Assert.AreEqual(0.3f, q.z, 1e-5f);
                Assert.AreEqual(0.4f, q.w, 1e-5f);

                CommandArg colorArg = new("RGBA(0.1,0.2,0.3,0.4)");
                Assert.IsTrue(colorArg.TryGet(out Color c));
                Assert.AreEqual(0.1f, c.r, 1e-5f);
                Assert.AreEqual(0.2f, c.g, 1e-5f);
                Assert.AreEqual(0.3f, c.b, 1e-5f);
                Assert.AreEqual(0.4f, c.a, 1e-5f);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
