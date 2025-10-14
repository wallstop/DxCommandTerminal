namespace WallstopStudios.DxCommandTerminal.UI
{
    using System;
    using UnityEngine;

    public enum LauncherSizeMode
    {
        Pixels = 0,
        RelativeToScreen = 1,
        RelativeToLauncher = 2,
    }

    [Serializable]
    public struct LauncherDimension
    {
        public LauncherSizeMode mode;

        [Min(0f)]
        public float value;

        public static LauncherDimension RelativeToScreen(float ratio)
        {
            return new LauncherDimension
            {
                mode = LauncherSizeMode.RelativeToScreen,
                value = ratio,
            };
        }

        public static LauncherDimension RelativeToLauncher(float ratio)
        {
            return new LauncherDimension
            {
                mode = LauncherSizeMode.RelativeToLauncher,
                value = ratio,
            };
        }

        public static LauncherDimension Pixels(float pixels)
        {
            return new LauncherDimension { mode = LauncherSizeMode.Pixels, value = pixels };
        }

        public float ResolvePixels(int screenLength, float launcherLength = 0f)
        {
            switch (mode)
            {
                case LauncherSizeMode.Pixels:
                    return Mathf.Max(0f, value);
                case LauncherSizeMode.RelativeToScreen:
                    return Mathf.Clamp01(value) * screenLength;
                case LauncherSizeMode.RelativeToLauncher:
                    return Mathf.Clamp01(value) * Mathf.Max(launcherLength, 0f);
                default:
                    return 0f;
            }
        }
    }

    [Serializable]
    public sealed class TerminalLauncherSettings
    {
        private const float MinimumPadding = 0f;
        private const float MinimumCorner = 0f;

        [Header("Dimensions")]
        public LauncherDimension width = LauncherDimension.RelativeToScreen(0.55f);

        public LauncherDimension height = LauncherDimension.RelativeToScreen(0.33f);

        public LauncherDimension historyHeight = LauncherDimension.RelativeToLauncher(0.45f);

        [Min(220f)]
        public float minimumWidth = 380f;

        [Min(72f)]
        public float minimumHeight = 110f;

        [Header("Position")]
        [Range(0f, 1f)]
        public float verticalAnchor = 0.5f;

        [Range(0f, 1f)]
        public float horizontalAnchor = 0.5f;

        [Min(MinimumPadding)]
        public float screenPadding = 32f;

        [Header("Visuals")]
        [Min(MinimumCorner)]
        public float cornerRadius = 16f;

        [Min(MinimumPadding)]
        public float insetPadding = 14f;

        [Header("History")]
        [Min(1)]
        public int historyVisibleEntryCount = 6;

        [Range(0.1f, 8f)]
        public float historyFadeExponent = 2.3f;

        [Tooltip("Pixels reserved for the input row and autocomplete when sizing history.")]
        [Min(48f)]
        public float inputReservePixels = 96f;

        [Header("Behaviour")]
        public bool snapOpen = true;

        [Min(0f)]
        public float animationDuration = 0.14f;

        public LauncherLayoutMetrics ComputeMetrics(int screenWidth, int screenHeight)
        {
            float safeWidth = Mathf.Max(minimumWidth, width.ResolvePixels(screenWidth));
            float safeHeight = Mathf.Max(minimumHeight, height.ResolvePixels(screenHeight));

            float horizontalPadding = Mathf.Max(screenPadding, MinimumPadding);
            float verticalPadding = Mathf.Max(screenPadding, MinimumPadding);

            float maxWidth = Mathf.Max(minimumWidth, screenWidth - (horizontalPadding * 2f));
            float maxHeight = Mathf.Max(minimumHeight, screenHeight - (verticalPadding * 2f));

            safeWidth = Mathf.Min(safeWidth, maxWidth);
            safeHeight = Mathf.Min(safeHeight, maxHeight);

            float left = Mathf.Clamp(
                (screenWidth * horizontalAnchor) - (safeWidth * 0.5f),
                horizontalPadding,
                Mathf.Max(horizontalPadding, screenWidth - safeWidth - horizontalPadding)
            );
            float top = Mathf.Clamp(
                (screenHeight * verticalAnchor) - (safeHeight * 0.5f),
                verticalPadding,
                Mathf.Max(verticalPadding, screenHeight - safeHeight - verticalPadding)
            );

            float maxHistoryHeight = Mathf.Max(0f, safeHeight - inputReservePixels);
            float historyPixels = Mathf.Min(
                maxHistoryHeight,
                historyHeight.ResolvePixels(screenHeight, safeHeight)
            );

            if (historyPixels < 0f)
            {
                historyPixels = 0f;
            }

            return new LauncherLayoutMetrics(
                width: safeWidth,
                height: safeHeight,
                left: left,
                top: top,
                historyHeight: historyPixels,
                cornerRadius: Mathf.Max(cornerRadius, MinimumCorner),
                insetPadding: Mathf.Max(insetPadding, MinimumPadding),
                historyVisibleEntryCount: Mathf.Max(0, historyVisibleEntryCount),
                historyFadeExponent: historyFadeExponent,
                snapOpen: snapOpen,
                animationDuration: Mathf.Max(0f, animationDuration)
            );
        }
    }

    public readonly struct LauncherLayoutMetrics
    {
        public LauncherLayoutMetrics(
            float width,
            float height,
            float left,
            float top,
            float historyHeight,
            float cornerRadius,
            float insetPadding,
            int historyVisibleEntryCount,
            float historyFadeExponent,
            bool snapOpen,
            float animationDuration
        )
        {
            Width = width;
            Height = height;
            Left = left;
            Top = top;
            HistoryHeight = historyHeight;
            CornerRadius = cornerRadius;
            InsetPadding = insetPadding;
            HistoryVisibleEntryCount = historyVisibleEntryCount;
            HistoryFadeExponent = historyFadeExponent;
            SnapOpen = snapOpen;
            AnimationDuration = animationDuration;
        }

        public float Width { get; }

        public float Height { get; }

        public float Left { get; }

        public float Top { get; }

        public float HistoryHeight { get; }

        public float CornerRadius { get; }

        public float InsetPadding { get; }

        public int HistoryVisibleEntryCount { get; }

        public float HistoryFadeExponent { get; }

        public bool SnapOpen { get; }

        public float AnimationDuration { get; }
    }
}
