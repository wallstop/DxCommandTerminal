namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    ///     Immutable specification of one typed command argument: how it
    ///     parses, whether it is required, the value an omitted optional
    ///     argument takes, and how it validates and completes. Configured
    ///     through <see cref="CommandBuilder.Arg{T}"/> and the fluent members
    ///     of <see cref="CommandArgumentSpec{T}"/>.
    /// </summary>
    /// <remarks>
    ///     Every fluent call on <see cref="CommandArgumentSpec{T}"/> returns a
    ///     new instance, so a spec captured by the caller cannot be mutated
    ///     after its command registered.
    /// </remarks>
    public abstract class CommandArgument
    {
        /// <summary>Argument name, unique within its command.</summary>
        public string Name { get; }

        /// <summary>Friendly type name used in usage text and error messages.</summary>
        public string TypeName { get; }

        /// <summary>True when the argument must be provided on every invocation.</summary>
        public abstract bool IsRequired { get; }

        /// <summary>
        ///     Optional argument description, shown as the description of the
        ///     argument's completion candidates.
        /// </summary>
        public abstract string Description { get; }

        /// <summary>True when the argument contributes completion candidates.</summary>
        public abstract bool HasChoices { get; }

        /// <summary>
        ///     True when the argument is the command's unbounded trailing
        ///     argument: every token after the declared arguments parses and
        ///     validates as this argument's type and collects into one array.
        /// </summary>
        internal virtual bool IsRemaining => false;

        internal CommandArgument(string name, string typeName)
        {
            Name = name;
            TypeName = typeName;
        }

        internal abstract bool TryParse(CommandArg input, out object parsed);

        /// <summary>
        ///     Parses and validates every argument from
        ///     <paramref name="start"/> to the end of
        ///     <paramref name="arguments"/> with this spec's parser and
        ///     validators, collecting the values in order and storing the
        ///     typed array in <paramref name="slot"/> of the invocation's
        ///     parsed-values buffer — the same boundary every parsed value
        ///     crosses once. Only remaining arguments support this; other
        ///     specs throw.
        /// </summary>
        internal abstract bool TryParseAll(
            BorrowedCommandArguments arguments,
            int start,
            object[] parsedValues,
            int slot,
            out CommandArg failedToken,
            out string validationError
        );

        internal abstract object GetDefault();

        internal abstract string ValidateParsed(object parsed);

        internal abstract string FormatParseError(CommandArg input);

        internal abstract void AppendUsage(StringBuilder builder);

        internal abstract void AppendCompletions(
            in CommandCompletionContext context,
            List<CommandCompletion> results
        );
    }
}
