namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using Input;
    using NUnit.Framework;
    using UnityEngine.InputSystem;
    using UnityEngine.InputSystem.LowLevel;
    using UnityEngine.TestTools;

    /*
        Pins the polled hotkeys against a host with no keyboard: a
        gamepad-only setup, a console, or the frame an unplug lands used to
        answer every poll with a NullReferenceException from the unguarded
        Keyboard.current dereference, once per frame per poller (#221).

        The device work runs against the real Input System, so the removal
        is the real one - the same InputSystem.RemoveDevice the issue names.
     */
    public sealed class InputHelpersDeviceTests
    {
        /*
            Readiness-poll headroom, not a fixed expectation: an unfocused or
            agent-driven editor can take far more than a focused one needs to
            land a queued state event. The poll exits the frame it succeeds,
            so the headroom never slows a green run (the CommandPaletteTests
            budget).
         */
        private const int FrameBudget = 600;

        /*
            The controller's default bindings, plus the palette's: every
            hotkey the package polls by default, so one no-keyboard frame
            covers them all.
         */
        private static readonly string[] DefaultHotkeys =
        {
            "`",
            "#`",
            "tab",
            "#tab",
            "up",
            "down",
            "enter",
            "return",
            "escape",
        };

        private static void PollAllDefaultHotkeys()
        {
            foreach (string hotkey in DefaultHotkeys)
            {
                InputHelpers.IsKeyPressed(hotkey, InputMode.NewInputSystem);
            }
        }

        private static IEnumerator Settle()
        {
            /*
                A queued state needs frames to reach the poll; nothing here
                reads a one-frame press, so a fixed budget is the wait.
             */
            for (int frame = 0; frame < 5; ++frame)
            {
                yield return null;
            }
        }

        private static IEnumerator PollPress(string hotkey, string message)
        {
            bool pressed = false;
            for (int frame = 0; frame < FrameBudget && !pressed; ++frame)
            {
                pressed = InputHelpers.IsKeyPressed(hotkey, InputMode.NewInputSystem);
                yield return null;
            }

            Assert.IsTrue(pressed, message);
        }

        [SetUp]
        public void SetUp()
        {
            InputHelpers.ResetControlMemoForTesting();
        }

        [TearDown]
        public void TearDown()
        {
            /*
                A test that failed mid-removal leaves no keyboard for the
                suites after this one; the reset is idempotent when the
                keyboard is already back.
             */
            if (Keyboard.current == null)
            {
                InputSystem.AddDevice<Keyboard>();
            }
        }

        /*
            The chord contract, against a live press: a shifted or ctrl'd
            binding fires only with its modifier held, on the frame the key
            itself presses. These are the default toggle and completion
            bindings - `#backquote` shares a key with the console toggle, and
            `ctrl+x` shares a key with typing.
         */
        [UnityTest]
        public IEnumerator ModifierChordsRequireTheModifierHeld()
        {
            if (Keyboard.current == null)
            {
                InputSystem.AddDevice<Keyboard>();
            }

            /*
                A queued state replaces the whole keyboard, and a state equal
                to the current one is not a new press, so every press is
                released first (the TerminalPlayerInputControllerTests
                pattern). A press answers true on one frame only, so the
                reads poll every frame until the press lands and are judged
                on the frame they do.
             */
            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.Backquote));

            bool barePressed = false;
            bool shiftChordFired = false;
            for (int frame = 0; frame < FrameBudget && !barePressed; ++frame)
            {
                bool bare = InputHelpers.IsKeyPressed("`", InputMode.NewInputSystem);
                bool shifted = InputHelpers.IsKeyPressed("#`", InputMode.NewInputSystem);
                if (bare)
                {
                    barePressed = true;
                    shiftChordFired = shifted;
                }

                yield return null;
            }

            Assert.IsTrue(barePressed, "Sanity: the bare key presses");
            Assert.IsFalse(shiftChordFired, "shift+backquote must not fire on a bare backquote");

            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
            InputSystem.QueueStateEvent(
                Keyboard.current,
                new KeyboardState(Key.LeftShift, Key.Backquote)
            );
            yield return PollPress("#`", "shift+backquote fires with shift held");

            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.X));

            bool bareXPressed = false;
            bool ctrlChordFired = false;
            for (int frame = 0; frame < FrameBudget && !bareXPressed; ++frame)
            {
                bool bare = InputHelpers.IsKeyPressed("x", InputMode.NewInputSystem);
                bool ctrl = InputHelpers.IsKeyPressed("ctrl+x", InputMode.NewInputSystem);
                if (bare)
                {
                    bareXPressed = true;
                    ctrlChordFired = ctrl;
                }

                yield return null;
            }

            Assert.IsTrue(bareXPressed, "Sanity: the bare x presses");
            Assert.IsFalse(ctrlChordFired, "ctrl+x must not fire on a bare x");

            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftCtrl, Key.X));
            yield return PollPress("ctrl+x", "ctrl+x fires with ctrl held");

            InputSystem.QueueStateEvent(Keyboard.current, default(KeyboardState));
            yield return Settle();
        }

        [UnityTest]
        public IEnumerator RemovedKeyboardPollsDefaultHotkeysWithoutThrowing()
        {
            Keyboard keyboard = Keyboard.current;
            Assert.That(keyboard != null, "Sanity: this host has no keyboard device to remove");

            InputSystem.RemoveDevice(keyboard);
            try
            {
                Assert.That(
                    Keyboard.current == null,
                    "Sanity: the removal left no current keyboard"
                );
                foreach (string hotkey in DefaultHotkeys)
                {
                    bool pressed = InputHelpers.IsKeyPressed(hotkey, InputMode.NewInputSystem);
                    Assert.IsFalse(pressed, $"No keyboard means no press, for '{hotkey}' too");
                }
            }
            finally
            {
                InputSystem.AddDevice<Keyboard>();
            }

            Assert.That(Keyboard.current != null, "The re-added keyboard answers polls again");
            Assert.IsFalse(
                InputHelpers.IsKeyPressed("`", InputMode.NewInputSystem),
                "A re-added keyboard starts with no press held"
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnchangedHotkeyPassResolvesNoNewControls()
        {
            if (Keyboard.current == null)
            {
                InputSystem.AddDevice<Keyboard>();
            }

            PollAllDefaultHotkeys();
            int resolutionsAfterFirstPass = InputHelpers.ControlResolutionsForTesting;
            Assert.That(0 < resolutionsAfterFirstPass, "Sanity: the first pass resolved controls");

            PollAllDefaultHotkeys();

            Assert.AreEqual(
                resolutionsAfterFirstPass,
                InputHelpers.ControlResolutionsForTesting,
                "A pass over unchanged hotkeys must resolve no new controls"
            );

            /*
                A keyboard change re-resolves: the memo's controls belong to
                one device, and a stale control would read a dead device's
                state.
             */
            InputSystem.RemoveDevice(Keyboard.current);
            InputSystem.AddDevice<Keyboard>();
            PollAllDefaultHotkeys();
            Assert.That(
                resolutionsAfterFirstPass < InputHelpers.ControlResolutionsForTesting,
                "A different keyboard re-resolves the controls"
            );

            yield return null;
        }
    }
}
