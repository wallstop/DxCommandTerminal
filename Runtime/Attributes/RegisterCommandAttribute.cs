namespace WallstopStudios.DxCommandTerminal.Attributes
{
    using System;
    using System.Reflection;
    using Backend;

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RegisterCommandAttribute : Attribute
    {
        public string Name { get; set; }
        public int MinArgCount { get; set; } = 0;

        /*
            int, not int?: attribute properties must be constant-compatible,
            so nullable types are not legal here. Negative means unbounded;
            CommandInfo normalizes it to null at registration.
         */
        public int MaxArgCount { get; set; } = -1;
        public string Help { get; set; }
        public string Hint { get; set; }

        public bool EditorOnly { get; set; }

        public bool DevelopmentOnly { get; set; }

        public bool AddToHistory { get; set; } = true;

        /// <summary>
        ///     Environments the command may run in. Defaults to
        ///     <see cref="CommandExecutionContextSets.All"/> so attributed commands
        ///     keep their previous availability everywhere, Edit Mode
        ///     included. Set a narrower set to opt out of environments; the
        ///     Edit-Mode opt-in rule applies to new
        ///     <see cref="Backend.CommandDefinition"/> metadata, whose
        ///     default is <see cref="CommandExecutionContextSets.Gameplay"/>.
        /// </summary>
        public CommandExecutionContexts Contexts { get; set; } = CommandExecutionContextSets.All;

        // Should not be used by client code - internal flag to indicate that this is a "Default", or in-built command
        internal bool Default { get; set; }

        public RegisterCommandAttribute(string commandName = null)
        {
            commandName = commandName?.Replace(" ", string.Empty).Trim();
            Name = commandName;
        }

        internal RegisterCommandAttribute(bool isDefault)
            : this(string.Empty)
        {
            Default = isDefault;
        }

        private static string InferCommandName(string methodName)
        {
            const string commandId = "COMMAND";
            int index = methodName.IndexOf(commandId, StringComparison.OrdinalIgnoreCase);

            // Method is prefixed, suffixed with, or contains "COMMAND".
            string commandName =
                0 <= index ? methodName.Remove(index, commandId.Length) : methodName;

            return commandName;
        }

        public void NormalizeName(MethodInfo method)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = InferCommandName(method.Name);
            }

            Name = Name.Replace(" ", string.Empty).Trim();
        }
    }
}
