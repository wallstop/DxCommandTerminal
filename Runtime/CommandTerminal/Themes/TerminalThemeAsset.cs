namespace WallstopStudios.DxCommandTerminal.Themes
{
    using System.Globalization;
    using System.Text;
    using UnityEngine;

    /*
        Authoring-only theme asset (issue #72, option A): one asset per theme
        with color fields for the 19 custom properties every terminal theme
        sheet must define. An Editor postprocessor auto-writes the sibling
        .uss file on every asset change; the generated sheet drops into a
        TerminalThemePack like any hand-written one and is never read back by
        this asset, so runtime behavior and shipped packs are unchanged.
     */
    [CreateAssetMenu(
        menuName = "Wallstop Studios/DxCommandTerminal/Theme Asset",
        fileName = "NewTerminalThemeAsset",
        order = 1_111_124
    )]
    public sealed class TerminalThemeAsset : ScriptableObject
    {
        /*
            The CSS class the generated sheet targets, derived from the asset
            name so `set-theme` and the pack's theme-name list resolve it the
            same way as the shipped sheets ("DarkTheme" -> "dark-theme").
         */
        public string CssClassName
        {
            get
            {
                string kebab = KebabCase(name);
                if (kebab.EndsWith("-theme"))
                {
                    return kebab;
                }

                return kebab + "-theme";
            }
        }

        [Header("Backgrounds")]
        [SerializeField]
        [Tooltip("Terminal window background")]
        private Color _terminalBg = new(0.1098f, 0.1098f, 0.1176f, 0.9f);

        [SerializeField]
        [Tooltip("Button background")]
        private Color _buttonBg = new(0.2275f, 0.2275f, 0.2353f, 1f);

        [SerializeField]
        [Tooltip("Input field background")]
        private Color _inputFieldBg = new(0.1725f, 0.1725f, 0.1804f, 0.8f);

        [SerializeField]
        [Tooltip("Selected button background")]
        private Color _buttonSelectedBg = new(0f, 0.4784f, 1f, 0.85f);

        [SerializeField]
        [Tooltip("Button hover background")]
        private Color _buttonHoverBg = new(0.2824f, 0.2824f, 0.2902f, 1f);

        [SerializeField]
        [Tooltip("Scrollbar background")]
        private Color _scrollBg = new(0.2275f, 0.2275f, 0.2353f, 1f);

        [SerializeField]
        [Tooltip("Scrollbar inverse background")]
        private Color _scrollInverseBg = new(0.3529f, 0.3529f, 0.3529f, 1f);

        [SerializeField]
        [Tooltip("Scrollbar active (dragged) background")]
        private Color _scrollActiveBg = new(0.4314f, 0.4314f, 0.4314f, 1f);

        [Header("Text & Foreground")]
        [SerializeField]
        [Tooltip("Button text color")]
        private Color _buttonText = new(0.949f, 0.949f, 0.9686f, 0.9f);

        [SerializeField]
        [Tooltip("Selected button text color")]
        private Color _buttonSelectedText = new(1f, 1f, 1f, 1f);

        [SerializeField]
        [Tooltip("Button hover text color")]
        private Color _buttonHoverText = new(0.949f, 0.949f, 0.9686f, 1f);

        [SerializeField]
        [Tooltip("Input text color")]
        private Color _inputTextColor = new(0.949f, 0.949f, 0.9686f, 1f);

        [SerializeField]
        [Tooltip("Message log text color")]
        private Color _textMessage = new(0.949f, 0.949f, 0.9686f, 1f);

        [SerializeField]
        [Tooltip("Warning log text color")]
        private Color _textWarning = new(1f, 0.8f, 0f, 1f);

        [SerializeField]
        [Tooltip("Input echo text color")]
        private Color _textInputEcho = new(0.1961f, 0.6784f, 0.902f, 1f);

        [SerializeField]
        [Tooltip("Shell prompt text color")]
        private Color _textShell = new(0.5569f, 0.5569f, 0.5765f, 1f);

        [SerializeField]
        [Tooltip("Error log text color")]
        private Color _textError = new(1f, 0.2706f, 0.2275f, 1f);

        [Header("Other UI Elements")]
        [SerializeField]
        [Tooltip("Scrollbar handle color")]
        private Color _scrollColor = new(0.949f, 0.949f, 0.9686f, 1f);

        [SerializeField]
        [Tooltip("Input caret color")]
        private Color _caretColor = new(0.949f, 0.949f, 0.9686f, 1f);

        internal static string KebabCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed-theme";
            }

            StringBuilder builder = new(value.Length + 8);
            bool previousWasBoundary = true;
            foreach (char c in value)
            {
                if (char.IsUpper(c))
                {
                    if (!previousWasBoundary)
                    {
                        builder.Append('-');
                    }

                    builder.Append(char.ToLowerInvariant(c));
                    previousWasBoundary = false;
                    continue;
                }

                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                    previousWasBoundary = false;
                    continue;
                }

                if (!previousWasBoundary)
                {
                    builder.Append('-');
                    previousWasBoundary = true;
                }
            }

            while (0 < builder.Length && builder[builder.Length - 1] == '-')
            {
                builder.Length -= 1;
            }

            return builder.Length == 0 ? "unnamed-theme" : builder.ToString();
        }

        private static void AppendToken(StringBuilder builder, string token, Color color)
        {
            builder
                .Append("    ")
                .Append(token)
                .Append(": rgba(")
                .Append(Channel(color.r))
                .Append(", ")
                .Append(Channel(color.g))
                .Append(", ")
                .Append(Channel(color.b))
                .Append(", ")
                .Append(Alpha(color.a))
                .AppendLine(");");
        }

        private static string Channel(float value)
        {
            int channel = Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
            return channel.ToString(CultureInfo.InvariantCulture);
        }

        private static string Alpha(float value)
        {
            /*
                Three decimals match the precision every shipped sheet uses;
                trailing zeros are trimmed so alpha 1 renders "1", not "1.0".
             */
            float alpha = Mathf.Clamp01(value);
            string formatted = alpha.ToString("0.###", CultureInfo.InvariantCulture);
            return formatted.Length == 0 ? "0" : formatted;
        }

        public string BuildUss()
        {
            string cssClass = CssClassName;
            StringBuilder builder = new(1024);
            builder.Append('.').Append(cssClass).AppendLine(" {");
            builder.AppendLine("    /* Backgrounds */");
            AppendToken(builder, "--terminal-bg", _terminalBg);
            AppendToken(builder, "--button-bg", _buttonBg);
            AppendToken(builder, "--input-field-bg", _inputFieldBg);
            AppendToken(builder, "--button-selected-bg", _buttonSelectedBg);
            AppendToken(builder, "--button-hover-bg", _buttonHoverBg);
            AppendToken(builder, "--scroll-bg", _scrollBg);
            AppendToken(builder, "--scroll-inverse-bg", _scrollInverseBg);
            AppendToken(builder, "--scroll-active-bg", _scrollActiveBg);
            builder.AppendLine();
            builder.AppendLine("    /* Text & Foreground */");
            AppendToken(builder, "--button-text", _buttonText);
            AppendToken(builder, "--button-selected-text", _buttonSelectedText);
            AppendToken(builder, "--button-hover-text", _buttonHoverText);
            AppendToken(builder, "--input-text-color", _inputTextColor);
            AppendToken(builder, "--text-message", _textMessage);
            AppendToken(builder, "--text-warning", _textWarning);
            AppendToken(builder, "--text-input-echo", _textInputEcho);
            AppendToken(builder, "--text-shell", _textShell);
            AppendToken(builder, "--text-error", _textError);
            builder.AppendLine();
            builder.AppendLine("    /* Other UI Elements */");
            AppendToken(builder, "--scroll-color", _scrollColor);
            AppendToken(builder, "--caret-color", _caretColor);
            builder.Append('}').AppendLine();
            return builder.ToString();
        }
    }
}
