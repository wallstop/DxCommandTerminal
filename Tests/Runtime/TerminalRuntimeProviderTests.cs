namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;

    public sealed class TerminalRuntimeProviderTests
    {
        private ITerminalRuntimeConfigurator _previousConfigurator;
        private ITerminalRuntimeProvider _previousProvider;

        [SetUp]
        public void Setup()
        {
            _previousConfigurator = TerminalUI.RuntimeConfigurator;
            _previousProvider = TerminalUI.RuntimeProvider;
        }

        [TearDown]
        public void TearDown()
        {
            TerminalUI.RuntimeConfigurator =
                _previousConfigurator ?? TerminalRuntimeConfiguratorProxy.Default;
            TerminalUI.RuntimeProvider = _previousProvider ?? TerminalRuntimeProviderProxy.Default;
        }

        [Test]
        public void AwakeUsesInjectedRuntimeConfiguratorAndProvider()
        {
            StubConfigurator configurator = new StubConfigurator();
            StubRuntimeProvider provider = new StubRuntimeProvider();
            TerminalUI.RuntimeConfigurator = configurator;
            TerminalUI.RuntimeProvider = provider;

            GameObject go = new GameObject("TerminalRuntimeProviderTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                Assert.That(configurator.SetModeCalled, Is.True);
                Assert.That(provider.RequestedCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private sealed class StubConfigurator : ITerminalRuntimeConfigurator
        {
            internal bool SetModeCalled { get; private set; }

            private bool _editorAutoDiscover;

            public void SetMode(TerminalRuntimeModeFlags modes)
            {
                SetModeCalled = true;
            }

            public bool EditorAutoDiscover
            {
                get => _editorAutoDiscover;
                set => _editorAutoDiscover = value;
            }

            public int TryAutoDiscoverParsers()
            {
                return 0;
            }
        }

        private sealed class StubRuntimeProvider : ITerminalRuntimeProvider
        {
            internal int RequestedCount { get; private set; }

            public ITerminalRuntime ActiveRuntime
            {
                get
                {
                    RequestedCount++;
                    return null;
                }
            }
        }
    }
}
