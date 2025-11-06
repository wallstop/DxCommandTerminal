namespace WallstopStudios.DxCommandTerminal.Attributes
{
    using System;
    using Backend;

    /// <summary>
    /// Attach to a command method to specify a dynamic argument completer.
    /// Type must implement IArgumentCompleter and have either a public parameterless
    /// constructor or a public static Instance property/field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class CommandCompleterAttribute : Attribute
    {
        public Type CompleterType { get; }

        public CommandCompleterAttribute(Type completerType)
        {
            if (completerType == null)
            {
                throw new ArgumentNullException(nameof(completerType));
            }

            if (!typeof(IArgumentCompleter).IsAssignableFrom(completerType))
            {
                throw new ArgumentException(
                    $"{completerType.FullName} must implement {nameof(IArgumentCompleter)}",
                    nameof(completerType)
                );
            }

            CompleterType = completerType;
        }
    }
}
