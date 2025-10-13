namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System;
    using System.Globalization;
    using System.Net;

    public sealed class GuidArgParser : ArgParser<Guid>
    {
        public static readonly GuidArgParser Instance = new GuidArgParser();

        protected override bool TryParseTyped(string input, out Guid value)
        {
            return Guid.TryParse(input, out value);
        }
    }

    public sealed class DateTimeArgParser : ArgParser<DateTime>
    {
        public static readonly DateTimeArgParser Instance = new DateTimeArgParser();

        protected override bool TryParseTyped(string input, out DateTime value)
        {
            bool ok = DateTime.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out value
            );
            if (!ok)
            {
                return false;
            }
            if (value.Kind == DateTimeKind.Utc)
            {
                value = value.ToLocalTime();
            }
            return true;
        }
    }

    public sealed class DateTimeOffsetArgParser : ArgParser<DateTimeOffset>
    {
        public static readonly DateTimeOffsetArgParser Instance = new DateTimeOffsetArgParser();

        protected override bool TryParseTyped(string input, out DateTimeOffset value)
        {
            return DateTimeOffset.TryParse(
                input,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out value
            );
        }
    }

    public sealed class CharArgParser : ArgParser<char>
    {
        public static readonly CharArgParser Instance = new CharArgParser();

        protected override bool TryParseTyped(string input, out char value)
        {
            return char.TryParse(input, out value);
        }
    }

    public sealed class TimeSpanArgParser : ArgParser<TimeSpan>
    {
        public static readonly TimeSpanArgParser Instance = new TimeSpanArgParser();

        protected override bool TryParseTyped(string input, out TimeSpan value)
        {
            return TimeSpan.TryParse(input, CultureInfo.InvariantCulture, out value);
        }
    }

    public sealed class VersionArgParser : ArgParser<Version>
    {
        public static readonly VersionArgParser Instance = new VersionArgParser();

        protected override bool TryParseTyped(string input, out Version value)
        {
            return Version.TryParse(input, out value);
        }
    }

    public sealed class IPAddressArgParser : ArgParser<IPAddress>
    {
        public static readonly IPAddressArgParser Instance = new IPAddressArgParser();

        protected override bool TryParseTyped(string input, out IPAddress value)
        {
            return IPAddress.TryParse(input, out value);
        }
    }
}
