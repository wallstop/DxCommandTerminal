namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.UIElements;

    public sealed class LayoutMeasurementUtilityTests
    {
        [Test]
        public void ClampPositiveReturnsZeroForNegative()
        {
            float result = LayoutMeasurementUtility.ClampPositive(-5f);
            Assert.That(result, Is.EqualTo(0f));
        }

        [Test]
        public void ComputeAverageRowHeightFallsBackWhenHeightZero()
        {
            float average = LayoutMeasurementUtility.ComputeAverageRowHeight(0f, 3, 10f);
            Assert.That(average, Is.EqualTo(10f));
        }

        [Test]
        public void ComputeReservedSuggestionHeightUsesLauncherSpacing()
        {
            float reserved = LayoutMeasurementUtility.ComputeReservedSuggestionHeight(
                false,
                true,
                suggestionsHeight: 20f,
                spacingAboveHistory: 4f,
                autoCompleteSpacing: 6f
            );
            Assert.That(reserved, Is.EqualTo(20f + Mathf.Max(2f, 6f * 0.25f)));
        }

        [Test]
        public void ClampToHistoryLimitRestrictsValue()
        {
            float clamped = LayoutMeasurementUtility.ClampToHistoryLimit(250f, 200f);
            Assert.That(clamped, Is.EqualTo(200f));
        }

        [Test]
        public void ComputeStandardContainerHeightClampsToPositive()
        {
            float height = LayoutMeasurementUtility.ComputeStandardContainerHeight(50f, 10f, 5f);
            Assert.That(height, Is.EqualTo(50f));

            float clamped = LayoutMeasurementUtility.ComputeStandardContainerHeight(10f, 8f, 5f);
            Assert.That(clamped, Is.EqualTo(13f));
        }

        [Test]
        public void ResolvePaddingFallsBackToStyleValue()
        {
            StyleLength stylePadding = new StyleLength(12f);
            float resolved = 0f;

            float result = LayoutMeasurementUtility.ResolvePadding(resolved, stylePadding);
            Assert.That(result, Is.EqualTo(12f));

            result = LayoutMeasurementUtility.ResolvePadding(4f, stylePadding);
            Assert.That(result, Is.EqualTo(4f));
        }
    }
}
