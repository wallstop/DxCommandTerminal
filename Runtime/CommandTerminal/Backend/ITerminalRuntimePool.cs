namespace WallstopStudios.DxCommandTerminal.Backend
{
    public interface ITerminalRuntimePool
    {
        bool TryRent(out ITerminalRuntime runtime);

        void Return(ITerminalRuntime runtime);

        void Clear();
    }
}
