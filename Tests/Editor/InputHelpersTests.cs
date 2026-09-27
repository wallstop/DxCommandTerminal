namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Input;
    using NUnit.Framework;

    /*
        Pins which hotkey bindings type text, because the console surfaces
        hand those keys to a focused text field instead of firing their
        action. The rule is an allowlist of the names that type nothing, so
        these rows cover both directions: a character binding that must be
        handed to the field, and a named key that must stay live. Both
        shipped default bindings are here, so a default that stops typing
        fails here.
     */
    public sealed class InputHelpersTests
    {
        [TestCase("`", true)]
        [TestCase("#`", true)]
        [TestCase("~", true)]
        [TestCase("k", true)]
        [TestCase("K", true)]
        [TestCase("7", true)]
        [TestCase("!", true)]
        [TestCase(" ", true)]
        [TestCase("space", true)]
        [TestCase("slash", true)]
        [TestCase("backquote", true)]
        [TestCase("comma", true)]
        [TestCase("period", true)]
        [TestCase("quote", true)]
        [TestCase("semicolon", true)]
        [TestCase("backslash", true)]
        [TestCase("minus", true)]
        [TestCase("equals", true)]
        [TestCase("leftbracket", true)]
        [TestCase("rightbracket", true)]
        [TestCase("spacebar", true)]
        [TestCase("alpha1", true)]
        [TestCase("numpad1", true)]
        [TestCase("か", true)]
        [TestCase("shift+slash", true)]
        [TestCase("ctrl+space", false)]
        [TestCase("ctrl+`", false)]
        [TestCase("shift+tab", false)]
        [TestCase("#tab", false)]
        [TestCase("tab", false)]
        [TestCase("enter", false)]
        [TestCase("return", false)]
        [TestCase("keypadenter", false)]
        [TestCase("escape", false)]
        [TestCase("esc", false)]
        [TestCase("backspace", false)]
        [TestCase("delete", false)]
        [TestCase("home", false)]
        [TestCase("end", false)]
        [TestCase("up", false)]
        [TestCase("down", false)]
        [TestCase("left", false)]
        [TestCase("right", false)]
        [TestCase("uparrow", false)]
        [TestCase("leftarrow", false)]
        [TestCase("pagedown", false)]
        [TestCase("pageup", false)]
        [TestCase("f1", false)]
        [TestCase("f12", false)]
        [TestCase("F24", false)]
        [TestCase("anykey", false)]
        [TestCase("capslock", false)]
        [TestCase("numlock", false)]
        [TestCase("printscreen", false)]
        [TestCase("pause", false)]
        [TestCase("scrolllock", false)]
        [TestCase("contextmenu", false)]
        [TestCase("mediaplaypause", false)]
        [TestCase("mediaforward", false)]
        [TestCase("leftshift", false)]
        [TestCase("shift", false)]
        [TestCase("numpaddivide", true)]
        [TestCase("oem1", true)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void ProducesTypedTextMatchesTheBindingsThatTypeCharacters(string key, bool expected)
        {
            Assert.That(
                InputHelpers.ProducesTypedText(key),
                Is.EqualTo(expected),
                $"ProducesTypedText(\"{key}\") must be {expected}"
            );
        }
    }
}
