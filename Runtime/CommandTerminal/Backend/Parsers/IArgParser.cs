namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System;

    public interface IArgParser
    {
        Type TargetType { get; }
        bool TryParse(string input, out object value);
    }

    public abstract class ArgParser<T> : IArgParser
    {
        public Type TargetType => typeof(T);

        public bool TryParse(string input, out object value)
        {
            if (TryParseTyped(input, out T typed))
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }

        protected abstract bool TryParseTyped(string input, out T value);
    }
}
