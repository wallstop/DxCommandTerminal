namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;

    internal sealed class TerminalRuntimePool : ITerminalRuntimePool
    {
        private readonly Stack<ITerminalRuntime> _pool = new Stack<ITerminalRuntime>();

        public bool TryRent(out ITerminalRuntime runtime)
        {
            if (_pool.Count > 0)
            {
                runtime = _pool.Pop();
                return runtime != null;
            }

            runtime = null;
            return false;
        }

        public void Return(ITerminalRuntime runtime)
        {
            if (runtime == null)
            {
                return;
            }

            _pool.Push(runtime);
        }

        public void Clear()
        {
            _pool.Clear();
        }
    }
}
