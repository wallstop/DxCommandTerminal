namespace WallstopStudios.DxCommandTerminal.Input
{
    public interface IInputHandler
    {
        bool ShouldHandleInputThisFrame { get; }
    }
}
