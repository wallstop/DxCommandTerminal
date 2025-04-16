namespace WallstopStudios.DxCommandTerminal.Input
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using UnityEngine;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;

    public static class InputHelpers
    {
        private static readonly string[] ShiftModifiers = { "shift+", "#" };

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

        public static bool IsKeyPressed(string key, InputMode inputMode)
        {
#pragma warning disable CS0612 // Type or member is obsolete
            if (inputMode == InputMode.None)
#pragma warning restore CS0612 // Type or member is obsolete
            {
                return false;
            }

            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            bool shiftRequired = false;
            string keyName = key;
            int startIndex = 0;

            foreach (string shiftModifier in ShiftModifiers)
            {
                if (
                    key.StartsWith(shiftModifier, StringComparison.OrdinalIgnoreCase)
                    && key != shiftModifier
                )
                {
                    shiftRequired = true;
                    startIndex = shiftModifier.Length;
                    break;
                }
            }

            if (!shiftRequired && key.Length == 1)
            {
                char keyChar = key[0];
                if (char.IsUpper(keyChar) && char.IsLetter(keyChar))
                {
                    shiftRequired = true;
                }
                else if (
                    inputMode == InputMode.LegacyInputSystem
                    && AlternativeSpecialShiftedKeyCodeMap.TryGetValue(
                        key,
                        out string legacyShiftedKeyName
                    )
                )
                {
                    shiftRequired = true;
                    keyName = legacyShiftedKeyName;
                }
            }

            if (0 < startIndex)
            {
                if (CachedSubstrings.TryGetValue(key, out keyName))
                {
                    keyName = key[startIndex..].Trim();
                    if (keyName.Length == 1 && keyName.NeedsLowerInvariantConversion())
                    {
                        keyName = keyName.ToLowerInvariant();
                    }
                    CachedSubstrings[key] = keyName;
                }
            }

            if (string.IsNullOrWhiteSpace(keyName))
            {
                return false;
            }

            if (inputMode == InputMode.LegacyInputSystem)
            {
                if (Enum.TryParse(keyName, ignoreCase: true, out KeyCode keyCode))
                {
                    return Input.GetKey(keyCode)
                        && (
                            !shiftRequired
                            || Input.GetKey(KeyCode.LeftShift)
                            || Input.GetKey(KeyCode.RightShift)
                        );
                }

                return false;
            }

#if ENABLE_INPUT_SYSTEM
            if (
                !shiftRequired
                && (
                    AlternativeSpecialShiftedKeyCodeMap.TryGetValue(
                        keyName,
                        out string shiftedKeyName
                    ) || SpecialShiftedKeyCodeMap.TryGetValue(keyName, out shiftedKeyName)
                )
            )
            {
                shiftRequired = true;
                keyName = shiftedKeyName;
            }

            Keyboard currentKeyboard = Keyboard.current;
            return (!shiftRequired || currentKeyboard.shiftKey.isPressed)
                && (
                    currentKeyboard.TryGetChildControl<KeyControl>(
                        SpecialKeyCodeMap.GetValueOrDefault(keyName, keyName)
                    )
                        is { wasPressedThisFrame: true }
                    || currentKeyboard.TryGetChildControl<KeyControl>(keyName)
                        is { wasPressedThisFrame: true }
                );
#endif
            return false;
        }
    }
}
