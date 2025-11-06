namespace WallstopStudios.DxCommandTerminal.Input
{
    internal static class TerminalInputSourceFactory
    {
        private sealed class UnityInputSource : ITerminalInputSource
        {
            public UnityInputSource(InputMode mode)
            {
                Mode = mode;
            }

            public InputMode Mode { get; }

            public bool IsKeyPressed(string binding)
            {
                return InputHelpers.IsKeyPressed(binding, Mode);
            }
        }

        public static ITerminalInputSource Create(InputMode mode)
        {
            return new UnityInputSource(mode);
        }
    }
}
