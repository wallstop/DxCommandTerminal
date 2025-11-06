namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
    using Input;
    using UnityEngine;

    [DisallowMultipleComponent]
    public sealed class TestTerminalInput : MonoBehaviour, ITerminalInput
    {
        public string CommandText { get; set; } = string.Empty;
    }
}
