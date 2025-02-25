namespace DxCommandTerminal.Tests.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using CommandTerminal;
    using NUnit.Framework;
    using UnityEngine;
    using Random = System.Random;

    public sealed class CommandShellTests
    {
        private enum TestEnum1
        {
            Value1,
            Value2,
            Value3,
            Value4,
            Value5,
        }

        private enum TestEnum2
        {
            Value1,
            Value2,
            Value3,
            Value4,
            Value5,
            Value6,
            Value7,
            Value8,
        }

        private const int NumTries = 1_000;

        private readonly Random _random = new();

        [Test]
        public void Float()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out float value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0f, value);

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
            Assert.AreEqual(true, value);
            arg = new CommandArg("False");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(false, value);

            arg = new CommandArg("TRUE");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(true, value);
            arg = new CommandArg("true");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(true, value);

            arg = new CommandArg(bool.TrueString);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(true, value);
            arg = new CommandArg(bool.FalseString);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(false, value);

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
            Assert.AreEqual((int.MaxValue), value);
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
            List<string> prepend = new() { "(", "[", "<", "{" };
            List<string> append = new() { ")", "[", ">", "}" };
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)_random.NextDouble();
                float y = (float)_random.NextDouble();
                expected = new(x, y);
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
                    (string pre, string post) in prepend.Zip(
                        append,
                        (preValue, postvalue) => (x: preValue, y: postvalue)
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

            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)_random.NextDouble();
                float y = (float)_random.NextDouble();
                float z = (float)_random.NextDouble();
                expected = new(x, y);
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
                    (string pre, string post) in prepend.Zip(
                        append,
                        (preValue, postvalue) => (x: preValue, y: postvalue)
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
    }
}
