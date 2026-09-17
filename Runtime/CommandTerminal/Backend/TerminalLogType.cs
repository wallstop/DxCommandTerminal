namespace WallstopStudios.DxCommandTerminal.Backend
{
    using UnityEngine;

    public enum TerminalLogType
    {
        Error = LogType.Error,
        Assert = LogType.Assert,
        Warning = LogType.Warning,
        Message = LogType.Log,
        Exception = LogType.Exception,
        Input = 5,
        ShellMessage = 6,
    }
}
