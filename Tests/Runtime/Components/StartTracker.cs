namespace DxCommandTerminal.Tests.Tests.Runtime.Components
{
    using UnityEngine;

    [DisallowMultipleComponent]
    public sealed class StartTracker : MonoBehaviour
    {
        public bool Started { get; private set; }

        private void Start()
        {
            Started = true;
        }
    }
}
