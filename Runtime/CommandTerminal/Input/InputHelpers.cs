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

        private static readonly string[] CtrlModifiers = { "ctrl+", "control+" };

        /*
            The names that type no character, from both reachable surfaces: the
            Input System's keyboard control names, every multi-character key in
            KeyCodeMapping, and the legacy KeyCode members the parse can name
            directly - navigation, editing, function, modifier, lock, media,
            IME, mouse, and joystick. Everything else the parse surface can
            produce is a character key, so an unrecognized name counts as
            typing - a hotkey held back for one typing session is recoverable,
            a swallowed character is not. A key either input system adds later
            therefore fails toward the character.

            anyKey is deliberately absent: it presses on every keystroke, so a
            binding on it is held back while a field has focus, like a
            character key's binding is.
         */
        private static readonly HashSet<string> NonTypedKeyNames = BuildNonTypedKeyNames();

        private static readonly Dictionary<string, CachedKeyName> CachedKeys = new();

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
            if (inputMode == default)
            {
                return false;
            }

            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (!CachedKeys.TryGetValue(key, out CachedKeyName cached))
            {
                cached = ResolveKeyName(key);
                CachedKeys[key] = cached;
            }

            string resolvedName = cached.Name;
            if (string.IsNullOrWhiteSpace(resolvedName))
            {
                return false;
            }

#pragma warning disable CS0612 // Type or member is obsolete
            if (inputMode == InputMode.LegacyInputSystem)
#pragma warning restore CS0612 // Type or member is obsolete
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                if (
                    Enum.TryParse(resolvedName, ignoreCase: true, out KeyCode keyCode)
                    || KeyCodeMapping.TryGetValue(resolvedName, out keyCode)
                )
                {
                    return Input.GetKeyDown(keyCode)
                        && (
                            !cached.ShiftRequired
                            || Input.GetKey(KeyCode.LeftShift)
                            || Input.GetKey(KeyCode.RightShift)
                        )
                        && (
                            !cached.CtrlRequired
                            || Input.GetKey(KeyCode.LeftControl)
                            || Input.GetKey(KeyCode.RightControl)
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
                string lookupName = resolvedName;
                bool shiftRequired = cached.ShiftRequired;
                if (
                    !shiftRequired
                    && (
                        AlternativeSpecialShiftedKeyCodeMap.TryGetValue(
                            lookupName,
                            out string shiftedKeyName
                        ) || SpecialShiftedKeyCodeMap.TryGetValue(lookupName, out shiftedKeyName)
                    )
                )
                {
                    shiftRequired = true;
                    lookupName = shiftedKeyName;
                }

                Keyboard currentKeyboard = Keyboard.current;
                return (!shiftRequired || currentKeyboard.shiftKey.isPressed)
                    && (!cached.CtrlRequired || currentKeyboard.ctrlKey.isPressed)
                    && (
                        currentKeyboard.TryGetChildControl<KeyControl>(
                            SpecialKeyCodeMap.GetValueOrDefault(lookupName, lookupName)
                        )
                            is { wasPressedThisFrame: true }
                        || currentKeyboard.TryGetChildControl<KeyControl>(lookupName)
                            is { wasPressedThisFrame: true }
                    );
#endif
            }
            return false;
        }

        /// <summary>
        ///     Resolves a hotkey string into its key name and modifier
        ///     requirements. Internal for test coverage of the parse surface
        ///     (see WallstopStudios.DxCommandTerminal.Tests.Runtime).
        /// </summary>
        internal static CachedKeyName ResolveKeyName(string key)
        {
            bool ctrlRequired = false;
            bool shiftRequired = false;
            string keyName = key;
            /*
                Modifiers strip in either order ("ctrl+shift+a" and
                "shift+ctrl+a" both work); a bare modifier never strips
                (guarded in StripModifier), so the loop always terminates
                within two iterations.
             */
            for (int i = 0; i < 2; ++i)
            {
                if (!ctrlRequired && StripModifier(keyName, CtrlModifiers, out string withoutCtrl))
                {
                    ctrlRequired = true;
                    keyName = withoutCtrl;
                    continue;
                }

                if (
                    !shiftRequired
                    && StripModifier(keyName, ShiftModifiers, out string withoutShift)
                )
                {
                    shiftRequired = true;
                    keyName = withoutShift;
                    continue;
                }

                break;
            }

            if (!shiftRequired && keyName.Length == 1)
            {
                char keyChar = keyName[0];
                if (char.IsUpper(keyChar) && char.IsLetter(keyChar))
                {
                    shiftRequired = true;
                }
                else if (
                    AlternativeSpecialShiftedKeyCodeMap.TryGetValue(
                        keyName,
                        out string legacyShiftedKeyName
                    )
                )
                {
                    shiftRequired = true;
                    keyName = legacyShiftedKeyName;
                }
            }

            /*
                Modifier stripping and special-key rewriting are deterministic
                per input string; the resolved name and its modifier
                requirements are cached so repeated per-frame polling never
                allocates. Invalid keys keep an empty name so they also skip
                the parse on later calls.
             */
            return new CachedKeyName(keyName, shiftRequired, ctrlRequired);
        }

        /// <summary>
        ///     Reports whether a hotkey binding presses a key that types text -
        ///     any character key, with or without Shift, and with no Ctrl
        ///     modifier. The console surfaces hand those keys to a focused text
        ///     field instead of firing their action, so every character stays
        ///     typeable in a command line or a palette query. Only Ctrl chords
        ///     and the named keys that type nothing (navigation, editing,
        ///     function, modifier) keep firing while a field has focus.
        /// </summary>
        internal static bool ProducesTypedText(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (!CachedKeys.TryGetValue(key, out CachedKeyName cached))
            {
                cached = ResolveKeyName(key);
                CachedKeys[key] = cached;
            }

            /*
                A Ctrl chord inserts no character, so it stays a live hotkey
                while a field has focus. Every other binding resolves either to
                a character key or to a name that types nothing, and the
                character key belongs to the field.
             */
            return !cached.CtrlRequired && !TypesNothing(cached.Name);
        }

