namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Backend;

    internal static class CommandArgParserCommon
    {
        public static bool TryParseFloatList(
            ReadOnlySpan<char> input,
            out float a,
            out float b,
            out float c,
            out float d,
            out int count
        )
        {
            a = 0f;
            b = 0f;
            c = 0f;
            d = 0f;
            count = 0;
            int i = 0;
            while (i < input.Length)
            {
                char ch = input[i];
                if (char.IsWhiteSpace(ch) || IsIgnoredChar(ch))
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(ch))
                {
                    while (i < input.Length && input[i] != ':')
                    {
                        i++;
                    }
                    if (i < input.Length && input[i] == ':')
                    {
                        i++;
                    }
                    continue;
                }
                if (CommandArg.Delimiters.Contains(ch))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < input.Length)
                {
                    char cch = input[i];
                    if (
                        char.IsWhiteSpace(cch)
                        || CommandArg.Delimiters.Contains(cch)
                        || IsIgnoredChar(cch)
                    )
                    {
                        break;
                    }
                    i++;
                }
                ReadOnlySpan<char> slice = input.Slice(start, i - start);
                if (
                    !float.TryParse(
                        slice,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float value
                    )
                )
                {
                    return false;
                }
                switch (count)
                {
                    case 0:
                        a = value;
                        break;
                    case 1:
                        b = value;
                        break;
                    case 2:
                        c = value;
                        break;
                    case 3:
                        d = value;
                        break;
                    default:
                        break;
                }
                count++;
            }
            return 0 < count;
        }

        public static bool TryParseIntList(
            ReadOnlySpan<char> input,
            out int a,
            out int b,
            out int c,
            out int d,
            out int count
        )
        {
            a = 0;
            b = 0;
            c = 0;
            d = 0;
            count = 0;
            int i = 0;
            while (i < input.Length)
            {
                char ch = input[i];
                if (char.IsWhiteSpace(ch) || IsIgnoredChar(ch))
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(ch))
                {
                    while (i < input.Length && input[i] != ':')
                    {
                        i++;
                    }
                    if (i < input.Length && input[i] == ':')
                    {
                        i++;
                    }
                    continue;
                }
                if (CommandArg.Delimiters.Contains(ch))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < input.Length)
                {
                    char cch = input[i];
                    if (
                        char.IsWhiteSpace(cch)
                        || CommandArg.Delimiters.Contains(cch)
                        || IsIgnoredChar(cch)
                    )
                    {
                        break;
                    }
                    i++;
                }
                ReadOnlySpan<char> slice = input.Slice(start, i - start);
                if (
                    !int.TryParse(
                        slice,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int value
                    )
                )
                {
                    return false;
                }
                switch (count)
                {
                    case 0:
                        a = value;
                        break;
                    case 1:
                        b = value;
                        break;
                    case 2:
                        c = value;
                        break;
                    case 3:
                        d = value;
                        break;
                    default:
                        break;
                }
                count++;
            }
            return 0 < count;
        }

        public static string[] StripAndSplit(string input)
        {
            string strippedInput = input;
            foreach (string ignored in CommandArg.IgnoredValuesForComplexTypes)
            {
                if (!string.IsNullOrEmpty(ignored))
                {
                    strippedInput = strippedInput.Replace(
                        ignored,
                        string.Empty,
                        StringComparison.OrdinalIgnoreCase
                    );
                }
            }

            foreach (char delimiter in CommandArg.Delimiters)
            {
                if (strippedInput.Contains(delimiter))
                {
                    return strippedInput.Split(delimiter);
                }
            }

            return new[] { strippedInput };
        }

        public static bool IsIgnoredChar(char ch)
        {
            return ch == '('
                || ch == ')'
                || ch == '['
                || ch == ']'
                || ch == '\''
                || ch == '`'
                || ch == '|'
                || ch == '{'
                || ch == '}'
                || ch == '<'
                || ch == '>';
        }

        public static bool TryParseLabeledFloatMap(
            ReadOnlySpan<char> input,
            out Dictionary<string, float> values,
            out bool malformed
        )
        {
            values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            malformed = false;
            int i = 0;
            bool foundAnyLabel = false;
            while (i < input.Length)
            {
                char ch = input[i];
                if (char.IsWhiteSpace(ch) || IsIgnoredChar(ch))
                {
                    i++;
                    continue;
                }

                if (!char.IsLetter(ch))
                {
                    i++;
                    continue;
                }

                int labelStart = i;
                while (i < input.Length && char.IsLetter(input[i]))
                {
                    i++;
                }
                ReadOnlySpan<char> labelSpan = input.Slice(labelStart, i - labelStart);
                // Skip whitespace
                while (i < input.Length && char.IsWhiteSpace(input[i]))
                {
                    i++;
                }
                if (i >= input.Length || input[i] != ':')
                {
                    // Not a label-value pair; continue scanning
                    continue;
                }
                i++; // skip colon
                foundAnyLabel = true;

                // Skip whitespace and ignored chars after colon
                while (i < input.Length && (char.IsWhiteSpace(input[i]) || IsIgnoredChar(input[i])))
                {
                    i++;
                }

                int valueStart = i;
                while (
                    i < input.Length
                    && !char.IsWhiteSpace(input[i])
                    && !IsIgnoredChar(input[i])
                    && !CommandArg.Delimiters.Contains(input[i])
                    && !char.IsLetter(input[i])
                )
                {
                    i++;
                }

                ReadOnlySpan<char> valueSpan = input.Slice(valueStart, i - valueStart);
                if (valueSpan.Length == 0)
                {
                    malformed = true;
                    continue;
                }

                if (
                    float.TryParse(
                        valueSpan,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float f
                    )
                )
                {
                    string label = labelSpan.ToString();
                    values[label] = f;
                }
                else
                {
                    malformed = true;
                }
            }

            return foundAnyLabel;
        }

        public static bool TryParseLabeledIntMap(
            ReadOnlySpan<char> input,
            out Dictionary<string, int> values,
            out bool malformed
        )
        {
            values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            malformed = false;
            int i = 0;
            bool foundAnyLabel = false;
            while (i < input.Length)
            {
                char ch = input[i];
                if (char.IsWhiteSpace(ch) || IsIgnoredChar(ch))
                {
                    i++;
                    continue;
                }

                if (!char.IsLetter(ch))
                {
                    i++;
                    continue;
                }

                int labelStart = i;
                while (i < input.Length && char.IsLetter(input[i]))
                {
                    i++;
                }
                ReadOnlySpan<char> labelSpan = input.Slice(labelStart, i - labelStart);
                // Skip whitespace
                while (i < input.Length && char.IsWhiteSpace(input[i]))
                {
                    i++;
                }
                if (i >= input.Length || input[i] != ':')
                {
                    // Not a label-value pair; continue scanning
                    continue;
                }
                i++; // skip colon
                foundAnyLabel = true;

                // Skip whitespace and ignored chars after colon
                while (i < input.Length && (char.IsWhiteSpace(input[i]) || IsIgnoredChar(input[i])))
                {
                    i++;
                }

                int valueStart = i;
                while (
                    i < input.Length
                    && !char.IsWhiteSpace(input[i])
                    && !IsIgnoredChar(input[i])
                    && !CommandArg.Delimiters.Contains(input[i])
                    && !char.IsLetter(input[i])
                )
                {
                    i++;
                }

                ReadOnlySpan<char> valueSpan = input.Slice(valueStart, i - valueStart);
                if (valueSpan.Length == 0)
                {
                    malformed = true;
                    continue;
                }

                if (
                    int.TryParse(
                        valueSpan,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int iv
                    )
                )
                {
                    string label = labelSpan.ToString();
                    values[label] = iv;
                }
                else
                {
                    malformed = true;
                }
            }

            return foundAnyLabel;
        }
    }
}
