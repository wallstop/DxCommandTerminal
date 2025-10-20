namespace WallstopStudios.DxCommandTerminal.Service
{
    using UnityEngine;
    using WallstopStudios.DxCommandTerminal.Internal;

    /// <summary>
    /// Global settings singleton that persists the default service binding asset.
    /// </summary>
    [ScriptableSingletonPath("Assets/Resources/TerminalServiceBindingSettings.asset")]
    public sealed class TerminalServiceBindingSettings
        : ScriptableObjectSingleton<TerminalServiceBindingSettings>
    {
        [SerializeField]
        [Tooltip(
            "Default service binding asset used when a TerminalUI does not specify one explicitly."
        )]
        private TerminalServiceBindingAsset _defaultBinding;

        private static TerminalServiceBindingAsset _runtimeFallbackBinding;

        public static TerminalServiceBindingAsset DefaultBinding
        {
            get
            {
                if (Instance != null && Instance._defaultBinding != null)
                {
                    return Instance._defaultBinding;
                }
                return _runtimeFallbackBinding;
            }
        }

        public TerminalServiceBindingAsset GetDefaultBinding()
        {
            return _defaultBinding;
        }

        public void SetDefaultBinding(TerminalServiceBindingAsset binding)
        {
            _defaultBinding = binding;
#if UNITY_EDITOR
            _runtimeFallbackBinding = null;
#endif
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        internal static void SetDefaultBindingForTests(TerminalServiceBindingAsset binding)
        {
            if (Instance != null)
            {
                Instance._defaultBinding = binding;
            }
            _runtimeFallbackBinding = binding;
        }
    }
}
