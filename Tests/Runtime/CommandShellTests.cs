namespace DxCommandTerminal.Tests.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using CommandTerminal;
    using JetBrains.Annotations;
    using NUnit.Framework;
    using UnityEngine;
    using Random = System.Random;

    public sealed class CommandShellTests
    {
        private readonly struct TestStruct1
        {
            public readonly Guid id;

            public TestStruct1(Guid id)
            {
                this.id = id;
            }
        }

        private enum TestEnum1
        {
            [UsedImplicitly]
            Value1,

            [UsedImplicitly]
            Value2,

            [UsedImplicitly]
            Value3,

            [UsedImplicitly]
            Value4,

            [UsedImplicitly]
            Value5,
        }

        private enum TestEnum2
        {
            [UsedImplicitly]
            Value1,

            [UsedImplicitly]
            Value2,

            [UsedImplicitly]
            Value3,

            [UsedImplicitly]
            Value4,

            [UsedImplicitly]
            Value5,

            [UsedImplicitly]
            Value6,

            [UsedImplicitly]
            Value7,

            [UsedImplicitly]
            Value8,
        }

        private const int NumTries = 1_000;

        private readonly Random _random = new();

        private readonly List<string> _prepend = new() { "(", "[", "<", "{" };
        private readonly List<string> _append = new() { ")", "[", ">", "}" };

        [Test]
        public void Float()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out float value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0f, value);
            arg = new CommandArg("1");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(1.0f, value),
                $"Expected {value} to be equal to {1.0f}"
            );
            arg = new CommandArg("3");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(3.0f, value),
                $"Expected {value} to be equal to {3.0f}"
            );
            arg = new CommandArg("-100");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(-100.0f, value),
                $"Expected {value} to be equal to {-100.0f}"
            );

            for (int i = 0; i < NumTries; ++i)
            {
                float expected = (float)_random.NextDouble();
                arg = new CommandArg(expected.ToString(CultureInfo.InvariantCulture));
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Mathf.Approximately(expected, value),
                    $"{expected} not equal to {value}"
                );
            }

            arg = new CommandArg(nameof(float.PositiveInfinity));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(float.PositiveInfinity, value);
            arg = new CommandArg(nameof(float.NegativeInfinity));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(float.NegativeInfinity, value);
            arg = new CommandArg(nameof(float.NaN));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(float.NaN, value);
            arg = new CommandArg(nameof(float.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(float.MaxValue, value);

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Double()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out double value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0.0, value);
            arg = new CommandArg("1");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(1.0f, (float)value),
                $"Expected {value} to be equal to {1.0}"
            );
            arg = new CommandArg("3");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(3.0f, (float)value),
                $"Expected {value} to be equal to {3.0}"
            );
            arg = new CommandArg("-100");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(-100.0f, (float)value),
                $"Expected {value} to be equal to {-100.0}"
            );

            for (int i = 0; i < NumTries; ++i)
            {
                double expected = _random.NextDouble();
                arg = new CommandArg(expected.ToString(CultureInfo.InvariantCulture));
                Assert.IsTrue(arg.TryGet(out value));
                double delta = Math.Abs(expected - value);
                Assert.IsTrue(delta <= 0.00001, $"{expected} not equal to {value}");
            }

            arg = new CommandArg(nameof(double.PositiveInfinity));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(double.PositiveInfinity, value);
            arg = new CommandArg(nameof(double.NegativeInfinity));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(double.NegativeInfinity, value);
            arg = new CommandArg(nameof(double.NaN));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(double.NaN, value);
            arg = new CommandArg(nameof(double.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(double.MaxValue, value);

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Bool()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out bool value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("True");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(value);
            arg = new CommandArg("False");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsFalse(value);

            arg = new CommandArg("TRUE");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(value);
            arg = new CommandArg("true");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(value);

            arg = new CommandArg(bool.TrueString);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(value);
            arg = new CommandArg(bool.FalseString);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsFalse(value);

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("     ");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Int()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out int value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0, value);

            for (int i = 0; i < NumTries; ++i)
            {
                int expected = _random.Next(int.MinValue, int.MaxValue);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            arg = new CommandArg(nameof(int.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(int.MaxValue, value);
            arg = new CommandArg(nameof(int.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(int.MinValue, value);

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Enum()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out TestEnum1 value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual((TestEnum1)0, value);

            TestEnum1[] testEnum1Values = System
                .Enum.GetValues(typeof(TestEnum1))
                .OfType<TestEnum1>()
                .ToArray();
            for (int i = 0; i < NumTries; ++i)
            {
                int index = _random.Next(testEnum1Values.Length);
                TestEnum1 expected = testEnum1Values[index];
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            TestEnum2[] testEnum2Values = System
                .Enum.GetValues(typeof(TestEnum2))
                .OfType<TestEnum2>()
                .ToArray();
            for (int i = 0; i < NumTries; ++i)
            {
                int index = _random.Next(testEnum2Values.Length);
                TestEnum2 expected = testEnum2Values[index];
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out TestEnum2 value2));
                Assert.AreEqual(expected, value2);
            }

            arg = new CommandArg("1");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual((TestEnum1)1, value);
            arg = new CommandArg("100");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("-1");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Vector2()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Vector2 value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            Vector2 expected;

            // Unexpected input
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)_random.NextDouble();
                arg = new CommandArg(x.ToString(CultureInfo.InvariantCulture));
                Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{x}{post}");
                    Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
                }
            }

            // x,y
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)_random.NextDouble();
                float y = (float)_random.NextDouble();
                expected = new Vector2(x, y);
                Vector2 expectedRounded = new((float)Math.Round(x, 2), (float)Math.Round(y, 2));
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Mathf.Approximately(expectedRounded.x, value.x)
                        && Mathf.Approximately(expectedRounded.y, value.y),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                );

                arg = new CommandArg($"{expected.x},{expected.y}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Mathf.Approximately(expected.x, value.x)
                        && Mathf.Approximately(expected.y, value.y),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                );

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{expected.x},{expected.y}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.IsTrue(
                        Mathf.Approximately(expected.x, value.x)
                            && Mathf.Approximately(expected.y, value.y),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                    );
                }
            }

            // x,y,z (z is ok, but ignored)
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)_random.NextDouble();
                float y = (float)_random.NextDouble();
                float z = (float)_random.NextDouble();
                expected = new Vector2(x, y);
                Vector2 expectedRounded = new((float)Math.Round(x, 2), (float)Math.Round(y, 2));
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Mathf.Approximately(expectedRounded.x, value.x)
                        && Mathf.Approximately(expectedRounded.y, value.y),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                );

                arg = new CommandArg($"{expected.x},{expected.y},{z}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Mathf.Approximately(expected.x, value.x)
                        && Mathf.Approximately(expected.y, value.y),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                );

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{expected.x},{expected.y},{z}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.IsTrue(
                        Mathf.Approximately(expected.x, value.x)
                            && Mathf.Approximately(expected.y, value.y),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                    );
                }
            }

            arg = new CommandArg(nameof(UnityEngine.Vector2.zero));
            expected = UnityEngine.Vector2.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(expected.x, value.x)
                    && Mathf.Approximately(expected.y, value.y),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector2.up));
            expected = UnityEngine.Vector2.up;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(expected.x, value.x)
                    && Mathf.Approximately(expected.y, value.y),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector2.left));
            expected = UnityEngine.Vector2.left;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Mathf.Approximately(expected.x, value.x)
                    && Mathf.Approximately(expected.y, value.y),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Color()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Color value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            Color expected = UnityEngine.Color.white;
            arg = new CommandArg(nameof(UnityEngine.Color.white));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            expected = UnityEngine.Color.red;
            arg = new CommandArg(nameof(UnityEngine.Color.red));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            expected = UnityEngine.Color.cyan;
            arg = new CommandArg(nameof(UnityEngine.Color.cyan));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            expected = UnityEngine.Color.black;
            arg = new CommandArg(nameof(UnityEngine.Color.black));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            for (int i = 0; i < NumTries; ++i)
            {
                float r = (float)_random.NextDouble();
                float g = (float)_random.NextDouble();
                float b = (float)_random.NextDouble();
                expected = new Color(r, g, b);
                arg = new CommandArg(expected.ToString());

                // Colors have a floating point precision of 3 decimal places, otherwise our equality checks will be off
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Mathf.Approximately((float)Math.Round(expected.r, 3), value.r)
                        && Mathf.Approximately((float)Math.Round(expected.g, 3), value.g)
                        && Mathf.Approximately((float)Math.Round(expected.b, 3), value.b),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({r},{g},{b}). "
                        + $"Expected: ({expected.r},{expected.g},{expected.b})."
                );
            }
        }

        [Test]
        public void Untyped()
        {
            CommandArg arg = new("1");
            Assert.IsTrue(arg.TryGet(typeof(int), out object value));
            Assert.AreEqual(1, value);

            arg = new CommandArg("2.5");
            Assert.IsTrue(arg.TryGet(typeof(float), out value));
            Assert.IsTrue(
                Mathf.Approximately((float)value, 2.5f),
                $"Expected {value} to be approximately {2.5f}"
            );

            arg = new CommandArg("red");
            Assert.IsTrue(arg.TryGet(typeof(Color), out value));
            Assert.AreEqual(UnityEngine.Color.red, (Color)value);

            arg = new CommandArg("invisible");
            Assert.IsFalse(arg.TryGet(typeof(Color), out value));

            arg = new CommandArg("(1.2564, 3.6)");
            Assert.IsTrue(arg.TryGet(typeof(Vector2), out value));
            Vector2 expected = (Vector2)value;
            Assert.IsTrue(
                Mathf.Approximately(expected.x, 1.2564f) && Mathf.Approximately(expected.y, 3.6f),
                $"Expected {expected} to be approximately {arg.String}"
            );

            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(typeof(float), out value));
            Assert.IsFalse(arg.TryGet(typeof(int), out value));
            Assert.IsTrue(arg.TryGet(typeof(string), out value));
            Assert.AreEqual(arg.String, value);
        }

        [Test]
        public void CustomParserBuiltInType()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                string expectedString = Guid.NewGuid().ToString();
                int expected = _random.Next(int.MinValue, int.MaxValue);
                CommandArg arg = new(expectedString);
                Assert.IsFalse(arg.TryGet(out int value));
                Assert.IsTrue(arg.TryGet(out value, CustomParser));
                Assert.AreEqual(expected, value);

                // Make sure the parser isn't sticky
                Assert.IsFalse(arg.TryGet(out value));

                arg = new CommandArg(Guid.NewGuid().ToString());
                Assert.IsFalse(arg.TryGet(out value, CustomParser));
                continue;

                bool CustomParser(string input, out int parsed)
                {
                    if (string.Equals(expectedString, input, StringComparison.OrdinalIgnoreCase))
                    {
                        parsed = expected;
                        return true;
                    }

                    parsed = default;
                    return false;
                }
            }
        }

        [Test]
        public void CustomParserCustomType()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                string expectedString = Guid.NewGuid().ToString();
                TestStruct1 expected = new(Guid.NewGuid());
                CommandArg arg = new(expectedString);
                Assert.IsFalse(arg.TryGet(out TestStruct1 value));
                Assert.IsTrue(arg.TryGet(out value, CustomParser));
                Assert.AreEqual(expected, value);

                // Make sure the parser isn't sticky
                Assert.IsFalse(arg.TryGet(out value));

                arg = new CommandArg(Guid.NewGuid().ToString());
                Assert.IsFalse(arg.TryGet(out value, CustomParser));

                continue;

                bool CustomParser(string input, out TestStruct1 parsed)
                {
                    if (string.Equals(expectedString, input, StringComparison.OrdinalIgnoreCase))
                    {
                        parsed = expected;
                        return true;
                    }

                    parsed = default;
                    return false;
                }
            }
        }

        [Test]
        public void ParserRegistration() { }

        [Test]
        public void ParserDeregistration() { }
    }
}
