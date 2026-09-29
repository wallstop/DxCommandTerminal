namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using NUnit.Framework;
    using UI;
    using UnityEngine;

    /*
        Which key moves the log, and how far.

        The command line owns panel focus for as long as the console is open,
        so every one of these keys arrives at the command field rather than at
        the log view. This is the decision the terminal makes about a key that
        reached it; applying it needs a live panel and lives in
        TerminalUILogScrollTests.

        The tables drive keys rather than scroll intents, because a key is what
        a developer presses and an intent is this package's own vocabulary.
        Asserting the effect - where the view ends up - also keeps the two
        halves of the decision in one test, so a key that scrolls the wrong way
        cannot pass by naming the right intent.

        Data rather than one test per key because the failures are not
        independent. Taking Home away from a text field, paging a view that
        cannot scroll, and letting through a key the field wanted are all the
        same class of mistake - the log answering a key that was not its to
        answer - so one table states the whole rule.
     */
    public sealed class LogScrollKeysTests
    {
        private static float ScrollTo(
            KeyCode keyCode,
            bool commandKey,
            float value,
            float highValue,
            float page
        )
        {
            Assert.That(
                LogScrollKeys.TryResolve(keyCode, commandKey, out LogScrollIntent intent),
                Is.True,
                $"{keyCode} is the log's key to answer here"
            );
            return LogScrollKeys.Target(intent, value, highValue, page);
        }

        [TestCase(KeyCode.PageUp)]
        [TestCase(KeyCode.PageDown)]
        [TestCase(KeyCode.Home)]
        [TestCase(KeyCode.End)]
        [TestCase(KeyCode.LeftArrow)]
        [TestCase(KeyCode.RightArrow)]
        [TestCase(KeyCode.UpArrow)]
        [TestCase(KeyCode.DownArrow)]
        [TestCase(KeyCode.A)]
        [TestCase(KeyCode.Escape)]
        [TestCase(KeyCode.Return)]
        [TestCase(KeyCode.Tab)]
        [TestCase(KeyCode.Backspace)]
        public void APlainKeyReachesTheLogOnlyWhenTheLogWantsIt(KeyCode keyCode)
        {
            bool answered = LogScrollKeys.TryResolve(keyCode, commandKey: false, out _);

            Assert.That(
                answered,
                Is.EqualTo(keyCode is KeyCode.PageUp or KeyCode.PageDown),
                "Paging is the log's alone; every other key stays with the command line"
            );
        }

        [TestCase(KeyCode.PageUp)]
        [TestCase(KeyCode.PageDown)]
        [TestCase(KeyCode.Home)]
        [TestCase(KeyCode.End)]
        public void ACommandKeyReachesTheLogEvenWhereTheCaretWould(KeyCode keyCode)
        {
            Assert.That(
                LogScrollKeys.TryResolve(keyCode, commandKey: true, out _),
                Is.True,
                "The command modifier is what makes Home and End the log's to answer "
                    + "rather than the caret's; without it they stay with the field"
            );
        }

        [TestCase(KeyCode.PageUp, 500f, 1000f, 400f, 100f)]
        [TestCase(KeyCode.PageDown, 100f, 1000f, 400f, 500f)]
        [TestCase(KeyCode.Home, 500f, 1000f, 400f, 0f)]
        [TestCase(KeyCode.End, 100f, 1000f, 400f, 1000f)]
        [TestCase(KeyCode.PageUp, 50f, 1000f, 400f, 0f)]
        [TestCase(KeyCode.PageDown, 950f, 1000f, 400f, 1000f)]
        [TestCase(KeyCode.PageUp, 0f, 1000f, 0f, 0f)]
        public void AKeyMovesTheLogByAPageAndClampsToItsScrollableRange(
            KeyCode keyCode,
            float value,
            float highValue,
            float page,
            float expected
        )
        {
            Assert.That(
                ScrollTo(keyCode, commandKey: true, value, highValue, page),
                Is.EqualTo(expected).Within(0.001f),
                "The log moves by a page and never past either end of its scroll"
            );
        }

        [TestCase(KeyCode.PageUp)]
        [TestCase(KeyCode.PageDown)]
        public void ALogThatCannotScrollStaysPut(KeyCode keyCode)
        {
            Assert.That(
                ScrollTo(keyCode, commandKey: true, 300f, 300f, 0f),
                Is.EqualTo(300f),
                "A view with no overflow has nothing to page through, and a page of zero would not move"
            );
        }
    }
}
