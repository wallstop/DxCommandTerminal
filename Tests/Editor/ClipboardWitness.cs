namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using NUnit.Framework;
    using UnityEngine;

    /*
        The EditMode share of the clipboard verdict: writes and reads back
        probe strings through the system clipboard until the host holds two
        consecutive writes - the same evidence TerminalClipboard.TryWrite
        uses and the same two-pass hold the PlayMode paste fixtures apply.

        Every pass writes a DIFFERENT value and demands the read-back name
        that same value. A host that answers one write behind - the degraded
        pinned session, #250 - reads the previous pass's value for every
        read, so a constant probe would falsely hold on the echo; fresh
        values per pass turn the lag into the mismatch it is.

        A host whose clipboard cannot serve that fails every copy and cut
        fixture for the environment's sake; the probe converts that into an
        Ignore that names the environment, while a host that answers twice
        in a row leaves every clipboard assert standing as a product
        verdict.
     */
    internal static class ClipboardWitness
    {
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
                    string probe = $"dxct-clipboard-probe-{attempt}";
                    GUIUtility.systemCopyBuffer = probe;
                    heldPasses = string.Equals(GUIUtility.systemCopyBuffer, probe, StringComparison.Ordinal) ? heldPasses + 1 : 0;
                }

                if (HeldPassesRequired <= heldPasses)
                {
                    return;
                }

                Assert.Ignore("The host clipboard did not hold two consecutive direct writes, so the " + "copy and cut fixtures cannot measure delivery here (#207, #250)");
            }
            finally
            {
                GUIUtility.systemCopyBuffer = original;
            }
        }
    }
}
