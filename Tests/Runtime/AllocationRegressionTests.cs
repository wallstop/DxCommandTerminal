namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using Backend;
    using Components;
    using NUnit.Framework;
    using UI;
    using Unity.Profiling;
    using Unity.Profiling.LowLevel.Unsafe;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class AllocationRegressionTests
    {
        private static readonly ProfilerCategory MemoryCategory = ProfilerCategory.Memory;

        [UnityTest]
        public IEnumerator CommandLoggingDoesNotAllocate()
        {
            yield return TestSceneHelpers.CleanRestart(resetStateOnInit: true);

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            // Warm up caches
            TestRuntimeScope.Shell.RunCommand("help");
            TestRuntimeScope.Buffer?.DrainPending();
            yield return null;

            using ProfilerRecorder recorder = ProfilerRecorder.StartNew(MemoryCategory, "GC.Alloc");
            try
            {
                const int iterations = 24;
                for (int i = 0; i < iterations; ++i)
                {
                    TestRuntimeScope.Shell.RunCommand("log test-message");
                    TestRuntimeScope.Buffer?.DrainPending();
                }

                terminal.HandleNext();
                terminal.HandlePrevious();
                terminal.ToggleFull();
                terminal.ToggleSmall();

                yield return null;

                Assert.That(
                    recorder.LastValue,
                    Is.EqualTo(0),
                    $"Expected zero GC allocations during command/log operations but observed {recorder.LastValue} bytes."
                );
            }
            finally
            {
                recorder.Dispose();
            }
        }
    }
}
