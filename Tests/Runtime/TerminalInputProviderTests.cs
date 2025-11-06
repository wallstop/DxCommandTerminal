namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Input;
    using NUnit.Framework;
    using UI;
    using UnityEngine;

    public sealed class TerminalInputProviderTests
    {
        private ITerminalInputProvider _previousProvider;

        [SetUp]
        public void Setup()
        {
            _previousProvider = TerminalUI.InputProvider;
        }

        [TearDown]
        public void Teardown()
        {
            TerminalUI.InputProvider = _previousProvider ?? TerminalInputProviderProxy.Default;
        }

        [Test]
        public void UsesCustomInputProviderWhenComponentMissing()
        {
            StubInputProvider provider = new();
            TerminalUI.InputProvider = provider;

            GameObject go = new("TerminalInputProviderTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                Assert.That(provider.RequestCount, Is.EqualTo(1));
                Assert.That(provider.LatestTerminal, Is.SameAs(terminal));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private sealed class StubInputProvider : ITerminalInputProvider
        {
            internal int RequestCount { get; private set; }

            internal TerminalUI LatestTerminal { get; private set; }

            public ITerminalInput GetInput(TerminalUI terminal)
            {
                RequestCount++;
                LatestTerminal = terminal;
                return DefaultTerminalInput.Instance;
            }
        }
    }
}
