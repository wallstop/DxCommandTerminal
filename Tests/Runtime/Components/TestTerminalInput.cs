namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Components
{
    using WallstopStudios.DxCommandTerminal.Input;
    using UnityEngine;

    [DisallowMultipleComponent]
    public sealed class TestTerminalInput : MonoBehaviour, ITerminalInput
    {
        public string CommandText { get; set; } = string.Empty;
    }
}
