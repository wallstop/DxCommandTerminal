namespace WallstopStudios.DxCommandTerminal.Backend
{
    using Input;
    using UI;

    public interface ITerminalServiceLocator
    {
        ITerminalProvider TerminalProvider { get; }

        ITerminalRuntimeConfigurator RuntimeConfigurator { get; }

        ITerminalInputProvider InputProvider { get; }

        ITerminalRuntimeProvider RuntimeProvider { get; }

        ITerminalRuntimeScope RuntimeScope { get; }

        ITerminalRuntimeConfiguratorService RuntimeConfiguratorService { get; }

        ITerminalRuntimePool RuntimePool { get; }
    }

    internal sealed class TerminalServiceLocator : ITerminalServiceLocator
    {
        internal static ITerminalServiceLocator Default { get; } = new TerminalServiceLocator();

        private readonly ITerminalProvider _terminalProvider;
        private readonly ITerminalRuntimeConfigurator _runtimeConfigurator;
        private readonly ITerminalInputProvider _inputProvider;
        private readonly ITerminalRuntimeProvider _runtimeProvider;
        private readonly ITerminalRuntimeScope _runtimeScope;
        private readonly ITerminalRuntimeConfiguratorService _runtimeConfiguratorService;
        private readonly ITerminalRuntimePool _runtimePool;

        private TerminalServiceLocator()
        {
            _terminalProvider = TerminalRegistry.Default;
            _runtimeConfigurator = TerminalRuntimeConfiguratorProxy.Default;
            _inputProvider = TerminalInputProviderProxy.Default;
            _runtimeProvider = TerminalRuntimeProviderProxy.Default;
            _runtimeScope = TerminalRuntimeScope.Default;
            _runtimeConfiguratorService = TerminalRuntimeConfiguratorService.Default;
            _runtimePool = new TerminalRuntimePool();
        }

        public ITerminalProvider TerminalProvider => _terminalProvider;

        public ITerminalRuntimeConfigurator RuntimeConfigurator => _runtimeConfigurator;

        public ITerminalInputProvider InputProvider => _inputProvider;

        public ITerminalRuntimeProvider RuntimeProvider => _runtimeProvider;

        public ITerminalRuntimeScope RuntimeScope => _runtimeScope;

        public ITerminalRuntimeConfiguratorService RuntimeConfiguratorService =>
            _runtimeConfiguratorService;

        public ITerminalRuntimePool RuntimePool => _runtimePool;
    }

    internal sealed class MutableTerminalServiceLocator : ITerminalServiceLocator
    {
        internal MutableTerminalServiceLocator(
            ITerminalProvider terminalProvider,
            ITerminalRuntimeConfigurator runtimeConfigurator,
            ITerminalInputProvider inputProvider,
            ITerminalRuntimeProvider runtimeProvider,
            ITerminalRuntimeScope runtimeScope,
            ITerminalRuntimeConfiguratorService runtimeConfiguratorService,
            ITerminalRuntimePool runtimePool
        )
        {
            TerminalProvider = terminalProvider;
            RuntimeConfigurator = runtimeConfigurator;
            InputProvider = inputProvider;
            RuntimeProvider = runtimeProvider;
            RuntimeScope = runtimeScope;
            RuntimeConfiguratorService = runtimeConfiguratorService;
            RuntimePool = runtimePool;
        }

        public ITerminalProvider TerminalProvider { get; set; }

        public ITerminalRuntimeConfigurator RuntimeConfigurator { get; set; }

        public ITerminalInputProvider InputProvider { get; set; }

        public ITerminalRuntimeProvider RuntimeProvider { get; set; }

        public ITerminalRuntimeScope RuntimeScope { get; set; }

        public ITerminalRuntimeConfiguratorService RuntimeConfiguratorService { get; set; }

        public ITerminalRuntimePool RuntimePool { get; set; }
    }
}
