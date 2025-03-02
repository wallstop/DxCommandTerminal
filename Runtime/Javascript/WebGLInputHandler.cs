namespace Javascript
{
    using System.Runtime.InteropServices;
    using UnityEngine;

    [DisallowMultipleComponent]
    public sealed class WebGLInputHandler : MonoBehaviour
    {
        [DllImport("__Internal")]
        private static extern void DisableBrowserShortcuts();

        // TODO
        private void Awake()
        {
#if UNITY_WEBGL
            WebGLInput.captureAllKeyboardInput = true;
#endif
        }

        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            DisableBrowserShortcuts();
#endif
        }
    }
}
