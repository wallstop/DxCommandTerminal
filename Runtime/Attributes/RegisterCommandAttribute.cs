namespace Attributes
{
    using System;
    using System.Reflection;

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RegisterCommandAttribute : Attribute
    {
        public string Name { get; set; }
        public int MinArgCount { get; set; } = 0;
        public int MaxArgCount { get; set; } = -1;
        public string Help { get; set; }
        public string Hint { get; set; }

        // Should not be used by client code - internal flag to indicate that this is a "Default", or in-built command
        public bool Default { get; set; }

        public RegisterCommandAttribute(string commandName = null)
        {
            commandName = commandName?.Replace(" ", string.Empty);
            Name = commandName;
        }

        public void NormalizeName(MethodInfo method)
        {
            Name ??= InferCommandName(InferFrontCommandName(method.Name) ?? method.Name);
            Name = Name.Replace(" ", string.Empty);
        }

        private static string InferCommandName(string methodName)
        {
            const string commandId = "COMMAND";
            int index = methodName.IndexOf(commandId, StringComparison.OrdinalIgnoreCase);

            // Method is prefixed, suffixed with, or contains "COMMAND".
            string commandName =
                index >= 0 ? methodName.Remove(index, commandId.Length) : methodName;

            return commandName;
        }

        private static string InferFrontCommandName(string methodName)
        {
            const string frontId = "FRONT";
            int index = methodName.IndexOf(frontId, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? methodName.Remove(index, frontId.Length) : null;
        }
    }
}
