namespace WallstopStudios.DxCommandTerminal.Backend.Parsers
{
    using System;
    using System.Globalization;
    using UnityEngine;

    public sealed class Vector2ArgParser : ArgParser<Vector2>
    {
        public static readonly Vector2ArgParser Instance = new();

        protected override bool TryParseTyped(string input, out Vector2 value)
        {
            if (
                CommandArgParserCommon.TryParseLabeledFloatMap(
                    input.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, float> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (
                    labeled.TryGetValue("x", out float lx) && labeled.TryGetValue("y", out float ly)
                )
                {
                    value = new Vector2(lx, ly);
                    return true;
                }
                value = default;
                return false;
            }
            if (
                CommandArgParserCommon.TryParseFloatList(
                    input.AsSpan(),
                    out float f0,
                    out float f1,
                    out float f2,
                    out _,
                    out int cnt
                )
            )
            {
                if (cnt == 2)
                {
                    value = new Vector2(f0, f1);
                    return true;
                }
                if (cnt == 3)
                {
                    value = (Vector2)new Vector3(f0, f1, f2);
                    return true;
                }
            }

            string[] split = CommandArgParserCommon.StripAndSplit(input);
            switch (split.Length)
            {
                case 2
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float x
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float y
                        ):
                    value = new Vector2(x, y);
                    return true;
                case 3
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float x
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float y
                        )
                        && float.TryParse(
                            split[2],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float z
                        ):
                    value = (Vector2)new Vector3(x, y, z);
                    return true;
            }

