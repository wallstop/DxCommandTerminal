namespace WallstopStudios.DxCommandTerminal.Input
{
    public interface ITerminalInputTarget
    {
        bool IsClosed { get; }

        void Close();

        void ToggleSmall();

        void ToggleFull();

        void ToggleLauncher();

        void EnterCommand();

        void CompleteCommand(bool searchForward);

        void HandlePrevious();

        void HandleNext();
    }
}
