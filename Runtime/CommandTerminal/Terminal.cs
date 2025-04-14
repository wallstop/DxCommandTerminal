namespace CommandTerminal
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using JetBrains.Annotations;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;
#endif

    public static class Terminal
    {
        public static CommandLog Buffer { get; internal set; }
        public static CommandShell Shell { get; internal set; }
        public static CommandHistory History { get; internal set; }
        public static CommandAutoComplete AutoComplete { get; internal set; }

        public static IReadOnlyDictionary<string, string> SpecialKeyCodes => SpecialKeyCodeMap;
        public static IReadOnlyDictionary<string, string> SpecialShiftedKeyCodes =>
            SpecialShiftedKeyCodeMap;

        public static IReadOnlyDictionary<string, string> AlternativeSpecialShiftedKeyCodes =>
            AlternativeSpecialShiftedKeyCodeMap;

        private static readonly Dictionary<string, string> CachedSubstrings = new();

        private static readonly Dictionary<string, string> SpecialKeyCodeMap = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            { "`", "backquote" },
            { "-", "minus" },
            { "=", "equals" },
            { "[", "leftBracket" },
            { "]", "rightBracket" },
            { ";", "semicolon" },
            { "'", "quote" },
            { "\\", "backslash" },
            { ",", "comma" },
            { ".", "period" },
            { "/", "slash" },
            { "1", "digit1" },
            { "2", "digit2" },
            { "3", "digit3" },
            { "4", "digit4" },
            { "5", "digit5" },
            { "6", "digit6" },
            { "7", "digit7" },
            { "8", "digit8" },
            { "9", "digit9" },
            { "0", "digit0" },
            { "up", "upArrow" },
            { "left", "leftArrow" },
            { "right", "rightArrow" },
            { "down", "downArrow" },
            { " ", "space" },
        };

        private static readonly Dictionary<string, string> SpecialShiftedKeyCodeMap = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            { "~", "backquote" },
            { "!", "digit1" },
            { "@", "digit2" },
            { "#", "digit3" },
            { "$", "digit4" },
            { "^", "digit6" },
            { "%", "digit5" },
            { "&", "digit7" },
            { "*", "digit8" },
            { "(", "digit9" },
            { ")", "digit0" },
            { "_", "minus" },
            { "+", "equals" },
            { "{", "leftBracket" },
            { "}", "rightBracket" },
            { ":", "semicolon" },
            { "\"", "quote" },
            { "|", "backslash" },
            { "<", "comma" },
            { ">", "period" },
            { "?", "slash" },
        };

        private static readonly Dictionary<string, string> AlternativeSpecialShiftedKeyCodeMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "!", "1" },
                { "@", "2" },
                { "#", "3" },
                { "$", "4" },
                { "^", "5" },
                { "%", "6" },
                { "&", "7" },
                { "*", "8" },
                { "(", "9" },
                { ")", "0" },
            };

        [StringFormatMethod("format")]
        public static bool Log(string format, params object[] parameters)
        {
            return Log(TerminalLogType.ShellMessage, format, parameters);
        }

        [StringFormatMethod("format")]
        public static bool Log(TerminalLogType type, string format, params object[] parameters)
        {
            CommandLog buffer = Buffer;
            if (buffer == null)
            {
                return false;
            }

            string formattedMessage = parameters is { Length: > 0 }
                ? string.Format(format, parameters)
                : format;
            return buffer.HandleLog(formattedMessage, type);
        }

#if ENABLE_INPUT_SYSTEM
        internal static bool IsKeyPressed(string key)
        {
            if (1 < key.Length && key.StartsWith("#", StringComparison.OrdinalIgnoreCase))
            {
                if (!CachedSubstrings.TryGetValue(key, out string expected))
                {
                    expected = key.Substring(1);
                    if (expected.Length == 1 && expected.NeedsLowerInvariantConversion())
                    {
                        expected = expected.ToLowerInvariant();
                    }

                    CachedSubstrings[key] = expected;
                }

                return Keyboard.current.shiftKey.isPressed
                    && (
                        Keyboard.current.TryGetChildControl<KeyControl>(
                            SpecialKeyCodes.GetValueOrDefault(expected, expected)
                        )
                            is { wasPressedThisFrame: true }
                        || Keyboard.current.TryGetChildControl<KeyControl>(expected)
                            is { wasPressedThisFrame: true }
                    );
            }

            const string shiftModifier = "shift+";
            if (
                shiftModifier.Length < key.Length
                && key.StartsWith(shiftModifier, StringComparison.OrdinalIgnoreCase)
            )
            {
                if (!CachedSubstrings.TryGetValue(key, out string expected))
                {
                    expected = key.Substring(shiftModifier.Length);
                    if (expected.Length == 1 && expected.NeedsLowerInvariantConversion())
                    {
                        expected = expected.ToLowerInvariant();
                    }

                    CachedSubstrings[key] = expected;
                }

                return Keyboard.current.shiftKey.isPressed
                    && (
                        Keyboard.current.TryGetChildControl<KeyControl>(
                            SpecialKeyCodes.GetValueOrDefault(expected, expected)
                        )
                            is { wasPressedThisFrame: true }
                        || Keyboard.current.TryGetChildControl<KeyControl>(expected)
                            is { wasPressedThisFrame: true }
                    );
            }
            else if (
                SpecialShiftedKeyCodes.TryGetValue(key, out string expected)
                && Keyboard.current.shiftKey.isPressed
                && Keyboard.current.TryGetChildControl<KeyControl>(expected)
                    is { wasPressedThisFrame: true }
            )
            {
                return true;
            }
            else if (
                AlternativeSpecialShiftedKeyCodes.TryGetValue(key, out expected)
                && Keyboard.current.shiftKey.isPressed
                && Keyboard.current.TryGetChildControl<KeyControl>(expected)
                    is { wasPressedThisFrame: true }
            )
            {
                return true;
            }
            else if (key.Length == 1 && key.NeedsLowerInvariantConversion())
            {
                key = key.ToLowerInvariant();
                return Keyboard.current.shiftKey.isPressed
                    && Keyboard.current.TryGetChildControl<KeyControl>(key)
                        is { wasPressedThisFrame: true };
            }
            else
            {
                return Keyboard.current.TryGetChildControl<KeyControl>(
                        SpecialKeyCodes.GetValueOrDefault(key, key)
                    )
                        is { wasPressedThisFrame: true }
                    || Keyboard.current.TryGetChildControl<KeyControl>(key)
                        is { wasPressedThisFrame: true };
            }
        }

#endif
    }
}
