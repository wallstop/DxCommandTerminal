namespace WallstopStudios.DxCommandTerminal.UI
{
    using System.Collections.Generic;

    public interface ITerminalProvider
    {
        TerminalUI ActiveTerminal { get; }

        IReadOnlyList<TerminalUI> ActiveTerminals { get; }

        void Register(TerminalUI terminal);

        void Unregister(TerminalUI terminal);
    }
}
