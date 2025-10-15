namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using Backend;
    using Components;
    using Input;
    using NUnit.Framework;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class TerminalKeyboardControllerTests
    {
        private sealed class FakeTerminalTarget : MonoBehaviour, ITerminalInputTarget
        {
            public bool Closed { get; private set; }
            public int EnterCommandCount { get; private set; }
            public int CompleteForwardCount { get; private set; }
            public int CompleteBackwardCount { get; private set; }
            public int PreviousCount { get; private set; }
            public int NextCount { get; private set; }
            public int ToggleSmallCount { get; private set; }
            public int ToggleFullCount { get; private set; }
            public int ToggleLauncherCount { get; private set; }

            public bool IsClosed => Closed;

            public void Close()
            {
                Closed = true;
            }

            public void ToggleSmall()
            {
                ++ToggleSmallCount;
            }

            public void ToggleFull()
            {
                ++ToggleFullCount;
            }

            public void ToggleLauncher()
            {
                ++ToggleLauncherCount;
            }

            public void EnterCommand()
            {
                ++EnterCommandCount;
            }

            public void CompleteCommand(bool searchForward)
            {
                if (searchForward)
                {
                    ++CompleteForwardCount;
                }
                else
                {
                    ++CompleteBackwardCount;
                }
            }

            public void HandlePrevious()
            {
                ++PreviousCount;
            }

            public void HandleNext()
            {
                ++NextCount;
            }
        }

        private sealed class TestKeyboardController : TerminalKeyboardController
        {
            public void InvokeControl(TerminalControlTypes control)
            {
                ExecuteControlForTests(control);
            }
        }

        [UnityTest]
        public IEnumerator ControllerUsesInputTargetInterface()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            GameObject go = new("InputController");
            go.SetActive(false);

            FakeTerminalTarget target = go.AddComponent<FakeTerminalTarget>();
            TestKeyboardController controller = go.AddComponent<TestKeyboardController>();
            controller.terminal = null;

            go.SetActive(true);
            yield return null;

            controller.InvokeControl(TerminalControlTypes.EnterCommand);
            controller.InvokeControl(TerminalControlTypes.CompleteForward);
            controller.InvokeControl(TerminalControlTypes.CompleteBackward);
            controller.InvokeControl(TerminalControlTypes.Previous);
            controller.InvokeControl(TerminalControlTypes.Next);
            controller.InvokeControl(TerminalControlTypes.ToggleFull);
            controller.InvokeControl(TerminalControlTypes.ToggleSmall);
            controller.InvokeControl(TerminalControlTypes.ToggleLauncher);
            controller.InvokeControl(TerminalControlTypes.Close);

            Assert.AreEqual(1, target.EnterCommandCount);
            Assert.AreEqual(1, target.CompleteForwardCount);
            Assert.AreEqual(1, target.CompleteBackwardCount);
            Assert.AreEqual(1, target.PreviousCount);
            Assert.AreEqual(1, target.NextCount);
            Assert.AreEqual(1, target.ToggleFullCount);
            Assert.AreEqual(1, target.ToggleSmallCount);
            Assert.AreEqual(1, target.ToggleLauncherCount);
            Assert.IsTrue(target.IsClosed);
        }

        [UnityTest]
        public IEnumerator ControllerFallsBackToTerminalUIWhenTargetMissing()
        {
            yield return TestSceneHelpers.DestroyTerminalAndWait();

            yield return TerminalTests.SpawnTerminal(
                resetStateOnInit: true,
                configure: null,
                ensureLargeLogBuffer: true
            );

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            TerminalKeyboardController controller = terminal.gameObject.AddComponent<TerminalKeyboardController>();
            controller.terminal = terminal;

            yield return null;

            controller.ExecuteControlForTests(TerminalControlTypes.ToggleFull);
            controller.ExecuteControlForTests(TerminalControlTypes.Close);

            Assert.IsTrue(terminal.IsClosed);
        }
    }
}
