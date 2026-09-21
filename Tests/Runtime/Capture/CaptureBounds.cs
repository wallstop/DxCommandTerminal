namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using UnityEngine;

    /*
        Per-scenario acceptance bounds. The majority-color share proves a real
        themed background rendered; the distinct-color floor proves content
        (text, rows) drew on top of it.
     */
    public sealed class CaptureBounds
    {
        public int MinDistinctColors { get; }
        public float MinBackgroundFraction { get; }
        public float MaxBackgroundFraction { get; }

        public CaptureBounds(int minDistinctColors, float minBackgroundFraction, float max)
        {
            MinDistinctColors = minDistinctColors;
            MinBackgroundFraction = minBackgroundFraction;
            MaxBackgroundFraction = max;
        }

        public static CaptureBounds Default()
        {
            return new CaptureBounds(8, 0.30f, 0.995f);
        }
    }
}
