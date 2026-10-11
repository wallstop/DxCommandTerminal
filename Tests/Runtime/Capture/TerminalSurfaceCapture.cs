namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.UIElements;
    using Debug = UnityEngine.Debug;
    using Object = UnityEngine.Object;

    /*
        T04 fixture-capture harness: renders a live package-owned UI Toolkit
        panel into an offscreen render target, reads the pixels back, checks
        them against scenario bounds, and writes the PNG plus a self-describing
        manifest under the package's .artifacts/t4/ directory.

        The harness only observes the surfaces under test: it redirects
        PanelSettings.targetTexture, waits a bounded number of settled frames
        (driven by the calling IEnumerator, never Thread.Sleep), and restores
        every render global it touches. Read-only state capture stays in
        npm run unity:capture; baseline updates are a separate, explicit
        command that lands with T11's comparator.

        Nondeterminism is frozen at the source: the terminal's styled caret
        through TerminalUI.SetCursorBlinkPaused, and the command palette's
        native TextField caret through FreezeCursor (the caret paints with
        the text input's cursorColor; hosts without that property make the
        palette scenario skip its blink-invariance proof, recorded in the
        manifest diagnostics).
     */
    public static class TerminalSurfaceCapture
    {
        private const string PackageName = "com.wallstop-studios.dxcommandterminal";
        private const string ArtifactFolderName = "t4";
        private const int RevisionProbeTimeoutMilliseconds = 2000;

        /*
            Three forced repaint/render passes per capture (established in the
            unity-helpers capture this is adapted from): nested scroll views
            realize their content on the first pass, the dynamic font atlas can
            extend on the second, and the third draws both settled sets.
         */
        private const int ForcedRenderPasses = 3;
        private const BindingFlags InheritedInstanceMembers =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /*
            Offscreen rendering needs a real graphics device; a -nographics
            editor cannot rasterize, so capture tests skip there.
         */
        public static bool IsSupported => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        public static string UnsupportedReason =>
            "Fixture capture needs a graphics device to rasterize into an offscreen target; "
            + $"this editor reports {SystemInfo.graphicsDeviceType} (typically -nographics).";

        public static int CountRenderTextures()
        {
            return Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
        }

        /*
            Creates a fresh run directory under the package's .artifacts/t4/
            so one capture session's files stay grouped.
         */
        public static string CreateRunDirectory()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string stamp = DateTime.UtcNow.ToString(
                "yyyy-MM-dd'T'HH-mm-ss-fff'Z'",
                CultureInfo.InvariantCulture
            );
            string directory = Path.Combine(
                projectRoot,
                "Packages",
                PackageName,
                ".artifacts",
                ArtifactFolderName,
                stamp
            );
            Directory.CreateDirectory(directory);
            return directory;
        }

        /*
            Reads the settled render target back into an RGB24 texture, writes
            a PNG, and reports the pixel metrics the bounds check consumes.
            Before the readback it forces contentRoot's panel to render
            synchronously into the target, so pixels never depend on when the
            game view last repainted; a null root (the blank control) skips
            the forced render. Restores RenderTexture.active on every path and
            destroys the readback texture before returning.
         */
        public static CapturePixelMetrics CaptureToPng(
            RenderTexture target,
            VisualElement contentRoot,
            string pngOutputPath
        )
        {
            return CaptureFrame(target, contentRoot, pngOutputPath, null, false).Metrics;
        }

        /*
            Same read, keeping the readback pixels so a scenario can compare
            two of its own captures pixel against pixel. The PNG on disk stays
            the shared record; the in-memory pixels are what the invariance
            asserts read, because decoding the PNGs back would need a decoder
            the test assembly does not ship.
         */
        public static CapturedFrame CaptureFrame(
            RenderTexture target,
            VisualElement contentRoot,
            string pngOutputPath
        )
        {
            return CaptureFrame(target, contentRoot, pngOutputPath, null, true);
        }

        /*
            Pure bounds check: returns one line per violated bound, empty when
            the render is acceptable. Capture tests assert this list is empty;
            the negative control asserts a blank render violates it.
         */
        public static List<string> Evaluate(CapturePixelMetrics metrics, CaptureBounds bounds)
        {
            List<string> violations = new List<string>();
            if (metrics.DistinctColors < bounds.MinDistinctColors)
            {
                violations.Add(
                    $"distinctColors {metrics.DistinctColors} < min {bounds.MinDistinctColors} "
                        + "(blank or nearly blank render)"
                );
            }

            bool backgroundInRange =
                bounds.MinBackgroundFraction <= metrics.BackgroundFraction
                && metrics.BackgroundFraction <= bounds.MaxBackgroundFraction;
            if (!backgroundInRange)
            {
                violations.Add(
                    $"backgroundFraction {FormatPercent(metrics.BackgroundFraction)} outside "
                        + $"[{FormatPercent(bounds.MinBackgroundFraction)}, "
                        + $"{FormatPercent(bounds.MaxBackgroundFraction)}] "
                        + $"(modal color {FormatRgb(metrics)})"
                );
            }

            return violations;
        }

        public static void WriteManifest(CaptureManifest manifest, string manifestOutputPath)
        {
            File.WriteAllText(manifestOutputPath, manifest.ToJson(), new UTF8Encoding(false));
        }

        /*
            Resolves the package checkout's git revision for provenance.
            Returns null (never throws) when git or the repo is unavailable.
         */
        public static string TryReadRevision()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            if (!Directory.Exists(Path.Combine(projectRoot, ".git")))
            {
                return null;
            }

            try
            {
                ProcessStartInfo start = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    WorkingDirectory = projectRoot,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using Process process = Process.Start(start);
                if (process == null)
                {
                    return null;
                }

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(RevisionProbeTimeoutMilliseconds);
                return process.HasExited && 0 == process.ExitCode && 0 < output.Length
                    ? output
                    : null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Fixture capture revision probe failed: {exception.Message}");
                return null;
            }
        }

        /*
            Freezes the native UITK caret of a focused TextField while the
            scope is alive: the caret paints with the text input's
            cursorColor, so a fully transparent color takes the blink phase
            out of the captured pixels. On hosts whose UITK exposes no
            cursorColor property the scope stays inert and reports it; the
            palette scenario then skips its blink-invariance proof instead
            of asserting against an unfrozen caret.
         */
        public static CursorFreezeScope FreezeCursor(TextField field)
        {
            return new CursorFreezeScope(field);
        }

        /*
            Pixel-level invariance report: null when every channel of every
            pixel of both frames is within maxChannelDelta, otherwise one line
            naming the exceedance count, the worst delta, and the first
            mismatching pixel. Reads the frames' own pixels, so no PNG decode
            is involved.
         */
        public static string DescribeFrameMismatch(
            CapturedFrame expected,
            CapturedFrame actual,
            int maxChannelDelta
        )
        {
            if (expected.Width != actual.Width || expected.Height != actual.Height)
            {
                return $"size {actual.Width}x{actual.Height} != expected "
                    + $"{expected.Width}x{expected.Height}";
            }

            Color32[] expectedPixels = expected.Pixels;
            Color32[] actualPixels = actual.Pixels;
            if (expectedPixels == null || actualPixels == null)
            {
                throw new InvalidOperationException(
                    "Frame compare needs both captures taken with pixels kept."
                );
            }

            int mismatchCount = 0;
            int worstDelta = 0;
            int firstIndex = -1;
            for (int index = 0; index < expectedPixels.Length; ++index)
            {
                Color32 a = expectedPixels[index];
                Color32 b = actualPixels[index];
                int delta = Math.Max(
                    Math.Abs(a.r - b.r),
                    Math.Max(Math.Abs(a.g - b.g), Math.Abs(a.b - b.b))
                );
                if (delta <= maxChannelDelta)
                {
                    continue;
                }

                ++mismatchCount;
                if (worstDelta < delta)
                {
                    worstDelta = delta;
                }

                if (firstIndex < 0)
                {
                    firstIndex = index;
                }
            }

            if (mismatchCount == 0)
            {
                return null;
            }

            /*
                Reported in the PNG's row space (top-down) so the coordinate
                can be found with an image tool: the readback's rows count
                from the visual bottom, which is the space the compare runs
                in.
             */
            int x = firstIndex % expected.Width;
            int y = expected.Height - 1 - firstIndex / expected.Width;
            Color32 expectedPixel = expectedPixels[firstIndex];
            Color32 actualPixel = actualPixels[firstIndex];
            return $"{mismatchCount} pixel(s) exceed a per-channel delta of {maxChannelDelta} "
                + $"(worst {worstDelta}); first at ({x},{y}): expected "
                + $"#{expectedPixel.r:X2}{expectedPixel.g:X2}{expectedPixel.b:X2} got "
                + $"#{actualPixel.r:X2}{actualPixel.g:X2}{actualPixel.b:X2}";
        }

        internal static CapturedFrame CaptureFrame(
            RenderTexture target,
            VisualElement contentRoot,
            string pngOutputPath,
            Rect? metricsRegion,
            bool keepPixels
        )
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (target.width < 1 || target.height < 1)
            {
                throw new ArgumentException(
                    $"Capture target must have positive dimensions, got {target.width}x{target.height}.",
                    nameof(target)
                );
            }

            if (string.IsNullOrWhiteSpace(pngOutputPath))
            {
                throw new ArgumentException("Capture needs an output path.", nameof(pngOutputPath));
            }

            RenderTexture previousTarget = RenderTexture.active;
            Texture2D readback = null;
            try
            {
                RenderTexture.active = target;
                if (contentRoot != null)
                {
                    ForcePanelRender(contentRoot);
                }

                readback = new Texture2D(
                    target.width,
                    target.height,
                    TextureFormat.RGB24,
                    false,
                    false
                )
                {
                    name = "T4CaptureReadback",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
                readback.Apply(false, false);

                CapturePixelMetrics metrics = ReadMetrics(readback, metricsRegion);
                byte[] png = readback.EncodeToPNG();
                if (png == null || png.Length < 1)
                {
                    throw new InvalidOperationException(
                        $"PNG encoding produced no bytes for {pngOutputPath}."
                    );
                }

                CapturedFrame frame = new CapturedFrame(
                    metrics.WithPngBytes(png.Length),
                    keepPixels ? readback.GetPixels32() : null,
                    readback.width,
                    readback.height
                );
                string directory = Path.GetDirectoryName(pngOutputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllBytes(pngOutputPath, png);
                return frame;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                if (readback != null)
                {
                    Object.DestroyImmediate(readback);
                }
            }
        }

        /*
            Forces contentRoot's panel through repaint + render passes so the
            readback reads a settled, intentional frame instead of the game
            view's last asynchronous paint. The panel API Unity exposes for
            this is internal and inherited from a base panel type, so the one
            place this harness reflects is on Unity's own panel (never on
            package types); a shape change fails explicitly here instead of
            producing a silently stale image.
         */
        internal static void ForcePanelRender(VisualElement contentRoot)
        {
            ForcePanelRender(contentRoot, ForcedRenderPasses);
        }

        /*
            The pass count is a parameter so a latency measurement can read the
            panel's *first* render after a change, which is the only way to tell
            a surface that settles in one render from one that needs a second;
            every capture fixture uses the settled default. A zero-pass call
            would read whatever the target held, so it is refused.
         */
        internal static void ForcePanelRender(VisualElement contentRoot, int passes)
        {
            if (passes < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(passes),
                    passes,
                    "A forced capture must drive at least one repaint/render pass."
                );
            }

            IPanel panel = contentRoot.panel;
            if (panel == null)
            {
                throw new InvalidOperationException(
                    "The capture surface has no live panel to render; attach the "
                        + "render target while the surface is enabled and settled."
                );
            }

            Type panelType = panel.GetType();
            MethodInfo renderMethod = panelType.GetMethod(
                "Render",
                InheritedInstanceMembers,
                binder: null,
                Type.EmptyTypes,
                modifiers: null
            );
            MethodInfo repaintMethod = panelType.GetMethod(
                "Repaint",
                InheritedInstanceMembers,
                binder: null,
                new[] { typeof(Event) },
                modifiers: null
            );
            if (renderMethod == null || repaintMethod == null)
            {
                throw new InvalidOperationException(
                    $"Panel type {panelType.FullName} exposes no inherited Repaint(Event)/Render() "
                        + "pair to drive a synchronous capture. Unity's internal panel API "
                        + "changed; update the capture harness."
                );
            }

            for (int pass = 0; pass < passes; ++pass)
            {
                repaintMethod.Invoke(
                    panel,
                    new object[] { new Event { type = EventType.Repaint } }
                );
                renderMethod.Invoke(panel, Array.Empty<object>());
            }
        }

