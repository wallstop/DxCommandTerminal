#pragma warning disable CS0618 // Type or member is obsolete
namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UI;
    using UnityEngine;

    public sealed class TerminalRuntimeConfiguratorTests
    {
        private ITerminalRuntimeConfigurator _previousConfigurator;

        [SetUp]
        public void CaptureConfigurator()
        {
            _previousConfigurator = TerminalUI.RuntimeConfigurator;
        }

        [TearDown]
        public void RestoreConfigurator()
        {
            TerminalUI.RuntimeConfigurator =
                _previousConfigurator ?? TerminalRuntimeConfiguratorProxy.Default;
        }

        [Test]
        public void AwakeInvokesRuntimeConfigurator()
        {
            StubConfigurator configurator = new StubConfigurator();
            TerminalUI.RuntimeConfigurator = configurator;

            GameObject go = new GameObject("RuntimeConfiguratorTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                Assert.That(configurator.SetModeCalled, Is.True);
                Assert.That(configurator.Mode, Is.Not.EqualTo(TerminalRuntimeModeFlags.None));
                Assert.That(configurator.EditorAutoDiscoverSet, Is.True);
                Assert.That(configurator.TryAutoDiscoverCalled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private sealed class StubConfigurator : ITerminalRuntimeConfigurator
        {
            internal bool SetModeCalled { get; private set; }

            internal TerminalRuntimeModeFlags Mode { get; private set; }

            internal bool EditorAutoDiscoverSet { get; private set; }

            internal bool TryAutoDiscoverCalled { get; private set; }

            private bool _editorAutoDiscover;

            public void SetMode(TerminalRuntimeModeFlags modes)
            {
                SetModeCalled = true;
                Mode = modes;
            }

            public bool EditorAutoDiscover
            {
                get => _editorAutoDiscover;
                set
                {
                    _editorAutoDiscover = value;
                    EditorAutoDiscoverSet = true;
                }
            }

            public int TryAutoDiscoverParsers()
            {
                TryAutoDiscoverCalled = true;
                return 0;
            }
        }
    }
}
