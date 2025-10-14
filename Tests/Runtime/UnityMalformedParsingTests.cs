namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using Backend;
    using NUnit.Framework;
    using UnityEngine;

    public sealed class UnityMalformedParsingTests
    {
        [Test]
        public void Vector3MalformedTooFewComponents()
        {
            CommandArg arg = new("x: y:2 z:");
            Assert.IsFalse(arg.TryGet(out Vector3 _));
        }

        [Test]
        public void Vector2IntMalformedMissingNumeric()
        {
            CommandArg arg = new("x: y:5");
            Assert.IsFalse(arg.TryGet(out Vector2Int _));
        }

        [Test]
        public void QuaternionMalformedTooFewComponents()
        {
            CommandArg arg = new("x:0.1 y:0.2 z:0.3");
            Assert.IsFalse(arg.TryGet(out Quaternion _));
        }

        [Test]
        public void RectMalformedMissingWidth()
        {
            CommandArg arg = new("x:10 y:20 width: height:50");
            Assert.IsFalse(arg.TryGet(out Rect _));
        }

        [Test]
        public void RectIntMalformedTooFewNumbers()
        {
            CommandArg arg = new("x:10 y:20");
            Assert.IsFalse(arg.TryGet(out RectInt _));
        }

        [Test]
        public void ColorMalformedNonNumericRgba()
        {
            CommandArg arg = new("RGBA(0.1, nope, 0.3, 0.4)");
            Assert.IsFalse(arg.TryGet(out Color _));
        }

        [Test]
        public void Vector2MixedInvalidToken()
        {
            CommandArg arg = new("1,foo");
            Assert.IsFalse(arg.TryGet(out Vector2 _));
        }

        [Test]
        public void Vector3TooManyComponents()
        {
            CommandArg arg = new("1,2,3,4");
            Assert.IsFalse(arg.TryGet(out Vector3 _));
        }

        [Test]
        public void Vector4SingleComponentOnly()
        {
            CommandArg arg = new("1");
            Assert.IsFalse(arg.TryGet(out Vector4 _));
        }

        [Test]
        public void Vector2IntNonIntegerComponent()
        {
            CommandArg arg = new("1,2.5");
            Assert.IsFalse(arg.TryGet(out Vector2Int _));
        }

        [Test]
        public void Vector3IntTooManyComponents()
        {
            CommandArg arg = new("1,2,3,4");
            Assert.IsFalse(arg.TryGet(out Vector3Int _));
        }

        [Test]
        public void RectNonNumericComponent()
        {
            CommandArg arg = new("1,2,three,4");
            Assert.IsFalse(arg.TryGet(out Rect _));
        }

        [Test]
        public void RectIntNonIntegerComponent()
        {
            CommandArg arg = new("1,2,3.5,4");
            Assert.IsFalse(arg.TryGet(out RectInt _));
        }

        [Test]
        public void QuaternionNonNumericComponent()
        {
            CommandArg arg = new("0.1, nope, 0.3, 0.4");
            Assert.IsFalse(arg.TryGet(out Quaternion _));
        }

        [Test]
        public void Vector3OnlyWrappers()
        {
            CommandArg arg = new("[]");
            Assert.IsFalse(arg.TryGet(out Vector3 _));
            arg = new CommandArg("<>");
            Assert.IsFalse(arg.TryGet(out Vector3 _));
            arg = new CommandArg("{}");
            Assert.IsFalse(arg.TryGet(out Vector3 _));
            arg = new CommandArg("()");
            Assert.IsFalse(arg.TryGet(out Vector3 _));
        }

        [Test]
        public void RectOnlyDelimiters()
        {
            CommandArg arg = new(",,,");
            Assert.IsFalse(arg.TryGet(out Rect _));
            arg = new CommandArg("___");
            Assert.IsFalse(arg.TryGet(out Rect _));
            arg = new CommandArg("///");
            Assert.IsFalse(arg.TryGet(out Rect _));
            arg = new CommandArg(";;;");
            Assert.IsFalse(arg.TryGet(out Rect _));
        }

        [Test]
        public void Vector2OnlyDelimiter()
        {
            CommandArg arg = new(":");
            Assert.IsFalse(arg.TryGet(out Vector2 _));
        }

        [Test]
        public void ColorInvalidNamedComponents()
        {
            CommandArg arg = new("red, green, blue");
            Assert.IsFalse(arg.TryGet(out Color _));
        }

        [Test]
        public void Vector3DuplicateLabelMissingValue()
        {
            CommandArg arg = new("x:1 x: y:3");
            Assert.IsFalse(arg.TryGet(out Vector3 _));
        }

        [Test]
        public void RectDuplicateLabelMissingValue()
        {
            CommandArg arg = new("x:10 y:20 width:100 width: height:50");
            Assert.IsFalse(arg.TryGet(out Rect _));
        }

        [Test]
        public void QuaternionDuplicateLabelMissingValue()
        {
            CommandArg arg = new("x:0.1 y:0.2 z:0.3 w:");
            Assert.IsFalse(arg.TryGet(out Quaternion _));
        }

        [Test]
        public void Vector2IntDuplicateLabelMissingValue()
        {
            CommandArg arg = new("x:1 x: y:2");
            Assert.IsFalse(arg.TryGet(out Vector2Int _));
        }
    }
}
