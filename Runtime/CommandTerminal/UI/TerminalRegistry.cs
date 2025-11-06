namespace WallstopStudios.DxCommandTerminal.UI
{
    using System.Collections.Generic;

    internal sealed class TerminalRegistry : ITerminalProvider
    {
        private readonly List<TerminalUI> _terminals = new();

        internal static ITerminalProvider Default { get; } = new TerminalRegistry();

        public TerminalUI ActiveTerminal
        {
            get
            {
                int count = _terminals.Count;
                return count > 0 ? _terminals[count - 1] : null;
            }
        }

        public IReadOnlyList<TerminalUI> ActiveTerminals => _terminals;

        public void Register(TerminalUI terminal)
        {
            if (terminal == null || _terminals.Contains(terminal))
            {
                return;
            }

            _terminals.Add(terminal);
        }

        public void Unregister(TerminalUI terminal)
        {
            if (terminal == null)
            {
                return;
            }

            _terminals.Remove(terminal);
        }
    }
}
