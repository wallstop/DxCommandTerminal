namespace WallstopStudios.DxCommandTerminal.Service
{
    using System;
    using Backend;
    using UI;
    using UnityEngine;
    using WallstopStudios.DxCommandTerminal.Input;

    /// <summary>
    /// Component-level provider that can supply service overrides to a <see cref="TerminalUI"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerminalServiceBindingComponent : MonoBehaviour, ITerminalServiceLocator
    {
        [SerializeField]
        [Tooltip("Optional binding asset whose values will be used unless overridden locally.")]
        private TerminalServiceBindingAsset _bindingAsset;

        [SerializeField]
        [Tooltip(
            "Override for the terminal provider. Leave empty to use the asset/default implementation."
        )]
        private UnityEngine.Object _terminalProviderOverride;

        [SerializeField]
        [Tooltip("Override for the runtime configurator.")]
        private UnityEngine.Object _runtimeConfiguratorOverride;

        [SerializeField]
        [Tooltip("Override for the input provider.")]
        private UnityEngine.Object _inputProviderOverride;

        [SerializeField]
        [Tooltip("Override for the runtime provider (active runtime accessor).")]
        private UnityEngine.Object _runtimeProviderOverride;

        [SerializeField]
        [Tooltip("Override for runtime scope (registration/logging helpers).")]
        private UnityEngine.Object _runtimeScopeOverride;

        [SerializeField]
        [Tooltip("Override for runtime configurator service (mode helpers).")]
        private UnityEngine.Object _runtimeConfiguratorServiceOverride;

        [SerializeField]
        [Tooltip("Override for runtime pool (runtime reuse management).")]
        private UnityEngine.Object _runtimePoolOverride;

        [NonSerialized]
        private TerminalRuntimePool _localRuntimePool;

        public ITerminalProvider TerminalProvider =>
            Resolve(
                _terminalProviderOverride,
                _bindingAsset != null ? _bindingAsset.TerminalProvider : TerminalRegistry.Default
            );

        public ITerminalRuntimeConfigurator RuntimeConfigurator =>
            Resolve(
                _runtimeConfiguratorOverride,
                _bindingAsset != null
                    ? _bindingAsset.RuntimeConfigurator
                    : TerminalRuntimeConfiguratorProxy.Default
            );

        public ITerminalInputProvider InputProvider =>
            Resolve(
                _inputProviderOverride,
                _bindingAsset != null
                    ? _bindingAsset.InputProvider
                    : TerminalInputProviderProxy.Default
            );

        public ITerminalRuntimeProvider RuntimeProvider =>
            Resolve(
                _runtimeProviderOverride,
                _bindingAsset != null
                    ? _bindingAsset.RuntimeProvider
                    : TerminalRuntimeProviderProxy.Default
            );

        public ITerminalRuntimeScope RuntimeScope =>
            Resolve(
                _runtimeScopeOverride,
                _bindingAsset != null ? _bindingAsset.RuntimeScope : TerminalRuntimeScope.Default
            );

        public ITerminalRuntimeConfiguratorService RuntimeConfiguratorService =>
            Resolve(
                _runtimeConfiguratorServiceOverride,
                _bindingAsset != null
                    ? _bindingAsset.RuntimeConfiguratorService
                    : TerminalRuntimeConfiguratorService.Default
            );

        public ITerminalRuntimePool RuntimePool =>
            Resolve(
                _runtimePoolOverride,
                _bindingAsset != null
                    ? _bindingAsset.RuntimePool
                    : _localRuntimePool ?? (_localRuntimePool = new TerminalRuntimePool())
            );

        internal void SetBindingAssetForTests(TerminalServiceBindingAsset asset)
        {
            _bindingAsset = asset;
        }

        internal void SetRuntimePoolOverrideForTests(UnityEngine.Object pool)
        {
            _runtimePoolOverride = pool;
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
            EnsureImplements(ref _runtimePoolOverride, typeof(ITerminalRuntimePool));
        }

        private void EnsureImplements(ref UnityEngine.Object field, System.Type expectedType)
        {
            if (field == null)
            {
                return;
            }

            if (!expectedType.IsAssignableFrom(field.GetType()))
            {
                Debug.LogWarning(
                    $"Assigned object '{field.name}' does not implement {expectedType.Name} and will be ignored.",
                    this
                );
                field = null;
            }
        }
#endif
    }
}
