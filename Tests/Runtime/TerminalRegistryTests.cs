namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using NUnit.Framework;
    using UI;
    using UnityEngine;

    public sealed class TerminalRegistryTests
    {
        [SetUp]
        public void ResetRegistry()
        {
            if (TerminalUI.Instance != null)
            {
                Object.DestroyImmediate(TerminalUI.Instance.gameObject);
            }

            TerminalUI.TerminalProvider = new TerminalRegistry();
        }

        [TearDown]
        public void CleanUp()
        {
            if (TerminalUI.TerminalProvider is TerminalRegistry)
            {
                TerminalUI.TerminalProvider = TerminalRegistry.Default;
            }
        }

        [Test]
        public void RegisterAndUnregisterUpdatesActiveTerminal()
        {
            GameObject first = new GameObject("TerminalRegistry_First");
            GameObject second = new GameObject("TerminalRegistry_Second");
            try
            {
                TerminalUI firstTerminal = first.AddComponent<TerminalUI>();
                firstTerminal.disableUIForTests = true;
                first.SetActive(true);

                Assert.That(TerminalUI.TerminalProvider.ActiveTerminal, Is.SameAs(firstTerminal));

                TerminalUI secondTerminal = second.AddComponent<TerminalUI>();
                secondTerminal.disableUIForTests = true;
                second.SetActive(true);

                Assert.That(TerminalUI.TerminalProvider.ActiveTerminal, Is.SameAs(secondTerminal));

                Object.DestroyImmediate(second);
                Assert.That(TerminalUI.TerminalProvider.ActiveTerminal, Is.SameAs(firstTerminal));

                Object.DestroyImmediate(first);
                Assert.That(TerminalUI.TerminalProvider.ActiveTerminal, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(first);
            }
        }
    }
}
