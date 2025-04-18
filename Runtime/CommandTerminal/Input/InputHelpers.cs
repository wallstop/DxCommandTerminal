namespace WallstopStudios.DxCommandTerminal.Input
{
    using System;
    using System.Collections.Generic;
    using Extensions;
    using UnityEngine;
#if ENABLE_INPUT_SYSTEM
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.Controls;
#endif

    public static class InputHelpers
    {
        private static readonly string[] ShiftModifiers = { "shift+", "#" };

        private static readonly Dictionary<string, string> CachedSubstrings = new();

        private static readonly Dictionary<string, KeyCode> KeyCodeMapping = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            { "~", KeyCode.BackQuote },
            { "`", KeyCode.BackQuote },
            { "!", KeyCode.Alpha1 },
            { "@", KeyCode.Alpha2 },
            { "#", KeyCode.Alpha3 },
            { "$", KeyCode.Alpha4 },
            { "%", KeyCode.Alpha5 },
            { "^", KeyCode.Alpha6 },
            { "&", KeyCode.Alpha7 },
            { "*", KeyCode.Alpha8 },
            { "(", KeyCode.Alpha9 },
            { ")", KeyCode.Alpha0 },
            { "-", KeyCode.Minus },
            { "_", KeyCode.Minus },
            { "=", KeyCode.Equals },
            { "+", KeyCode.Equals },
            { "[", KeyCode.LeftBracket },
            { "{", KeyCode.LeftBracket },
            { "]", KeyCode.RightBracket },
            { "}", KeyCode.RightBracket },
            { "\\", KeyCode.Backslash },
            { "|", KeyCode.Backslash },
            { ";", KeyCode.Semicolon },
            { ":", KeyCode.Semicolon },
            { "'", KeyCode.Quote },
            { "\"", KeyCode.Quote },
            { ",", KeyCode.Comma },
            { "<", KeyCode.Comma },
            { ".", KeyCode.Period },
            { ">", KeyCode.Period },
            { "/", KeyCode.Slash },
            { "?", KeyCode.Slash },
            { "1", KeyCode.Alpha1 },
            { "2", KeyCode.Alpha2 },
            { "3", KeyCode.Alpha3 },
            { "4", KeyCode.Alpha4 },
            { "5", KeyCode.Alpha5 },
            { "6", KeyCode.Alpha6 },
            { "7", KeyCode.Alpha7 },
            { "8", KeyCode.Alpha8 },
            { "9", KeyCode.Alpha9 },
            { "0", KeyCode.Alpha0 },
            { "numpad1", KeyCode.Keypad1 },
            { "keypad1", KeyCode.Keypad1 },
            { "numpad2", KeyCode.Keypad2 },
            { "keypad2", KeyCode.Keypad2 },
            { "numpad3", KeyCode.Keypad3 },
            { "keypad3", KeyCode.Keypad3 },
            { "numpad4", KeyCode.Keypad4 },
            { "keypad4", KeyCode.Keypad4 },
            { "numpad5", KeyCode.Keypad5 },
            { "keypad5", KeyCode.Keypad5 },
            { "numpad6", KeyCode.Keypad6 },
            { "keypad6", KeyCode.Keypad6 },
            { "numpad7", KeyCode.Keypad7 },
            { "keypad7", KeyCode.Keypad7 },
            { "numpad8", KeyCode.Keypad8 },
            { "keypad8", KeyCode.Keypad8 },
            { "numpad9", KeyCode.Keypad9 },
            { "keypad9", KeyCode.Keypad9 },
            { "numpad0", KeyCode.Keypad0 },
            { "keypad0", KeyCode.Keypad0 },
            { "numpadplus", KeyCode.KeypadPlus },
            { "keypadplus", KeyCode.KeypadPlus },
            { "numpad+", KeyCode.KeypadPlus },
            { "numpadminus", KeyCode.KeypadMinus },
            { "keypadminus", KeyCode.KeypadMinus },
            { "numpad-", KeyCode.KeypadMinus },
            { "numpadmultiply", KeyCode.KeypadMultiply },
            { "keypadmultiply", KeyCode.KeypadMultiply },
            { "numpad*", KeyCode.KeypadMultiply },
            { "numpaddivide", KeyCode.KeypadDivide },
            { "keypaddivide", KeyCode.KeypadDivide },
            { "numpad/", KeyCode.KeypadDivide },
            { "numpadenter", KeyCode.KeypadEnter },
            { "keypadenter", KeyCode.KeypadEnter },
            { "numpadperiod", KeyCode.KeypadPeriod },
            { "keypadperiod", KeyCode.KeypadPeriod },
            { "numpad.", KeyCode.KeypadPeriod },
            { "numpaddecimal", KeyCode.KeypadPeriod },
            { "numpadequals", KeyCode.KeypadEquals },
            { "keypadequals", KeyCode.KeypadEquals },
            { "numpad=", KeyCode.KeypadEquals },
            { "esc", KeyCode.Escape },
            { "escape", KeyCode.Escape },
            { "return", KeyCode.Return },
            { "enter", KeyCode.Return },
            { "space", KeyCode.Space },
            { "spacebar", KeyCode.Space },
            { "del", KeyCode.Delete },
            { "delete", KeyCode.Delete },
            { "ins", KeyCode.Insert },
            { "insert", KeyCode.Insert },
            { "pageup", KeyCode.PageUp },
            { "pgup", KeyCode.PageUp },
            { "pagedown", KeyCode.PageDown },
            { "pgdn", KeyCode.PageDown },
            { "pagedn", KeyCode.PageDown },
            { "lshift", KeyCode.LeftShift },
            { "rshift", KeyCode.RightShift },
            { "leftshift", KeyCode.LeftShift },
            { "rightshift", KeyCode.RightShift },
            { "lctrl", KeyCode.LeftControl },
            { "rctrl", KeyCode.RightControl },
            { "lcontrol", KeyCode.LeftControl },
            { "rcontrol", KeyCode.RightControl },
            { "leftctrl", KeyCode.LeftControl },
            { "rightctrl", KeyCode.RightControl },
            { "leftcontrol", KeyCode.LeftControl },
            { "rightcontrol", KeyCode.RightControl },
            { "lalt", KeyCode.LeftAlt },
            { "ralt", KeyCode.RightAlt },
            { "leftalt", KeyCode.LeftAlt },
            { "rightalt", KeyCode.RightAlt },
            { "lcmd", KeyCode.LeftCommand },
            { "rcmd", KeyCode.RightCommand },
            { "lcommand", KeyCode.LeftCommand },
            { "rcommand", KeyCode.RightCommand },
            { "leftcmd", KeyCode.LeftCommand },
            { "rightcmd", KeyCode.RightCommand },
            { "leftcommand", KeyCode.LeftCommand },
            { "rightcommand", KeyCode.RightCommand },
            { "lwin", KeyCode.LeftWindows },
            { "rwin", KeyCode.RightWindows },
            { "leftwindows", KeyCode.LeftWindows },
            { "rightwindows", KeyCode.RightWindows },
            { "leftwin", KeyCode.LeftWindows },
            { "rightwin", KeyCode.RightWindows },
            { "capslock", KeyCode.CapsLock },
            { "numlock", KeyCode.Numlock },
            { "scrolllock", KeyCode.ScrollLock },
            { "prtscn", KeyCode.Print },
            { "printscreen", KeyCode.Print },
            { "pausebreak", KeyCode.Pause },
            { "pause", KeyCode.Pause },
            { "up", KeyCode.UpArrow },
            { "uparrow", KeyCode.UpArrow },
            { "down", KeyCode.DownArrow },
            { "downarrow", KeyCode.DownArrow },
            { "left", KeyCode.LeftArrow },
            { "leftarrow", KeyCode.LeftArrow },
            { "right", KeyCode.RightArrow },
            { "rightarrow", KeyCode.RightArrow },
            { "f1", KeyCode.F1 },
            { "f2", KeyCode.F2 },
            { "f3", KeyCode.F3 },
            { "f4", KeyCode.F4 },
            { "f5", KeyCode.F5 },
            { "f6", KeyCode.F6 },
            { "f7", KeyCode.F7 },
            { "f8", KeyCode.F8 },
            { "f9", KeyCode.F9 },
            { "f10", KeyCode.F10 },
            { "f11", KeyCode.F11 },
            { "f12", KeyCode.F12 },
            { "f13", KeyCode.F13 },
            { "f14", KeyCode.F14 },
            { "f15", KeyCode.F15 },
            { "mouse0", KeyCode.Mouse0 },
            { "leftmouse", KeyCode.Mouse0 },
            { "lmb", KeyCode.Mouse0 },
            { "mouse1", KeyCode.Mouse1 },
            { "rightmouse", KeyCode.Mouse1 },
            { "rmb", KeyCode.Mouse1 },
            { "mouse2", KeyCode.Mouse2 },
            { "middlemouse", KeyCode.Mouse2 },
            { "mmb", KeyCode.Mouse2 },
            { "mouse3", KeyCode.Mouse3 },
            { "mouse4", KeyCode.Mouse4 },
            { "mouse5", KeyCode.Mouse5 },
            { "mouse6", KeyCode.Mouse6 },
            { "none", KeyCode.None },
        };

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
                    AlternativeSpecialShiftedKeyCodeMap.TryGetValue(
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
                if (!CachedSubstrings.TryGetValue(key, out keyName))
                {
                    keyName = key[startIndex..];
                    if (keyName.NeedsTrim())
                    {
                        keyName = keyName.Trim();
                    }

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
#pragma warning disable CS0612 // Type or member is obsolete
            if (inputMode == InputMode.LegacyInputSystem)
#pragma warning restore CS0612 // Type or member is obsolete
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                if (
                    Enum.TryParse(keyName, ignoreCase: true, out KeyCode keyCode)
                    || KeyCodeMapping.TryGetValue(keyName, out keyCode)
                )
                {
                    return Input.GetKeyDown(keyCode)
                        && (
                            !shiftRequired
                            || Input.GetKey(KeyCode.LeftShift)
                            || Input.GetKey(KeyCode.LeftShift)
                            || Input.GetKey(KeyCode.RightShift)
                            || Input.GetKey(KeyCode.RightShift)
                        );
                }
#endif

                return false;
            }
#pragma warning disable CS0612 // Type or member is obsolete
            if (inputMode == InputMode.NewInputSystem)
#pragma warning restore CS0612 // Type or member is obsolete
            {
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
            }
            return false;
        }
    }
}
