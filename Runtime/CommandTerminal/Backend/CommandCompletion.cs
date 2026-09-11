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
        ///     Replacement range within the original input, or <c>null</c> to
        ///     replace the active token range reported by the completion
        ///     context.
        /// </summary>
        public CommandCompletionReplacement? Replacement { get; }

        public bool HasReplacementOverride => Replacement is CommandCompletionReplacement;

        /// <summary>Effective display label, never null.</summary>
        public string EffectiveDisplayLabel => DisplayLabel ?? InsertionText;

        public CommandCompletion(
            string insertionText,
            string displayLabel = null,
            string description = null,
            CommandCompletionReplacement? replacement = null
        )
        {
            InsertionText = insertionText ?? throw new ArgumentNullException(nameof(insertionText));
            DisplayLabel = displayLabel;
            Description = description;
            Replacement = replacement;
        }
    }
}
