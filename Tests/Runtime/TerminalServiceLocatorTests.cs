namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections.Generic;
    using Backend;
    using Input;
    using NUnit.Framework;
    using Service;
    using UI;
    using UnityEngine;

    public sealed class TerminalServiceLocatorTests
    {
        private ITerminalServiceLocator _previousLocator;

        [SetUp]
        public void CaptureLocator()
        {
            _previousLocator = TerminalUI.ServiceLocator;
        }

        [TearDown]
        public void RestoreLocator()
        {
            TerminalUI.ServiceLocator = _previousLocator ?? TerminalServiceLocator.Default;
        }

        [Test]
        public void ServiceLocatorOverridesStaticAccessors()
        {
            StubTerminalProvider terminalProvider = new();
            StubRuntimeConfigurator runtimeConfigurator = new();
            StubInputProvider inputProvider = new();
            StubRuntimeProvider runtimeProvider = new();
            StubRuntimeScope runtimeScope = new();
            StubRuntimeConfiguratorService runtimeConfiguratorService = new();
            StubRuntimePool runtimePool = new();

            ITerminalServiceLocator locator = new StubServiceLocator(
                terminalProvider,
                runtimeConfigurator,
                inputProvider,
                runtimeProvider,
                runtimeScope,
                runtimeConfiguratorService,
                runtimePool
            );

            TerminalUI.ServiceLocator = locator;

            Assert.That(TerminalUI.TerminalProvider, Is.SameAs(terminalProvider));
            Assert.That(TerminalUI.RuntimeConfigurator, Is.SameAs(runtimeConfigurator));
            Assert.That(TerminalUI.InputProvider, Is.SameAs(inputProvider));
            Assert.That(TerminalUI.RuntimeProvider, Is.SameAs(runtimeProvider));
            Assert.That(TerminalUI.ServiceLocator.RuntimeScope, Is.SameAs(runtimeScope));
            Assert.That(
                TerminalUI.ServiceLocator.RuntimeConfiguratorService,
                Is.SameAs(runtimeConfiguratorService)
            );
        }

        [Test]
        public void SettingTerminalProviderCreatesMutableLocator()
        {
            TerminalUI.ServiceLocator = TerminalServiceLocator.Default;
            ITerminalProvider replacementProvider = new TerminalRegistry();

            TerminalUI.TerminalProvider = replacementProvider;

            Assert.That(TerminalUI.ServiceLocator, Is.TypeOf<MutableTerminalServiceLocator>());
            Assert.That(TerminalUI.TerminalProvider, Is.SameAs(replacementProvider));
        }

        [Test]
        public void RuntimeScopeReceivesRegistrations()
        {
            StubRuntimeScope runtimeScope = new();
            StubRuntimePool runtimePool = new();
            MutableTerminalServiceLocator locator = new(
                new TerminalRegistry(),
                new StubRuntimeConfigurator(),
                new StubInputProvider(),
                new StubRuntimeProvider(),
                runtimeScope,
                new StubRuntimeConfiguratorService(),
                runtimePool
            );

            TerminalUI.ServiceLocator = locator;

            GameObject go = new("TerminalServiceLocator_RuntimeScope");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                Assert.That(runtimeScope.RegisteredRuntime, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            Assert.That(
                runtimeScope.UnregisteredRuntime,
                Is.SameAs(runtimeScope.RegisteredRuntime)
            );
        }

        [Test]
        public void ServiceBindingAssetOverridesLocator()
        {
            ITerminalServiceLocator originalLocator = TerminalUI.ServiceLocator;
            TerminalUI.ServiceLocator = TerminalServiceLocator.Default;

            TerminalServiceBindingAsset bindingAsset =
                ScriptableObject.CreateInstance<TerminalServiceBindingAsset>();
            ScriptableTerminalProvider provider =
                ScriptableObject.CreateInstance<ScriptableTerminalProvider>();

            bindingAsset.SetTerminalProviderForTests(provider);

            GameObject go = new("TerminalServiceBindingAssetTest");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            terminal.SetServiceBindingForTests(bindingAsset);
            go.SetActive(true);

            try
            {
                Assert.That(TerminalUI.ServiceLocator, Is.SameAs(bindingAsset));
                Assert.That(TerminalUI.TerminalProvider, Is.SameAs(provider));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(bindingAsset);
                Object.DestroyImmediate(provider);
                TerminalUI.ServiceLocator = originalLocator ?? TerminalServiceLocator.Default;
            }
        }

        [Test]
        public void ServiceBindingComponentAppliesAndRestoresLocator()
        {
            ITerminalServiceLocator originalLocator = TerminalUI.ServiceLocator;
            TerminalUI.ServiceLocator = TerminalServiceLocator.Default;

            TerminalServiceBindingAsset bindingAsset =
                ScriptableObject.CreateInstance<TerminalServiceBindingAsset>();
            ScriptableTerminalProvider provider =
                ScriptableObject.CreateInstance<ScriptableTerminalProvider>();
            bindingAsset.SetTerminalProviderForTests(provider);

            GameObject componentGo = new("TerminalServiceLocator_Component");
            componentGo.SetActive(false);
            TerminalServiceBindingComponent component =
                componentGo.AddComponent<TerminalServiceBindingComponent>();
            component.SetBindingAssetForTests(bindingAsset);
            componentGo.SetActive(true);

            GameObject terminalGo = new("TerminalServiceLocator_Component_Terminal");
            terminalGo.SetActive(false);
            TerminalUI terminal = terminalGo.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            terminal.SetServiceBindingComponentForTests(component);
            terminalGo.SetActive(true);

            Assert.That(TerminalUI.ServiceLocator, Is.SameAs(bindingAsset));

            Object.DestroyImmediate(terminalGo);
            Object.DestroyImmediate(componentGo);
            Assert.That(
                TerminalUI.ServiceLocator,
                Is.SameAs(originalLocator ?? TerminalServiceLocator.Default)
            );

            Object.DestroyImmediate(bindingAsset);
            Object.DestroyImmediate(provider);
            TerminalUI.ServiceLocator = originalLocator ?? TerminalServiceLocator.Default;
        }

        [Test]
        public void ServiceBindingSettingsProvideDefaultAsset()
        {
            ITerminalServiceLocator originalLocator = TerminalUI.ServiceLocator;
            TerminalUI.ServiceLocator = TerminalServiceLocator.Default;

            TerminalServiceBindingAsset bindingAsset =
                ScriptableObject.CreateInstance<TerminalServiceBindingAsset>();
            ScriptableTerminalProvider provider =
                ScriptableObject.CreateInstance<ScriptableTerminalProvider>();
            bindingAsset.SetTerminalProviderForTests(provider);

            TerminalServiceBindingSettings.SetDefaultBindingForTests(bindingAsset);

            GameObject go = new("TerminalServiceLocator_Settings");
            go.SetActive(false);
            TerminalUI terminal = go.AddComponent<TerminalUI>();
            terminal.disableUIForTests = true;
            go.SetActive(true);

            try
            {
                Assert.That(TerminalUI.ServiceLocator, Is.SameAs(bindingAsset));
                Assert.That(TerminalUI.TerminalProvider, Is.SameAs(provider));
            }
            finally
            {
                Object.DestroyImmediate(go);
                TerminalUI.ServiceLocator = originalLocator ?? TerminalServiceLocator.Default;
                TerminalServiceBindingSettings.SetDefaultBindingForTests(null);
                Object.DestroyImmediate(bindingAsset);
                Object.DestroyImmediate(provider);
            }
        }

        private sealed class StubServiceLocator : ITerminalServiceLocator
        {
            private readonly ITerminalProvider _terminalProvider;
            private readonly ITerminalRuntimeConfigurator _runtimeConfigurator;
            private readonly ITerminalInputProvider _inputProvider;
            private readonly ITerminalRuntimeProvider _runtimeProvider;
            private readonly ITerminalRuntimeScope _runtimeScope;
            private readonly ITerminalRuntimeConfiguratorService _runtimeConfiguratorService;

            private readonly ITerminalRuntimePool _runtimePool;

            internal StubServiceLocator(
                ITerminalProvider terminalProvider,
                ITerminalRuntimeConfigurator runtimeConfigurator,
                ITerminalInputProvider inputProvider,
                ITerminalRuntimeProvider runtimeProvider,
                ITerminalRuntimeScope runtimeScope,
                ITerminalRuntimeConfiguratorService runtimeConfiguratorService,
                ITerminalRuntimePool runtimePool
            )
            {
                _terminalProvider = terminalProvider;
                _runtimeConfigurator = runtimeConfigurator;
                _inputProvider = inputProvider;
                _runtimeProvider = runtimeProvider;
                _runtimeScope = runtimeScope;
                _runtimeConfiguratorService = runtimeConfiguratorService;
                _runtimePool = runtimePool;
            }

            public ITerminalProvider TerminalProvider => _terminalProvider;

            public ITerminalRuntimeConfigurator RuntimeConfigurator => _runtimeConfigurator;

            public ITerminalInputProvider InputProvider => _inputProvider;

            public ITerminalRuntimeProvider RuntimeProvider => _runtimeProvider;

            public ITerminalRuntimeScope RuntimeScope => _runtimeScope;

            public ITerminalRuntimeConfiguratorService RuntimeConfiguratorService =>
                _runtimeConfiguratorService;

            public ITerminalRuntimePool RuntimePool => _runtimePool;
        }

        private sealed class StubRuntimePool : ITerminalRuntimePool
        {
            public bool TryRent(out ITerminalRuntime runtime)
            {
                runtime = null;
                return false;
            }

            public void Return(ITerminalRuntime runtime) { }

            public void Clear() { }
        }

        private sealed class StubTerminalProvider : ITerminalProvider
        {
            private readonly List<TerminalUI> _terminals = new();

            public TerminalUI ActiveTerminal
            {
                get
                {
                    int count = _terminals.Count;
                    return count > 0 ? _terminals[count - 1] : null;
                }
            }

            public IReadOnlyList<TerminalUI> ActiveTerminals => _terminals;

            public void Register(TerminalUI terminal)
            {
                if (terminal == null || _terminals.Contains(terminal))
                {
                    return;
                }

                _terminals.Add(terminal);
            }

            public void Unregister(TerminalUI terminal)
            {
                if (terminal == null)
                {
                    return;
                }

                _terminals.Remove(terminal);
            }
        }

        private sealed class ScriptableTerminalProvider : ScriptableObject, ITerminalProvider
        {
            private readonly List<TerminalUI> _terminals = new();

            public TerminalUI ActiveTerminal
            {
                get
                {
                    int count = _terminals.Count;
                    return count > 0 ? _terminals[count - 1] : null;
                }
            }

            public IReadOnlyList<TerminalUI> ActiveTerminals => _terminals;

            public void Register(TerminalUI terminal)
            {
                if (terminal == null || _terminals.Contains(terminal))
                {
                    return;
                }

                _terminals.Add(terminal);
            }

            public void Unregister(TerminalUI terminal)
            {
                if (terminal == null)
                {
                    return;
                }

                _terminals.Remove(terminal);
            }
        }

        private sealed class StubRuntimeConfigurator : ITerminalRuntimeConfigurator
        {
            public TerminalRuntimeModeFlags LastMode { get; private set; }

            public bool EditorAutoDiscover { get; set; }

            public void SetMode(TerminalRuntimeModeFlags modes)
            {
                LastMode = modes;
            }

            public int TryAutoDiscoverParsers()
            {
                return 0;
            }
        }

        private sealed class StubInputProvider : ITerminalInputProvider
        {
            public ITerminalInput GetInput(TerminalUI terminal)
            {
                return DefaultTerminalInput.Instance;
            }
        }

        private sealed class StubRuntimeProvider : ITerminalRuntimeProvider
        {
            public ITerminalRuntime ActiveRuntime => null;
        }

        private sealed class StubRuntimeScope : ITerminalRuntimeScope
        {
            internal ITerminalRuntime RegisteredRuntime { get; private set; }

            internal ITerminalRuntime UnregisteredRuntime { get; private set; }

            public ITerminalRuntime ActiveRuntime => RegisteredRuntime;

            public CommandLog Buffer => RegisteredRuntime?.Log;

            public CommandShell Shell => RegisteredRuntime?.Shell;

            public CommandHistory History => RegisteredRuntime?.History;

            public CommandAutoComplete AutoComplete => RegisteredRuntime?.AutoComplete;

            public void RegisterRuntime(ITerminalRuntime runtime)
            {
                RegisteredRuntime = runtime;
            }

            public void UnregisterRuntime(ITerminalRuntime runtime)
            {
                UnregisteredRuntime = runtime;
            }

            public bool Log(TerminalLogType type, string format, params object[] parameters)
            {
                return false;
            }

            public bool Log(string format, params object[] parameters)
            {
                return false;
            }
        }

        private sealed class StubRuntimeConfiguratorService : ITerminalRuntimeConfiguratorService
        {
            public TerminalRuntimeModeFlags CurrentMode { get; private set; }

            public bool EditorAutoDiscover { get; set; }

            public void SetMode(TerminalRuntimeModeFlags mode)
            {
                CurrentMode = mode;
            }

            public bool ShouldEnableEditorFeatures()
            {
                return false;
            }

            public bool ShouldEnableDevelopmentFeatures()
            {
                return false;
            }

            public bool ShouldEnableProductionFeatures()
            {
                return false;
            }

            public bool HasFlag(TerminalRuntimeModeFlags value, TerminalRuntimeModeFlags flag)
            {
                return (value & flag) == flag;
            }

            public int TryAutoDiscoverParsers()
            {
                return 0;
            }
        }
    }
}