            value = default;
            return false;
        }
    }

    public sealed class Vector3ArgParser : ArgParser<Vector3>
    {
        public static readonly Vector3ArgParser Instance = new();

        protected override bool TryParseTyped(string input, out Vector3 value)
        {
            if (
                CommandArgParserCommon.TryParseLabeledFloatMap(
                    input.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, float> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (
                    labeled.TryGetValue("x", out float lx)
                    && labeled.TryGetValue("y", out float ly)
                    && labeled.TryGetValue("z", out float lz)
                )
                {
                    value = new Vector3(lx, ly, lz);
                    return true;
                }
                value = default;
                return false;
            }
            if (
                CommandArgParserCommon.TryParseFloatList(
                    input.AsSpan(),
                    out float f0,
                    out float f1,
                    out float f2,
                    out _,
                    out int cnt
                )
            )
            {
                if (cnt == 2)
                {
                    value = new Vector3(f0, f1);
                    return true;
                }
                if (cnt == 3)
                {
                    value = new Vector3(f0, f1, f2);
                    return true;
                }
            }

            string[] split = CommandArgParserCommon.StripAndSplit(input);
            switch (split.Length)
            {
                case 2
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float x
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float y
                        ):
                    value = new Vector3(x, y);
                    return true;
                case 3
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float x
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float y
                        )
                        && float.TryParse(
                            split[2],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float z
                        ):
                    value = new Vector3(x, y, z);
                    return true;
            }

            value = default;
            return false;
        }
    }

    public sealed class Vector4ArgParser : ArgParser<Vector4>
    {
        public static readonly Vector4ArgParser Instance = new();

        protected override bool TryParseTyped(string input, out Vector4 value)
        {
            if (
                CommandArgParserCommon.TryParseLabeledFloatMap(
                    input.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, float> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (
                    labeled.TryGetValue("x", out float lx)
                    && labeled.TryGetValue("y", out float ly)
                    && labeled.TryGetValue("z", out float lz)
                    && labeled.TryGetValue("w", out float lw)
                )
                {
                    value = new Vector4(lx, ly, lz, lw);
                    return true;
                }
                value = default;
                return false;
            }
            if (
                CommandArgParserCommon.TryParseFloatList(
                    input.AsSpan(),
                    out float f0,
                    out float f1,
                    out float f2,
                    out float f3,
                    out int cnt
                )
            )
            {
                if (cnt == 2)
                {
                    value = new Vector4(f0, f1);
                    return true;
                }
                if (cnt == 3)
                {
                    value = new Vector4(f0, f1, f2);
                    return true;
                }
                if (cnt == 4)
                {
                    value = new Vector4(f0, f1, f2, f3);
                    return true;
                }
            }

            string[] split = CommandArgParserCommon.StripAndSplit(input);
            switch (split.Length)
            {
                case 2
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float x
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float y
                        ):
                    value = new Vector4(x, y);
                    return true;
                case 3
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float x
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float y
                        )
                        && float.TryParse(
                            split[2],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float z
                        ):
                    value = new Vector4(x, y, z);
                    return true;
                case 4
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float x
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float y
                        )
                        && float.TryParse(
                            split[2],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float z
                        )
                        && float.TryParse(
                            split[3],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float w
                        ):
                    value = new Vector4(x, y, z, w);
                    return true;
            }

            value = default;
            return false;
        }
    }

    public sealed class Vector2IntArgParser : ArgParser<Vector2Int>
    {
        public static readonly Vector2IntArgParser Instance = new();

        protected override bool TryParseTyped(string input, out Vector2Int value)
        {
            if (
                CommandArgParserCommon.TryParseLabeledIntMap(
                    input.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, int> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (labeled.TryGetValue("x", out int lx) && labeled.TryGetValue("y", out int ly))
                {
                    value = new Vector2Int(lx, ly);
                    return true;
                }
                value = default;
                return false;
            }
            if (
                CommandArgParserCommon.TryParseIntList(
                    input.AsSpan(),
                    out int i0,
                    out int i1,
                    out int i2,
                    out _,
                    out int icnt
                )
            )
            {
                if (icnt == 2)
                {
                    value = new Vector2Int(i0, i1);
                    return true;
                }
                if (icnt == 3)
                {
                    value = (Vector2Int)new Vector3Int(i0, i1, i2);
                    return true;
                }
            }

            string[] split = CommandArgParserCommon.StripAndSplit(input);
            switch (split.Length)
            {
                case 2
                    when int.TryParse(
                        split[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int x
                    )
                        && int.TryParse(
                            split[1],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int y
                        ):
                    value = new Vector2Int(x, y);
                    return true;
                case 3
                    when int.TryParse(
                        split[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int x
                    )
                        && int.TryParse(
                            split[1],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int y
                        )
                        && int.TryParse(
                            split[2],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int z
                        ):
                    value = (Vector2Int)new Vector3Int(x, y, z);
                    return true;
            }

            value = default;
            return false;
        }
    }

    public sealed class Vector3IntArgParser : ArgParser<Vector3Int>
    {
        public static readonly Vector3IntArgParser Instance = new();

        protected override bool TryParseTyped(string input, out Vector3Int value)
        {
            if (
                CommandArgParserCommon.TryParseLabeledIntMap(
                    input.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, int> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (
                    labeled.TryGetValue("x", out int lx)
                    && labeled.TryGetValue("y", out int ly)
                    && labeled.TryGetValue("z", out int lz)
                )
                {
                    value = new Vector3Int(lx, ly, lz);
                    return true;
                }
                value = default;
                return false;
            }
            if (
                CommandArgParserCommon.TryParseIntList(
                    input.AsSpan(),
                    out int i0,
                    out int i1,
                    out int i2,
                    out _,
                    out int icnt
                )
            )
            {
                if (icnt == 2)
                {
                    value = new Vector3Int(i0, i1);
                    return true;
                }
                if (icnt == 3)
                {
                    value = new Vector3Int(i0, i1, i2);
                    return true;
                }
            }

            string[] split = CommandArgParserCommon.StripAndSplit(input);
            switch (split.Length)
            {
                case 2
                    when int.TryParse(
                        split[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int x
                    )
                        && int.TryParse(
                            split[1],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int y
                        ):
                    value = new Vector3Int(x, y);
                    return true;
                case 3
                    when int.TryParse(
                        split[0],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int x
                    )
                        && int.TryParse(
                            split[1],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int y
                        )
                        && int.TryParse(
                            split[2],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int z
                        ):
                    value = new Vector3Int(x, y, z);
                    return true;
            }

            value = default;
            return false;
        }
    }

    public sealed class ColorArgParser : ArgParser<Color>
    {
        public static readonly ColorArgParser Instance = new();

        protected override bool TryParseTyped(string input, out Color value)
        {
            string colorString = input;
            if (colorString.StartsWith("RGBA", StringComparison.OrdinalIgnoreCase))
            {
                colorString = colorString.Replace(
                    "RGBA",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase
                );
            }

            if (
                CommandArgParserCommon.TryParseLabeledFloatMap(
                    colorString.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, float> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (
                    labeled.TryGetValue("r", out float r)
                    && labeled.TryGetValue("g", out float g)
                    && labeled.TryGetValue("b", out float b)
                )
                {
                    float a = labeled.TryGetValue("a", out float la) ? la : 1.0f;
                    value = new Color(r, g, b, a);
                    return true;
                }
                value = default;
                return false;
            }

            if (
                CommandArgParserCommon.TryParseFloatList(
                    colorString.AsSpan(),
                    out float cr,
                    out float cg,
                    out float cb,
                    out float ca,
                    out int ccnt
                )
            )
            {
                if (ccnt == 3)
                {
                    value = new Color(cr, cg, cb);
                    return true;
                }
                if (ccnt == 4)
                {
                    value = new Color(cr, cg, cb, ca);
                    return true;
                }
            }

            string[] split = CommandArgParserCommon.StripAndSplit(colorString);
            switch (split.Length)
            {
                case 3
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float r
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float g
                        )
                        && float.TryParse(
                            split[2],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float b
                        ):
                    value = new Color(r, g, b);
                    return true;
                case 4
                    when float.TryParse(
                        split[0],
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out float r
                    )
                        && float.TryParse(
                            split[1],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float g
                        )
                        && float.TryParse(
                            split[2],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float b
                        )
                        && float.TryParse(
                            split[3],
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out float a
                        ):
                    value = new Color(r, g, b, a);
                    return true;
            }

            value = default;
            return false;
        }
    }

    public sealed class QuaternionArgParser : ArgParser<Quaternion>
    {
        public static readonly QuaternionArgParser Instance = new();

        protected override bool TryParseTyped(string input, out Quaternion value)
        {
            if (
                CommandArgParserCommon.TryParseLabeledFloatMap(
                    input.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, float> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (
                    labeled.TryGetValue("x", out float lx)
                    && labeled.TryGetValue("y", out float ly)
                    && labeled.TryGetValue("z", out float lz)
                    && labeled.TryGetValue("w", out float lw)
                )
                {
                    value = new Quaternion(lx, ly, lz, lw);
                    return true;
                }
                value = default;
                return false;
            }
            if (
                CommandArgParserCommon.TryParseFloatList(
                    input.AsSpan(),
                    out float qx,
                    out float qy,
                    out float qz,
                    out float qw,
                    out int qcnt
                )
                && qcnt == 4
            )
            {
                value = new Quaternion(qx, qy, qz, qw);
                return true;
            }

            string[] split = CommandArgParserCommon.StripAndSplit(input);
            if (
                split.Length == 4
                && float.TryParse(
                    split[0],
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out float x
                )
                && float.TryParse(
                    split[1],
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out float y
                )
                && float.TryParse(
                    split[2],
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out float z
                )
                && float.TryParse(
                    split[3],
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out float w
                )
            )
            {
                value = new Quaternion(x, y, z, w);
                return true;
            }

            value = default;
            return false;
        }
    }

    public sealed class RectArgParser : ArgParser<Rect>
    {
        public static readonly RectArgParser Instance = new();

        protected override bool TryParseTyped(string input, out Rect value)
        {
            if (
                CommandArgParserCommon.TryParseLabeledFloatMap(
                    input.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, float> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (
                    labeled.TryGetValue("x", out float lx)
                    && labeled.TryGetValue("y", out float ly)
                    && labeled.TryGetValue("width", out float lw)
                    && labeled.TryGetValue("height", out float lh)
                )
                {
                    value = new Rect(lx, ly, lw, lh);
                    return true;
                }
                value = default;
                return false;
            }
            if (
                CommandArgParserCommon.TryParseFloatList(
                    input.AsSpan(),
                    out float rx,
                    out float ry,
                    out float rw,
                    out float rh,
                    out int rcnt
                )
                && rcnt == 4
            )
            {
                value = new Rect(rx, ry, rw, rh);
                return true;
            }

            string[] split = CommandArgParserCommon.StripAndSplit(input);
            if (
                split.Length == 4
                && float.TryParse(
                    split[0].Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out float x
                )
                && float.TryParse(
                    split[1].Replace("y:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out float y
                )
                && float.TryParse(
                    split[2].Replace("width:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out float width
                )
                && float.TryParse(
                    split[3].Replace("height:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out float height
                )
            )
            {
                value = new Rect(x, y, width, height);
                return true;
            }

            value = default;
            return false;
        }
    }

    public sealed class RectIntArgParser : ArgParser<RectInt>
    {
        public static readonly RectIntArgParser Instance = new();

        protected override bool TryParseTyped(string input, out RectInt value)
        {
            if (
                CommandArgParserCommon.TryParseLabeledIntMap(
                    input.AsSpan(),
                    out System.Collections.Generic.Dictionary<string, int> labeled,
                    out bool malformed
                )
            )
            {
                if (malformed)
                {
                    value = default;
                    return false;
                }
                if (
                    labeled.TryGetValue("x", out int lx)
                    && labeled.TryGetValue("y", out int ly)
                    && labeled.TryGetValue("width", out int lw)
                    && labeled.TryGetValue("height", out int lh)
                )
                {
                    value = new RectInt(lx, ly, lw, lh);
                    return true;
                }
                value = default;
                return false;
            }
            if (
                CommandArgParserCommon.TryParseIntList(
                    input.AsSpan(),
                    out int rix,
                    out int riy,
                    out int riw,
                    out int rih,
                    out int ricnt
                )
                && ricnt == 4
            )
            {
                value = new RectInt(rix, riy, riw, rih);
                return true;
            }

            string[] split = CommandArgParserCommon.StripAndSplit(input);
            if (
                split.Length == 4
                && int.TryParse(
                    split[0].Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int x
                )
                && int.TryParse(
                    split[1].Replace("y:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int y
                )
                && int.TryParse(
                    split[2].Replace("width:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int width
                )
                && int.TryParse(
                    split[3].Replace("height:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int height
                )
            )
            {
                value = new RectInt(x, y, width, height);
                return true;
            }

            value = default;
            return false;
        }
    }
}
