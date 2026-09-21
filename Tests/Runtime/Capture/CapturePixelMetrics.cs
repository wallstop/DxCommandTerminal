namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using UnityEngine;

    /*
        Immutable pixel measurements from one capture readback. Background is
        the modal color; BackgroundFraction is its share of all pixels.
     */
    public sealed class CapturePixelMetrics
    {
        public int Width { get; }
        public int Height { get; }
        public int DistinctColors { get; }
        public float BackgroundFraction { get; }
        public int BackgroundRed { get; }
        public int BackgroundGreen { get; }
        public int BackgroundBlue { get; }
        public int PngBytes { get; }

        public CapturePixelMetrics(
            int width,
            int height,
            int distinctColors,
            float backgroundFraction,
            int backgroundRed,
            int backgroundGreen,
            int backgroundBlue,
            int pngBytes
        )
        {
            Width = width;
            Height = height;
            DistinctColors = distinctColors;
            BackgroundFraction = backgroundFraction;
            BackgroundRed = backgroundRed;
            BackgroundGreen = backgroundGreen;
            BackgroundBlue = backgroundBlue;
            PngBytes = pngBytes;
        }

        public CapturePixelMetrics WithPngBytes(int pngBytes)
        {
            return new CapturePixelMetrics(
                Width,
                Height,
                DistinctColors,
                BackgroundFraction,
                BackgroundRed,
                BackgroundGreen,
                BackgroundBlue,
                pngBytes
            );
        }
    }
}
