namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System.Globalization;
    using System.Numerics;

    public sealed class FloatArgParser : ArgParser<float>
    {
        public static readonly FloatArgParser Instance = new();

        protected override bool TryParseTyped(string input, out float value)
        {
            return float.TryParse(
                input,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class IntArgParser : ArgParser<int>
    {
        public static readonly IntArgParser Instance = new();

        protected override bool TryParseTyped(string input, out int value)
        {
            return int.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class UIntArgParser : ArgParser<uint>
    {
        public static readonly UIntArgParser Instance = new();

        protected override bool TryParseTyped(string input, out uint value)
        {
            return uint.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class LongArgParser : ArgParser<long>
    {
        public static readonly LongArgParser Instance = new();

        protected override bool TryParseTyped(string input, out long value)
        {
            return long.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class ULongArgParser : ArgParser<ulong>
    {
        public static readonly ULongArgParser Instance = new();

        protected override bool TryParseTyped(string input, out ulong value)
        {
            return ulong.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class DoubleArgParser : ArgParser<double>
    {
        public static readonly DoubleArgParser Instance = new();

        protected override bool TryParseTyped(string input, out double value)
        {
            return double.TryParse(
                input,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class ShortArgParser : ArgParser<short>
    {
        public static readonly ShortArgParser Instance = new();

        protected override bool TryParseTyped(string input, out short value)
        {
            return short.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class UShortArgParser : ArgParser<ushort>
    {
        public static readonly UShortArgParser Instance = new();

        protected override bool TryParseTyped(string input, out ushort value)
        {
            return ushort.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class ByteArgParser : ArgParser<byte>
    {
        public static readonly ByteArgParser Instance = new();

        protected override bool TryParseTyped(string input, out byte value)
        {
            return byte.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class SByteArgParser : ArgParser<sbyte>
    {
        public static readonly SByteArgParser Instance = new();

        protected override bool TryParseTyped(string input, out sbyte value)
        {
            return sbyte.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class DecimalArgParser : ArgParser<decimal>
    {
        public static readonly DecimalArgParser Instance = new();

        protected override bool TryParseTyped(string input, out decimal value)
        {
            return decimal.TryParse(
                input,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }

    public sealed class BigIntegerArgParser : ArgParser<BigInteger>
    {
        public static readonly BigIntegerArgParser Instance = new();

        protected override bool TryParseTyped(string input, out BigInteger value)
        {
            return BigInteger.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value
            );
        }
    }
}