#if UNITY_EDITOR
        /*
            Pins the game view zoom so the panel's layout points match Screen
            pixels one-to-one: TerminalUI sizes in Screen pixels, and the
            host's zoomed, Retina-scaled game view otherwise disagrees about
            how many pixels a panel point is. Reflects on Unity's own
            GameView type - the one place this harness reflects on the editor
            - drives every open Game View instance, and fails explicitly when
            Unity's internal shape changes instead of mis-rendering.
         */
        public static GameViewZoomScope PinGameViewZoom()
        {
            Type gameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType(
                "UnityEditor.GameView"
            );
            if (gameViewType == null)
            {
                throw new InvalidOperationException(
                    "The capture harness could not resolve UnityEditor.GameView; update the "
                        + "harness for this Unity version."
                );
            }

            UnityEngine.Object[] existing = Resources.FindObjectsOfTypeAll(gameViewType);
            List<GameViewZoomTarget> targets = new List<GameViewZoomTarget>();
            foreach (UnityEngine.Object window in existing)
            {
                if (window is not UnityEditor.EditorWindow editorWindow)
                {
                    continue;
                }

                FieldInfo zoomField = gameViewType.GetField(
                    "m_ZoomArea",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                object zoomArea = zoomField?.GetValue(editorWindow);
                if (zoomArea == null)
                {
                    continue;
                }

                Type zoomAreaType = zoomArea.GetType();
                FieldInfo scaleField = zoomAreaType.GetField(
                    "m_Scale",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
                PropertyInfo minScaleProperty = zoomAreaType.GetProperty(
                    "hScaleMin",
                    BindingFlags.Instance | BindingFlags.Public
                );
                PropertyInfo minVScaleProperty = zoomAreaType.GetProperty(
                    "vScaleMin",
                    BindingFlags.Instance | BindingFlags.Public
                );
                if (scaleField == null || minScaleProperty == null || minVScaleProperty == null)
                {
                    throw new InvalidOperationException(
                        "ZoomableArea exposes no m_Scale/hScaleMin/vScaleMin; update the capture "
                            + "harness for this Unity version."
                    );
                }

                targets.Add(
                    new GameViewZoomTarget(
                        editorWindow,
                        zoomArea,
                        scaleField,
                        minScaleProperty,
                        minVScaleProperty
                    )
                );
            }

            if (targets.Count < 1)
            {
                throw new InvalidOperationException(
                    "No Game View is open, so the capture harness cannot pin its zoom. Open a "
                        + "Game View and retry."
                );
            }

            return new GameViewZoomScope(targets);
        }

        internal static string EscapeJson(string value)
        {
            if (value == null)
            {
                return "null";
            }

            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (character < ' ')
                        {
                            builder
                                .Append("\\u")
                                .Append(
                                    ((int)character).ToString("x4", CultureInfo.InvariantCulture)
                                );
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        /*
            Pixel measurements over a region of the readback. A null region is
            the whole target; otherwise the rect is already in the readback's
            pixel space - GetPixels32 rows count from the visual bottom - and
            is clamped to the target. The reported width/height/background
            fraction describe the region, so bounds evaluated against these
            metrics are bounds on the region's surface. The PNG that the
            capture writes alongside is always the whole frame.
         */
        private static CapturePixelMetrics ReadMetrics(Texture2D readback, Rect? region)
        {
            RectInt bounds = ResolvePixelRegion(readback, region);
            Color32[] pixels = readback.GetPixels32();
            Dictionary<int, int> counts = new Dictionary<int, int>(1024);
            int sampleCount = 0;
            for (int row = 0; row < bounds.height; ++row)
            {
                int rowStart = (bounds.y + row) * readback.width + bounds.x;
                for (int column = 0; column < bounds.width; ++column)
                {
                    Color32 pixel = pixels[rowStart + column];
                    int key = (pixel.r << 16) | (pixel.g << 8) | pixel.b;
                    if (counts.TryGetValue(key, out int count))
                    {
                        counts[key] = count + 1;
                    }
                    else
                    {
                        counts[key] = 1;
                    }

                    ++sampleCount;
                }
            }

            if (sampleCount < 1)
            {
                throw new InvalidOperationException(
                    $"Metrics region {bounds} is empty on a {readback.width}x{readback.height} "
                        + "readback."
                );
            }

            int backgroundKey = 0;
            int backgroundCount = 0;
            foreach (KeyValuePair<int, int> entry in counts)
            {
                if (backgroundCount < entry.Value)
                {
                    backgroundCount = entry.Value;
                    backgroundKey = entry.Key;
                }
            }

            return new CapturePixelMetrics(
                bounds.width,
                bounds.height,
                counts.Count,
                (float)backgroundCount / sampleCount,
                (backgroundKey >> 16) & 0xFF,
                (backgroundKey >> 8) & 0xFF,
                backgroundKey & 0xFF,
                0
            );
        }

        private static RectInt ResolvePixelRegion(Texture2D readback, Rect? region)
        {
            if (region == null)
            {
                return new RectInt(0, 0, readback.width, readback.height);
            }

            Rect requested = region.Value;
            int x = Mathf.Clamp(Mathf.FloorToInt(requested.x), 0, readback.width);
            int y = Mathf.Clamp(Mathf.FloorToInt(requested.y), 0, readback.height);
            int width = Mathf.Clamp(Mathf.CeilToInt(requested.width), 0, readback.width - x);
            int height = Mathf.Clamp(Mathf.CeilToInt(requested.height), 0, readback.height - y);
            return new RectInt(x, y, width, height);
        }

        private static string FormatPercent(float fraction)
        {
            return (100d * fraction).ToString("0.#", CultureInfo.InvariantCulture) + "%";
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

        /*
            Restores the pinned game view zoom and floor on Dispose, so a
            capture run leaves the host editor exactly as it found it.
         */
        public sealed class GameViewZoomScope : IDisposable
        {
            public float Current => _targets[0].Scale;

            private readonly List<GameViewZoomTarget> _targets;

            internal GameViewZoomScope(List<GameViewZoomTarget> targets)
            {
                _targets = targets;

                /*
                    Retina hosts clamp the game view zoom at the backing scale
                    (2x here), which is exactly the stretch this pin removes;
                    lower the floor for the capture and restore it after.
                 */
                foreach (GameViewZoomTarget target in _targets)
                {
                    target.LowerFloor();
                }
            }

            private static string Format(float value)
            {
                return value.ToString("0.###", CultureInfo.InvariantCulture);
            }

            public string Describe()
            {
                StringBuilder description = new StringBuilder(_targets.Count * 32);
                for (int index = 0; index < _targets.Count; ++index)
                {
                    if (0 < index)
                    {
                        description.Append(';');
                    }

                    description
                        .Append(index)
                        .Append(":scale=")
                        .Append(Format(_targets[index].Scale))
                        .Append(",min=")
                        .Append(Format(_targets[index].MinScale));
                }

                return description.ToString();
            }

            public void SetZoom(float zoom)
            {
                foreach (GameViewZoomTarget target in _targets)
                {
                    target.SetZoom(zoom);
                }
            }

            public void Dispose()
            {
                foreach (GameViewZoomTarget target in _targets)
                {
                    target.Restore();
                }
            }
        }

        internal sealed class GameViewZoomTarget
        {
            internal float Scale => ((Vector2)_scaleField.GetValue(_zoomArea)).x;

            internal float MinScale => (float)_hMinScale.GetValue(_zoomArea);

            private readonly UnityEditor.EditorWindow _gameView;
            private readonly object _zoomArea;
            private readonly FieldInfo _scaleField;
            private readonly PropertyInfo _hMinScale;
            private readonly PropertyInfo _vMinScale;
            private readonly Vector2 _previousScale;
            private readonly float _previousMinScale;

            internal GameViewZoomTarget(
                UnityEditor.EditorWindow gameView,
                object zoomArea,
                FieldInfo scaleField,
                PropertyInfo hMinScale,
                PropertyInfo vMinScale
            )
            {
                _gameView = gameView;
                _zoomArea = zoomArea;
                _scaleField = scaleField;
                _hMinScale = hMinScale;
                _vMinScale = vMinScale;
                _previousScale = (Vector2)_scaleField.GetValue(_zoomArea);
                _previousMinScale = MinScale;
            }

            internal void LowerFloor()
            {
                _hMinScale.SetValue(_zoomArea, 0.1f);
                _vMinScale.SetValue(_zoomArea, 0.1f);
            }

            internal void SetZoom(float zoom)
            {
                _scaleField.SetValue(_zoomArea, new Vector2(zoom, zoom));
                MethodInfo enforce = _zoomArea
                    .GetType()
                    .GetMethod(
                        "EnforceScaleAndRange",
                        BindingFlags.Instance | BindingFlags.NonPublic
                    );
                enforce?.Invoke(_zoomArea, null);
                _gameView.Repaint();
            }

            internal void Restore()
            {
                _hMinScale.SetValue(_zoomArea, _previousMinScale);
                _vMinScale.SetValue(_zoomArea, _previousMinScale);
                _scaleField.SetValue(_zoomArea, _previousScale);
                _gameView.Repaint();
            }
        }
#endif

        /*
            One capture's readback: the metrics the bounds check consumes plus,
            when the scenario asked for them, the raw pixels an invariance
            assert compares. Pixels are the whole target regardless of the
            metrics region, so frame compares always see the same surface the
            PNG records.
         */
        public readonly struct CapturedFrame
        {
            public CapturePixelMetrics Metrics { get; }
            public Color32[] Pixels { get; }
            public int Width { get; }
            public int Height { get; }

            public CapturedFrame(
                CapturePixelMetrics metrics,
                Color32[] pixels,
                int width,
                int height
            )
            {
                Metrics = metrics;
                Pixels = pixels;
                Width = width;
                Height = height;
            }
        }

        public sealed class CursorFreezeScope : IDisposable
        {
            private const string TextInputName = "unity-text-input";
            private const string CursorColorProperty = "cursorColor";

            public bool Engaged => _cursorColor != null;

            private readonly VisualElement _textInput;
            private readonly PropertyInfo _cursorColor;
            private readonly Color _previousColor;
            private readonly string _reason;

            internal CursorFreezeScope(TextField field)
            {
                if (field == null)
                {
                    throw new ArgumentNullException(nameof(field));
                }

                _textInput = field.Q(TextInputName);
                if (_textInput == null)
                {
                    _reason = $"no {TextInputName} child";
                    return;
                }

                _cursorColor = _textInput.GetType().GetProperty(CursorColorProperty);
                if (_cursorColor == null || !_cursorColor.CanRead || !_cursorColor.CanWrite)
                {
                    _reason = $"no {CursorColorProperty} property on {_textInput.GetType().Name}";
                    return;
                }

                try
                {
                    _previousColor = (Color)_cursorColor.GetValue(_textInput);
                    _cursorColor.SetValue(_textInput, Color.clear);
                }
                catch (Exception exception)
                {
                    _cursorColor = null;
                    _reason = (exception.InnerException ?? exception).Message;
                }
            }

            public string Describe()
            {
                return Engaged ? CursorColorProperty : $"unavailable ({_reason})";
            }

            public void Dispose()
            {
                if (!Engaged)
                {
                    return;
                }

                _cursorColor.SetValue(_textInput, _previousColor);
            }
        }
    }
}
