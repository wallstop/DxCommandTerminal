namespace WallstopStudios.DxCommandTerminal.Backend
{
    using System;
    using System.Globalization;
    using System.Linq;

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

        /// <summary>
        ///     Parses two or three components: "1.5, 2.5" is (1.5, 2.5) and a
        ///     third component is accepted and the value dropped, matching the
        ///     three-component form of the other vector types.
        /// </summary>
        public static bool Vector2(string input, out UnityEngine.Vector2 parsed)
        {
            if (!TrySplitComponents(input, out string[] split))
            {
                parsed = default;
                return false;
            }

            switch (split.Length)
            {
                case 2 when Float(split[0], out float x) && Float(split[1], out float y):
                    parsed = new UnityEngine.Vector2(x, y);
                    return true;
                case 3
                    when Float(split[0], out float x)
                        && Float(split[1], out float y)
                        && Float(split[2], out float z):
                    parsed = (UnityEngine.Vector2)new UnityEngine.Vector3(x, y, z);
                    return true;
                default:
                    parsed = default;
                    return false;
            }
        }

        /// <summary>
        ///     Parses two or three components: "1.5, 2.5" is (1.5, 2.5, 0).
        /// </summary>
        public static bool Vector3(string input, out UnityEngine.Vector3 parsed)
        {
            if (!TrySplitComponents(input, out string[] split))
            {
                parsed = default;
                return false;
            }

            switch (split.Length)
            {
                case 2 when Float(split[0], out float x) && Float(split[1], out float y):
                    parsed = new UnityEngine.Vector3(x, y);
                    return true;
                case 3
                    when Float(split[0], out float x)
                        && Float(split[1], out float y)
                        && Float(split[2], out float z):
                    parsed = new UnityEngine.Vector3(x, y, z);
                    return true;
                default:
                    parsed = default;
                    return false;
            }
        }

        /// <summary>
        ///     Parses two, three, or four components: "1.5, 2.5" is (1.5, 2.5, 0, 0).
        /// </summary>
        public static bool Vector4(string input, out UnityEngine.Vector4 parsed)
        {
            if (!TrySplitComponents(input, out string[] split))
            {
                parsed = default;
                return false;
            }

            switch (split.Length)
            {
                case 2 when Float(split[0], out float x) && Float(split[1], out float y):
                    parsed = new UnityEngine.Vector4(x, y);
                    return true;
                case 3
                    when Float(split[0], out float x)
                        && Float(split[1], out float y)
                        && Float(split[2], out float z):
                    parsed = new UnityEngine.Vector4(x, y, z);
                    return true;
                case 4
                    when Float(split[0], out float x)
                        && Float(split[1], out float y)
                        && Float(split[2], out float z)
                        && Float(split[3], out float w):
                    parsed = new UnityEngine.Vector4(x, y, z, w);
                    return true;
                default:
                    parsed = default;
                    return false;
            }
        }

        /// <summary>
        ///     Parses two or three integer components: "1, 2" is (1, 2) and a
        ///     third component is accepted and the value dropped, matching the
        ///     three-component form of the other integer vector types.
        /// </summary>
        public static bool Vector2Int(string input, out UnityEngine.Vector2Int parsed)
        {
            if (!TrySplitComponents(input, out string[] split))
            {
                parsed = default;
                return false;
            }

            switch (split.Length)
            {
                case 2 when Int(split[0], out int x) && Int(split[1], out int y):
                    parsed = new UnityEngine.Vector2Int(x, y);
                    return true;
                case 3
                    when Int(split[0], out int x)
                        && Int(split[1], out int y)
                        && Int(split[2], out int z):
                    parsed = (UnityEngine.Vector2Int)new UnityEngine.Vector3Int(x, y, z);
                    return true;
                default:
                    parsed = default;
                    return false;
            }
        }

        /// <summary>
        ///     Parses two or three integer components: "1, 2" is (1, 2, 0).
        /// </summary>
        public static bool Vector3Int(string input, out UnityEngine.Vector3Int parsed)
        {
            if (!TrySplitComponents(input, out string[] split))
            {
                parsed = default;
                return false;
            }

            switch (split.Length)
            {
                case 2 when Int(split[0], out int x) && Int(split[1], out int y):
                    parsed = new UnityEngine.Vector3Int(x, y);
                    return true;
                case 3
                    when Int(split[0], out int x)
                        && Int(split[1], out int y)
                        && Int(split[2], out int z):
                    parsed = new UnityEngine.Vector3Int(x, y, z);
                    return true;
                default:
                    parsed = default;
                    return false;
            }
        }

        /// <summary>
        ///     Parses three or four components into RGB(A) channels. A leading
        ///     "RGBA" group label, as produced by <see cref="object.ToString" />
        ///     on the Color type, is stripped: "RGBA(1.0, 0.0, 0.0)".
        /// </summary>
        public static bool Color(string input, out UnityEngine.Color parsed)
        {
            if (string.IsNullOrEmpty(input))
            {
                parsed = default;
                return false;
            }

            string colorString = input;
            if (input.StartsWith("RGBA", StringComparison.OrdinalIgnoreCase))
            {
                colorString = colorString.Replace(
                    "RGBA",
                    string.Empty,
                    StringComparison.OrdinalIgnoreCase
                );
            }

            if (!TrySplitComponents(colorString, out string[] split))
            {
                parsed = default;
                return false;
            }

            switch (split.Length)
            {
                case 3
                    when Float(split[0], out float r)
                        && Float(split[1], out float g)
                        && Float(split[2], out float b):
                    parsed = new UnityEngine.Color(r, g, b);
                    return true;
                case 4
                    when Float(split[0], out float r)
                        && Float(split[1], out float g)
                        && Float(split[2], out float b)
                        && Float(split[3], out float a):
                    parsed = new UnityEngine.Color(r, g, b, a);
                    return true;
                default:
                    parsed = default;
                    return false;
            }
        }

        /// <summary>
        ///     Parses four components into (x, y, z, w): "0, 0, 0, 1" is identity.
        /// </summary>
        public static bool Quaternion(string input, out UnityEngine.Quaternion parsed)
        {
            if (!TrySplitComponents(input, out string[] split))
            {
                parsed = default;
                return false;
            }

            if (
                split.Length == 4
                && Float(split[0], out float x)
                && Float(split[1], out float y)
                && Float(split[2], out float z)
                && Float(split[3], out float w)
            )
            {
                parsed = new UnityEngine.Quaternion(x, y, z, w);
                return true;
            }

            parsed = default;
            return false;
        }

        /// <summary>
        ///     Parses four components into x, y, width, and height. Per-component
        ///     "x:", "y:", "width:", and "height:" labels, as produced by
        ///     <see cref="object.ToString" /> on the Rect type, are stripped:
        ///     "x:0, y:1, width:2, height:3" and "0, 1, 2, 3" are equivalent.
        /// </summary>
        public static bool Rect(string input, out UnityEngine.Rect parsed)
        {
            if (!TrySplitComponents(input, out string[] split))
            {
                parsed = default;
                return false;
            }

            if (
                split.Length == 4
                && Float(
                    split[0].Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    out float x
                )
                && Float(
                    split[1].Replace("y:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    out float y
                )
                && Float(
                    split[2].Replace("width:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    out float width
                )
                && Float(
                    split[3].Replace("height:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    out float height
                )
            )
            {
                parsed = new UnityEngine.Rect(x, y, width, height);
                return true;
            }

            parsed = default;
            return false;
        }

        /// <summary>
        ///     Parses four integer components into x, y, width, and height.
        ///     Per-component "x:", "y:", "width:", and "height:" labels, as
        ///     produced by <see cref="object.ToString" /> on the RectInt type,
        ///     are stripped.
        /// </summary>
        public static bool RectInt(string input, out UnityEngine.RectInt parsed)
        {
            if (!TrySplitComponents(input, out string[] split))
            {
                parsed = default;
                return false;
            }

            if (
                split.Length == 4
                && Int(
                    split[0].Replace("x:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    out int x
                )
                && Int(
                    split[1].Replace("y:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    out int y
                )
                && Int(
                    split[2].Replace("width:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    out int width
                )
                && Int(
                    split[3].Replace("height:", string.Empty, StringComparison.OrdinalIgnoreCase),
                    out int height
                )
            )
            {
                parsed = new UnityEngine.RectInt(x, y, width, height);
                return true;
            }

            parsed = default;
            return false;
        }

        /// <summary>
        ///     Parses six components into a center and a size: "0, 0, 0, 1, 1, 1"
        ///     is a bounds centered at the origin with size one on every axis.
        /// </summary>
        public static bool Bounds(string input, out UnityEngine.Bounds parsed)
        {
            if (
                TrySplitComponents(input, out string[] split)
                && split.Length == 6
                && Float(split[0], out float centerX)
                && Float(split[1], out float centerY)
                && Float(split[2], out float centerZ)
                && Float(split[3], out float sizeX)
                && Float(split[4], out float sizeY)
                && Float(split[5], out float sizeZ)
            )
            {
                parsed = new UnityEngine.Bounds(
                    new UnityEngine.Vector3(centerX, centerY, centerZ),
                    new UnityEngine.Vector3(sizeX, sizeY, sizeZ)
                );
                return true;
            }

            parsed = default;
            return false;
        }

        /// <summary>
        ///     Parses six integer components into a position and a size:
        ///     "0, 0, 0, 1, 1, 1" is a one-cell bounds at the grid origin.
        /// </summary>
        public static bool BoundsInt(string input, out UnityEngine.BoundsInt parsed)
        {
            if (
                TrySplitComponents(input, out string[] split)
                && split.Length == 6
                && Int(split[0], out int positionX)
                && Int(split[1], out int positionY)
                && Int(split[2], out int positionZ)
                && Int(split[3], out int sizeX)
                && Int(split[4], out int sizeY)
                && Int(split[5], out int sizeZ)
            )
            {
                parsed = new UnityEngine.BoundsInt(
                    new UnityEngine.Vector3Int(positionX, positionY, positionZ),
                    new UnityEngine.Vector3Int(sizeX, sizeY, sizeZ)
                );
                return true;
            }

            parsed = default;
            return false;
        }

        /// <summary>
        ///     Parses four integer components in the
        ///     RectOffset(int, int, int, int) order: left, right, top, and
        ///     bottom. "4, 8, 2, 6" offsets four on the left, eight on the
        ///     right, two on the top, and six on the bottom.
        /// </summary>
        public static bool RectOffset(string input, out UnityEngine.RectOffset parsed)
        {
            if (
                TrySplitComponents(input, out string[] split)
                && split.Length == 4
                && Int(split[0], out int left)
                && Int(split[1], out int right)
                && Int(split[2], out int top)
                && Int(split[3], out int bottom)
            )
            {
                parsed = new UnityEngine.RectOffset(left, right, top, bottom);
                return true;
            }

            parsed = null;
            return false;
        }

        /// <summary>
        ///     Parses four components into a normal and a distance along that
        ///     normal: "0, 1, 0, 5" is the plane with up normal at height five.
        /// </summary>
        public static bool Plane(string input, out UnityEngine.Plane parsed)
        {
            if (
                TrySplitComponents(input, out string[] split)
                && split.Length == 4
                && Float(split[0], out float normalX)
                && Float(split[1], out float normalY)
                && Float(split[2], out float normalZ)
                && Float(split[3], out float distance)
            )
            {
                parsed = new UnityEngine.Plane(
                    new UnityEngine.Vector3(normalX, normalY, normalZ),
                    distance
                );
                return true;
            }

            parsed = default;
            return false;
        }

        /// <summary>
        ///     Parses six components into an origin and a direction:
        ///     "0, 0, 0, 0, 1, 0" is a ray from the origin pointing up.
        /// </summary>
        public static bool Ray(string input, out UnityEngine.Ray parsed)
        {
            if (
                TrySplitComponents(input, out string[] split)
                && split.Length == 6
                && Float(split[0], out float originX)
                && Float(split[1], out float originY)
                && Float(split[2], out float originZ)
                && Float(split[3], out float directionX)
                && Float(split[4], out float directionY)
                && Float(split[5], out float directionZ)
            )
            {
                parsed = new UnityEngine.Ray(
                    new UnityEngine.Vector3(originX, originY, originZ),
                    new UnityEngine.Vector3(directionX, directionY, directionZ)
                );
                return true;
            }

            parsed = default;
            return false;
        }

        /// <summary>
        ///     Parses two components into real and imaginary parts:
        ///     "1.5, -2" is 1.5 - 2i.
        /// </summary>
        public static bool Complex(string input, out System.Numerics.Complex parsed)
        {
            if (
                TrySplitComponents(input, out string[] split)
                && split.Length == 2
                && Double(split[0], out double real)
                && Double(split[1], out double imaginary)
            )
            {
                parsed = new System.Numerics.Complex(real, imaginary);
                return true;
            }

            parsed = default;
            return false;
        }

        private static bool TrySplitComponents(string input, out string[] split)
        {
            if (input == null)
            {
                split = null;
                return false;
            }

            string strippedInput = CommandArg
                .IgnoredValuesForComplexTypes.Where(ignored => !string.IsNullOrEmpty(ignored))
                .Aggregate(
                    input,
                    (current, ignored) =>
                        current.Replace(ignored, string.Empty, StringComparison.OrdinalIgnoreCase)
                );

            foreach (char delimiter in CommandArg.Delimiters)
            {
                if (0 <= strippedInput.IndexOf(delimiter, StringComparison.Ordinal))
                {
                    split = strippedInput.Split(delimiter);
                    return true;
                }
            }

            split = null;
            return false;
        }
    }
}
