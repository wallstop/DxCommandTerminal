namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using UnityEngine;

    /*
        Self-describing provenance for one captured PNG: what rendered, where,
        under which environment, and whether the pixel bounds passed. Set the
        properties, then call ToJson / write it through
        TerminalSurfaceCapture.WriteManifest.
     */
    public sealed class CaptureManifest
    {
        public string Scenario { get; set; }
        public string CapturedUtc { get; set; }
        public bool Complete { get; set; }
        public int ResolutionWidth { get; set; }
        public int ResolutionHeight { get; set; }
        public float LogicalScale { get; set; }
        public string ColorSpace { get; set; }
        public string UnityVersion { get; set; }
        public string GraphicsApi { get; set; }
        public string Theme { get; set; }
        public string Font { get; set; }
        public string Revision { get; set; }
        public string PngFile { get; set; }
        public string Diagnostics { get; set; }
        public CapturePixelMetrics Metrics { get; set; }
        public CaptureBounds Bounds { get; set; }
        public List<string> Violations { get; set; }

        private static void AppendField(StringBuilder json, string name, string value)
        {
            json.Append(TerminalSurfaceCapture.EscapeJson(name))
                .Append(':')
                .Append(TerminalSurfaceCapture.EscapeJson(value))
                .Append(',');
        }

        private static string FormatFraction(float fraction)
        {
            return fraction.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string FormatRgb(CapturePixelMetrics metrics)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "#{0:X2}{1:X2}{2:X2}",
                metrics.BackgroundRed,
                metrics.BackgroundGreen,
                metrics.BackgroundBlue
            );
        }

        public string ToJson()
        {
            StringBuilder json = new StringBuilder(1024);
            json.Append('{');
            AppendField(json, "scenario", Scenario);
            AppendField(json, "capturedUtc", CapturedUtc);
            json.Append("\"complete\":").Append(Complete ? "true" : "false").Append(',');
            json.Append("\"resolution\":{\"width\":")
                .Append(ResolutionWidth)
                .Append(",\"height\":")
                .Append(ResolutionHeight)
                .Append("},");
            json.Append("\"logicalScale\":")
                .Append(LogicalScale.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(',');
            AppendField(json, "colorSpace", ColorSpace);
            AppendField(json, "unityVersion", UnityVersion);
            AppendField(json, "graphicsApi", GraphicsApi);
            AppendField(json, "theme", Theme);
            AppendField(json, "font", Font);
            AppendField(json, "revision", Revision);
            AppendField(json, "png", PngFile);
            AppendField(json, "diagnostics", Diagnostics);
            if (Metrics != null)
            {
                json.Append("\"metrics\":{\"width\":")
                    .Append(Metrics.Width)
                    .Append(",\"height\":")
                    .Append(Metrics.Height)
                    .Append(",\"distinctColors\":")
                    .Append(Metrics.DistinctColors)
                    .Append(",\"backgroundFraction\":")
                    .Append(FormatFraction(Metrics.BackgroundFraction))
                    .Append(",\"backgroundRgb\":\"")
                    .Append(FormatRgb(Metrics))
                    .Append("\",\"pngBytes\":")
                    .Append(Metrics.PngBytes)
                    .Append("},");
            }

            if (Bounds != null)
            {
                json.Append("\"bounds\":{\"minDistinctColors\":")
                    .Append(Bounds.MinDistinctColors)
                    .Append(",\"minBackgroundFraction\":")
                    .Append(FormatFraction(Bounds.MinBackgroundFraction))
                    .Append(",\"maxBackgroundFraction\":")
                    .Append(FormatFraction(Bounds.MaxBackgroundFraction))
                    .Append("},");
            }

            json.Append("\"violations\":[");
            List<string> violations = Violations;
            int violationCount = violations?.Count ?? 0;
            for (int index = 0; index < violationCount; ++index)
            {
                if (0 < index)
                {
                    json.Append(',');
                }

                json.Append(TerminalSurfaceCapture.EscapeJson(violations[index]));
            }

            json.Append("]}");
            return json.ToString();
        }
    }
}