#if ENABLE_INPUT_SYSTEM
        /// <summary>
        ///     Reports whether the Input System control behind a PlayerInput
        ///     message types text. A message carries no key, but the action's
        ///     active control is the control that performed it, so a keyboard
        ///     character key is classified by the same rule as a polled
        ///     binding, and anything that is not a keyboard key - a gamepad,
        ///     mouse, or composite part - types nothing.
        /// </summary>
        /// <remarks>
        ///     A <c>ctrl+</c> chord inserts no character, so it stays a live
        ///     hotkey, matching <see cref="ProducesTypedText"/>: the control
        ///     names the key, and the live keyboard state is where the
        ///     modifier lives. <c>anyKey</c> is held back for the same reason
        ///     the polled path holds it - it presses on every keystroke.
        /// </remarks>
        internal static bool ControlProducesTypedText(InputControl control)
        {
            if (control is AnyKeyControl)
            {
                return true;
            }

            if (control is not KeyControl keyControl)
            {
                return false;
            }

            Keyboard currentKeyboard = Keyboard.current;
            if (currentKeyboard != null && currentKeyboard.ctrlKey.isPressed)
            {
                return false;
            }

            return ProducesTypedText(keyControl.name);
        }
#endif

        private static bool TypesNothing(string keyName)
        {
            if (keyName is { Length: 1 })
            {
                return false;
            }

            return NonTypedKeyNames.Contains(keyName);
        }

        /*
            The names that type no character, from both reachable surfaces: the
            Input System's keyboard control names and every multi-character key
            in KeyCodeMapping (navigation, editing, function, modifier, lock,
            media, IME, mouse, and joystick names). Everything else the parse
            surface can produce is a character key, so an unrecognized name
            counts as typing - a hotkey held back for one typing session is
            recoverable, a swallowed character is not. A key either input
            system adds later therefore fails toward the character.
         */
        private static HashSet<string> BuildNonTypedKeyNames()
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase)
            {
                "alt",
                "backspace",
                "break",
                "capslock",
                "clear",
                "cmd",
                "command",
                "contextmenu",
                "control",
                "ctrl",
                "del",
                "delete",
                "down",
                "downarrow",
                "end",
                "enter",
                "esc",
                "escape",
                "help",
                "home",
                "imeselected",
                "ins",
                "insert",
                "keypadenter",
                "lalt",
                "lcmd",
                "lcommand",
                "lcontrol",
                "lctrl",
                "left",
                "leftalt",
                "leftarrow",
                "leftcmd",
                "leftcommand",
                "leftcontrol",
                "leftctrl",
                "leftmeta",
                "leftmouse",
                "leftshift",
                "leftsuper",
                "leftwin",
                "leftwindows",
                "lmb",
                "lshift",
                "lwin",
                "mediaforward",
                "mediaplaypause",
                "mediarewind",
                "meta",
                "middlemouse",
                "mmb",
                "mouse0",
                "mouse1",
                "mouse2",
                "mouse3",
                "mouse4",
                "mouse5",
                "mouse6",
                "none",
                "noscroll",
                "numlock",
                "numpadenter",
                "pagedn",
                "pagedown",
                "pageup",
                "pause",
                "pausebreak",
                "pgdn",
                "pgup",
                "print",
                "printscreen",
                "prtscn",
                "ralt",
                "rcmd",
                "rcommand",
                "rcontrol",
                "rctrl",
                "return",
                "right",
                "rightarrow",
                "rightalt",
                "rightcmd",
                "rightcommand",
                "rightcontrol",
                "rightctrl",
                "rightmeta",
                "rightmouse",
                "rightshift",
                "rightsuper",
                "rightwin",
                "rightwindows",
                "rmb",
                "rshift",
                "rwin",
                "scrolllock",
                "select",
                "shift",
                "super",
                "sysreq",
                "tab",
                "up",
                "uparrow",
            };

            for (int functionKey = 1; functionKey <= 24; ++functionKey)
            {
                names.Add($"f{functionKey}");
            }

            for (int joystickButton = 0; joystickButton <= 19; ++joystickButton)
            {
                names.Add($"joystickbutton{joystickButton}");
            }

            return names;
        }

        private static bool StripModifier(string key, string[] modifiers, out string stripped)
        {
            foreach (string modifier in modifiers)
            {
                if (
                    key.StartsWith(modifier, StringComparison.OrdinalIgnoreCase)
                    && key.Length != modifier.Length
                )
                {
                    stripped = key[modifier.Length..];
                    if (stripped.NeedsTrim())
                    {
                        stripped = stripped.Trim();
                    }

                    if (stripped.Length == 1 && stripped.NeedsLowerInvariantConversion())
                    {
                        stripped = stripped.ToLowerInvariant();
                    }

                    return true;
                }
            }

            stripped = key;
            return false;
        }

        internal readonly struct CachedKeyName
        {
            public readonly string Name;
            public readonly bool ShiftRequired;
            public readonly bool CtrlRequired;

            public CachedKeyName(string name, bool shiftRequired, bool ctrlRequired)
            {
                Name = name;
                ShiftRequired = shiftRequired;
                CtrlRequired = ctrlRequired;
            }
        }
    }
}
