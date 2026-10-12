namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using NUnit.Framework;
    using UnityEngine;

    /*
        The EditMode share of the clipboard verdict: writes and reads back a
        probe string through the system clipboard until it holds on two
        consecutive passes - the same evidence TerminalClipboard.TryWrite
        uses and the same two-pass hold the PlayMode paste fixtures apply.

        A host whose clipboard will not hold a direct write (the degraded
        pinned session, #250) fails every copy and cut fixture for the
        environment's sake; the probe converts that into an Ignore that
        names the environment, while a host that answers twice in a row
        leaves every clipboard assert standing as a product verdict.
     */
    internal static class ClipboardWitness
    {
        private const string Probe = "dxct-clipboard-probe";
        private const int AttemptBudget = 10;
        private const int HeldPassesRequired = 2;

        public static void IgnoreWhenClipboardCannotServe()
        {
            string original = GUIUtility.systemCopyBuffer;
            try
            {
                int heldPasses = 0;
                for (int attempt = 0; attempt < AttemptBudget && heldPasses < HeldPassesRequired; ++attempt)
                {
                    GUIUtility.systemCopyBuffer = Probe;
                    heldPasses = GUIUtility.systemCopyBuffer == Probe ? heldPasses + 1 : 0;
                }

                if (HeldPassesRequired <= heldPasses)
                {
                    return;
                }

                Assert.Ignore("The host clipboard did not hold a direct write twice in a row, so the " + "copy and cut fixtures cannot measure delivery here (#207, #250)");
            }
            finally
            {
                GUIUtility.systemCopyBuffer = original;
            }
        }
    }
}
