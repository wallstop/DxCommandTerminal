namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Linq;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UI;
    using UnityEngine.TestTools;
    using Utils;

    public sealed class ArrayPoolTests
    {
        [TearDown]
        public void TearDown()
        {
            if (TerminalUI.Instance != null)
            {
                UnityEngine.Object.Destroy(TerminalUI.Instance.gameObject);
            }
        }

        [Test]
        public void ZeroSizeReturnsEmpty()
        {
            using ArrayLease<int> lease = DxArrayPool<int>.Get(0, out int[] arr);
            Assert.AreSame(Array.Empty<int>(), arr);

            using ArrayLease<int> fastLease = DxFastArrayPool<int>.Get(0, out int[] arr2);
            Assert.AreSame(Array.Empty<int>(), arr2);
        }

        [Test]
        public void LeaseReturnsCorrectSize()
        {
            using ArrayLease<byte> lease = DxArrayPool<byte>.Get(128, out byte[] arr);
            Assert.AreEqual(128, arr.Length);

            using ArrayLease<byte> fastLease = DxFastArrayPool<byte>.Get(256, out byte[] arr2);
            Assert.AreEqual(256, arr2.Length);
        }

        [Test]
        public void ClearingBehaviorForDxArrayPool()
        {
            byte[] original;
            using (ArrayLease<byte> lease = DxArrayPool<byte>.Get(4, out original))
            {
                for (int i = 0; i < original.Length; ++i)
                {
                    original[i] = 0xFF;
                }
            }
            using ArrayLease<byte> lease2 = DxArrayPool<byte>.Get(4, out byte[] second);
            Assert.AreSame(original, second);
            Assert.IsTrue(second.All(v => v == 0));
        }

        [Test]
        public void FastPoolDoesNotClear()
        {
            byte[] original;
            using (ArrayLease<byte> lease = DxFastArrayPool<byte>.Get(4, out original))
            {
                for (int i = 0; i < original.Length; ++i)
                {
                    original[i] = 0x7F;
                }
            }
            using ArrayLease<byte> lease2 = DxFastArrayPool<byte>.Get(4, out byte[] second);
            Assert.AreSame(original, second);
            Assert.IsTrue(second.All(v => v == 0x7F));
        }

        [UnityTest]
        public IEnumerator ConcurrencySanity()
        {
            // Spawn a terminal to match other playmode tests style (not strictly required)
            yield return TerminalTests.SpawnTerminal(resetStateOnInit: true);

            int tasks = 4;
            int iterations = 500;
            Task[] workers = new Task[tasks];
            for (int t = 0; t < tasks; ++t)
            {
                workers[t] = Task.Run(() =>
                {
                    Random rand = new Random(Environment.TickCount + t);
                    for (int i = 0; i < iterations; ++i)
                    {
                        int size = rand.Next(1, 128);
                        using ArrayLease<int> lease = DxArrayPool<int>.Get(size, out int[] a);
                        if (a.Length > 0)
                        {
                            a[0] = 123;
                        }
                    }
                });
            }

            while (true)
            {
                bool all = true;
                foreach (Task w in workers)
                {
                    all &= w.IsCompleted;
                }
                if (all)
                {
                    break;
                }
                yield return null;
            }
        }

        [Test]
        public void ReuseReturnsSameInstance()
        {
            int[] first;
            using (ArrayLease<int> l1 = DxArrayPool<int>.Get(64, out first)) { }
            using ArrayLease<int> l2 = DxArrayPool<int>.Get(64, out int[] second);
            Assert.AreSame(first, second);
        }
    }
}
