namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
    using System.Collections;
    using UI;
    using UnityEngine;

    public static class TestSceneHelpers
    {
        public static IEnumerator DestroyTerminalAndWait(int frames = 2)
        {
            if (TerminalUI.Instance != null)
            {
                Object.Destroy(TerminalUI.Instance.gameObject);
            }
            for (int i = 0; i < frames; ++i)
            {
                yield return null;
            }
        }

        public static IEnumerator CleanRestart(bool resetStateOnInit, int settleFrames = 2)
        {
            yield return DestroyTerminalAndWait(settleFrames);
            yield return TerminalTests.SpawnTerminal(resetStateOnInit);
            for (int i = 0; i < settleFrames; ++i)
            {
                yield return null;
            }
        }

        public static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; ++i)
            {
                yield return null;
            }
        }
    }
}
