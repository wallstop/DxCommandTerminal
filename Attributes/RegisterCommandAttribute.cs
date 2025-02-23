namespace Attributes
{
    using System;
    using System.Runtime.CompilerServices;

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RegisterCommandAttribute : Attribute
    {
        public int MinArgCount { get; set; } = 0;
        public int MaxArgCount { get; set; } = -1;

        public string Name { get; set; }
        public string Help { get; set; }
        public string Hint { get; set; }

        public RegisterCommandAttribute([CallerMemberName] string commandName = null)
        {
            Name = commandName;
        }
    }
}
