namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Threading.Tasks;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UI;
    using UnityEngine.TestTools;

    public sealed class LoggingThreadSafetyTests
    {
        [TearDown]
        public void TearDown()
        {
            if (TerminalUI.Instance != null)
            {
                UnityEngine.Object.Destroy(TerminalUI.Instance.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator ConcurrentEnqueueDoesNotCrashAndDrains()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            CommandLog buffer = Terminal.Buffer;
            Assert.IsNotNull(buffer);

            int initial = buffer.Logs.Count;
            int toProduce = 100;

            Task[] tasks = new Task[4];
            for (int t = 0; t < tasks.Length; ++t)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < toProduce; ++i)
                    {
                        Terminal.Log(TerminalLogType.Message, "threaded log {0}", i);
                    }
                });
            }

            // Wait until all tasks complete
            while (true)
            {
                bool allDone = true;
                foreach (Task task in tasks)
                {
                    allDone &= task.IsCompleted;
                }
                if (allDone)
                {
                    break;
                }
                yield return null;
            }

            // Give a few frames to drain the queue
            for (int i = 0; i < 5; ++i)
            {
                yield return null;
            }

            int finalCount = buffer.Logs.Count;
            Assert.GreaterOrEqual(finalCount, initial + toProduce * tasks.Length);
        }
    }
}
