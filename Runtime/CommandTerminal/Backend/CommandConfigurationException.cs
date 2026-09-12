namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;

    /// <summary>
    ///     Thrown when a <see cref="CommandBuilder"/> or
    ///     <see cref="CommandDefinition"/> is misconfigured at definition
    ///     time: missing handlers, duplicate names, required-after-optional
    ///     ordering, unparseable argument types, defaults failing their own
    ///     validation, and subcommand conflicts.
    /// </summary>
    /// <remarks>
    ///     Derives from <see cref="InvalidOperationException"/>, so existing
    ///     handlers of definition-time failures keep working while new callers
    ///     can catch authoring mistakes distinctly and read
    ///     <see cref="CommandName"/> programmatically.
    /// </remarks>
    public sealed class CommandConfigurationException : InvalidOperationException
    {
        /// <summary>The command whose configuration was rejected, when known.</summary>
        public string CommandName { get; }

        public CommandConfigurationException(string message, string commandName = null)
            : base(message)
        {
            CommandName = commandName;
        }
    }
}
