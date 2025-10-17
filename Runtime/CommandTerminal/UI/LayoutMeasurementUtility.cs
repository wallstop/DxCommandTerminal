namespace WallstopStudios.DxCommandTerminal.UI
{
    using UnityEngine;
    using UnityEngine.UIElements;

    internal static class LayoutMeasurementUtility
    {
        internal static float ClampPositive(float value)
        {
            if (float.IsNaN(value) || value < 0f)
            {
                return 0f;
            }

            return value;
        }

        internal static float ClampToRange(float value, float min, float max)
        {
            if (float.IsNaN(value))
            {
                return min;
            }

            return Mathf.Clamp(value, min, max);
        }

        internal static float ComputeAverageRowHeight(
            float totalHeight,
            int visibleCount,
            float fallback
        )
        {
            if (visibleCount <= 0)
            {
                return fallback;
            }

            float positiveHeight = ClampPositive(totalHeight);
            if (positiveHeight <= 0f)
            {
                return fallback;
            }

            float average = positiveHeight / visibleCount;
            if (float.IsNaN(average) || average <= 0f)
            {
                return fallback;
            }

            return average;
        }

        internal static float ClampRowHeightEstimate(
            float estimate,
            float fallback,
            float minimum,
            float maximum
        )
        {
            float sanitized = ClampPositive(estimate);
            if (sanitized <= 0f)
            {
                sanitized = fallback;
            }

            return Mathf.Clamp(sanitized, minimum, maximum);
        }

        internal static float ResolvePadding(float resolved, StyleLength styleValue)
        {
            float normalizedResolved = ClampPositive(resolved);
            if (normalizedResolved > 0f)
            {
                return normalizedResolved;
            }

            if (styleValue.keyword == StyleKeyword.Auto || styleValue.keyword == StyleKeyword.None)
            {
                return 0f;
            }

            return ClampPositive(styleValue.value.value);
        }

        internal static float ComputeReservedSuggestionHeight(
            bool isLauncherActive,
            bool hasSuggestions,
            float suggestionsHeight,
            float spacingAboveHistory,
            float autoCompleteSpacing
        )
        {
            if (!hasSuggestions)
            {
                return 0f;
            }

            if (isLauncherActive)
            {
                return suggestionsHeight + spacingAboveHistory;
            }

            float standardSpacing = Mathf.Max(2f, autoCompleteSpacing * 0.25f);
            return suggestionsHeight + standardSpacing;
        }

        internal static float ComputeStandardContainerHeight(
            float currentHeight,
            float paddingTop,
            float paddingBottom
        )
        {
            float innerHeight = ClampPositive(currentHeight - paddingTop - paddingBottom);
            return paddingTop + innerHeight + paddingBottom;
        }

        internal static float ClampToHistoryLimit(float value, float historyLimit)
        {
            return Mathf.Min(historyLimit, ClampPositive(value));
        }

        internal static float ComputeDesiredHistoryHeight(
            bool hasHistory,
            float fallbackHeight,
            float historyLimit
        )
        {
            if (!hasHistory)
            {
                return 0f;
            }

            float sanitized = ClampPositive(fallbackHeight);
            return Mathf.Min(historyLimit, sanitized);
        }
    }
}
