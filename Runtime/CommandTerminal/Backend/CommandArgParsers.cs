namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System.Globalization;

    /// <summary>
    ///     Culture-invariant parsers for the built-in types
    ///     <see cref="CommandArg" /> supports. Console input must parse
    ///     identically on every machine, so culture-sensitive overloads pin
    ///     their NumberStyles and <see cref="CultureInfo.InvariantCulture" />
    ///     explicitly; the style flags mirror each overload's default so only
    ///     the culture is pinned, never the accepted syntax.
    /// </summary>
    public static class CommandArgParsers
    {
        public static bool Float(string input, out float parsed) =>
            float.TryParse(
                input,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out parsed
            );

        public static bool Double(string input, out double parsed) =>
            double.TryParse(
                input,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out parsed
            );

        public static bool Decimal(string input, out decimal parsed) =>
            decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);

        public static bool Int(string input, out int parsed) =>
            int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        public static bool Uint(string input, out uint parsed) =>
            uint.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        public static bool Long(string input, out long parsed) =>
            long.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        public static bool Ulong(string input, out ulong parsed) =>
            ulong.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        public static bool Short(string input, out short parsed) =>
            short.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        public static bool Ushort(string input, out ushort parsed) =>
            ushort.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        public static bool Byte(string input, out byte parsed) =>
            byte.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        public static bool Sbyte(string input, out sbyte parsed) =>
            sbyte.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed);

        public static bool BigInteger(string input, out System.Numerics.BigInteger parsed) =>
            System.Numerics.BigInteger.TryParse(
                input,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed
            );

        public static bool DateTime(string input, out System.DateTime parsed) =>
            System.DateTime.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed
            );

        public static bool DateTimeOffset(string input, out System.DateTimeOffset parsed) =>
            System.DateTimeOffset.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsed
            );

        public static bool TimeSpan(string input, out System.TimeSpan parsed) =>
            System.TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out parsed);

        public static bool Bool(string input, out bool parsed) => bool.TryParse(input, out parsed);

        public static bool Char(string input, out char parsed) => char.TryParse(input, out parsed);

        public static bool Guid(string input, out System.Guid parsed) =>
            System.Guid.TryParse(input, out parsed);

        public static bool Version(string input, out System.Version parsed) =>
            System.Version.TryParse(input, out parsed);

        public static bool IPAddress(string input, out System.Net.IPAddress parsed) =>
            System.Net.IPAddress.TryParse(input, out parsed);
    }
}
