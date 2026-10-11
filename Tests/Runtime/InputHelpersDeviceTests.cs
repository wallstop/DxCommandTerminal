namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using System.Collections.Generic;
    using Input;
    using NUnit.Framework;
    using UnityEngine.InputSystem;
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
