namespace WallstopStudios.DxCommandTerminal.Service
{
    using System;
    using Backend;
    using Input;
    using UI;
    using UnityEngine;

    /// <summary>
    /// ScriptableObject wrapper that exposes service bindings for Terminal consumers.
    /// Can be assigned to <see cref="TerminalUI"/> to replace global service instances without resorting to static overrides.
    /// </summary>
    [CreateAssetMenu(
        fileName = "TerminalServiceBinding",
        menuName = "DXCommandTerminal/Terminal Service Binding",
        order = 1000
    )]
    public sealed class TerminalServiceBindingAsset : ScriptableObject, ITerminalServiceLocator
    {
        [SerializeField]
        [Tooltip(
            "Optional override for the terminal registry. Leave empty to use the built-in TerminalRegistry."
        )]
        private UnityEngine.Object _terminalProviderOverride;

        [SerializeField]
        [Tooltip(
            "Optional override for runtime configurator (mode flags and autodiscovery). Leave empty for defaults."
        )]
        private UnityEngine.Object _runtimeConfiguratorOverride;

        [SerializeField]
        [Tooltip(
            "Optional override for input provider. Leave empty to use the default input source."
        )]
        private UnityEngine.Object _inputProviderOverride;

        [SerializeField]
        [Tooltip(
            "Optional override for runtime provider (active runtime accessor). Leave empty to use default proxy."
        )]
        private UnityEngine.Object _runtimeProviderOverride;

        [SerializeField]
        [Tooltip(
            "Optional override for runtime scope (registration/log helpers). Leave empty for default scope."
        )]
        private UnityEngine.Object _runtimeScopeOverride;

        [SerializeField]
        [Tooltip(
            "Optional override for runtime configurator service (mode evaluation helpers). Leave empty for default service."
        )]
        private UnityEngine.Object _runtimeConfiguratorServiceOverride;

        [NonSerialized]
        private TerminalRuntimePool _runtimePoolInstance;

        public ITerminalProvider TerminalProvider =>
            Resolve(_terminalProviderOverride, TerminalRegistry.Default);

        public ITerminalRuntimeConfigurator RuntimeConfigurator =>
            Resolve(_runtimeConfiguratorOverride, TerminalRuntimeConfiguratorProxy.Default);

        public ITerminalInputProvider InputProvider =>
            Resolve(_inputProviderOverride, TerminalInputProviderProxy.Default);

        public ITerminalRuntimeProvider RuntimeProvider =>
            Resolve(_runtimeProviderOverride, TerminalRuntimeProviderProxy.Default);

        public ITerminalRuntimeScope RuntimeScope =>
            Resolve(_runtimeScopeOverride, TerminalRuntimeScope.Default);

        public ITerminalRuntimeConfiguratorService RuntimeConfiguratorService =>
            Resolve(
                _runtimeConfiguratorServiceOverride,
                TerminalRuntimeConfiguratorService.Default
            );

        public ITerminalRuntimePool RuntimePool =>
            _runtimePoolInstance ??= new TerminalRuntimePool();

        internal void SetTerminalProviderForTests(UnityEngine.Object provider)
        {
            _terminalProviderOverride = provider;
        }

        internal void SetRuntimeConfiguratorForTests(UnityEngine.Object configurator)
        {
            _runtimeConfiguratorOverride = configurator;
        }

        internal void SetInputProviderForTests(UnityEngine.Object provider)
        {
            _inputProviderOverride = provider;
        }

        internal void SetRuntimeProviderForTests(UnityEngine.Object provider)
        {
            _runtimeProviderOverride = provider;
        }

        internal void SetRuntimeScopeForTests(UnityEngine.Object scope)
        {
            _runtimeScopeOverride = scope;
        }

        internal void SetRuntimeConfiguratorServiceForTests(UnityEngine.Object service)
        {
            _runtimeConfiguratorServiceOverride = service;
        }

        private static T Resolve<T>(UnityEngine.Object candidate, T fallback)
            where T : class
        {
            if (candidate == null)
            {
                return fallback;
            }

            if (candidate is T typed)
            {
                return typed;
            }

            return fallback;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureImplements(ref _terminalProviderOverride, typeof(ITerminalProvider));
            EnsureImplements(
                ref _runtimeConfiguratorOverride,
                typeof(ITerminalRuntimeConfigurator)
            );
            EnsureImplements(ref _inputProviderOverride, typeof(ITerminalInputProvider));
            EnsureImplements(ref _runtimeProviderOverride, typeof(ITerminalRuntimeProvider));
            EnsureImplements(ref _runtimeScopeOverride, typeof(ITerminalRuntimeScope));
            EnsureImplements(
                ref _runtimeConfiguratorServiceOverride,
                typeof(ITerminalRuntimeConfiguratorService)
            );
        }

        private void EnsureImplements(ref UnityEngine.Object field, Type expectedInterface)
        {
            if (field == null)
            {
                return;
            }

            Type fieldType = field.GetType();
            if (!expectedInterface.IsAssignableFrom(fieldType))
            {
                Debug.LogWarning(
                    $"Assigned object '{field.name}' does not implement {expectedInterface.Name} and will be ignored.",
                    this
                );
                field = null;
            }
        }
#endif
    }
}
