namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     One completion candidate produced by a
    ///     <see cref="CommandCompletionProvider"/>. Insertion text is
    ///     required; display and description are optional presentation data.
    /// </summary>
    public readonly struct CommandCompletion
    {
        /// <summary>
        ///     Sentinel replacement range meaning "replace the active token
        ///     range reported by the completion context".
        /// </summary>
        public const int UseContextReplacement = -1;

        /// <summary>
        ///     Text inserted when the completion is accepted. Values
        ///     containing whitespace or quotes are quoted on insertion when
        ///     the active token is not already quoted.
        /// </summary>
        public string InsertionText { get; }

        /// <summary>Label shown where a UI displays candidates. Falls back to
        /// the insertion text.</summary>
        public string DisplayLabel { get; }

        /// <summary>Optional help or description text.</summary>
        public string Description { get; }

        /// <summary>
        ///     Start index of the replaced range within the original input, or
        ///     <see cref="UseContextReplacement"/> to replace the active
        ///     token range from the completion context.
        /// </summary>
        public int ReplacementStart { get; }

        /// <summary>
        ///     Length of the replaced range within the original input, or
        ///     <see cref="UseContextReplacement"/> to replace the active
        ///     token range from the completion context.
        /// </summary>
        public int ReplacementLength { get; }

        public CommandCompletion(
            string insertionText,
            string displayLabel = null,
            string description = null,
            int replacementStart = UseContextReplacement,
            int replacementLength = UseContextReplacement
        )
        {
            InsertionText = insertionText ?? throw new ArgumentNullException(nameof(insertionText));
            DisplayLabel = displayLabel;
            Description = description;
            ReplacementStart = replacementStart;
            ReplacementLength = replacementLength;
        }

        public bool HasReplacementOverride => UseContextReplacement < ReplacementStart;

        /// <summary>Effective display label, never null.</summary>
        public string EffectiveDisplayLabel => DisplayLabel ?? InsertionText;
    }
}
