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

    public sealed class CommandArgTests
    {
        private readonly struct TestStruct1 : IEquatable<TestStruct1>
        {
            private readonly Guid _id;

            public TestStruct1(Guid id)
            {
                _id = id;
            }

            public override bool Equals(object obj)
            {
                return obj is TestStruct1 other && Equals(other);
            }

            public bool Equals(TestStruct1 other)
            {
                return _id.Equals(other._id);
            }

            public override int GetHashCode()
            {
                return _id.GetHashCode();
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

        private const int NumTries = 5_000;

        private readonly Random _random = new();

        private readonly List<string> _prepend = new() { "(", "[", "<", "{" };
        private readonly List<string> _append = new() { ")", "]", ">", "}" };

        [SetUp]
        [TearDown]
        public void CleanUp()
        {
            int unregistered = CommandArg.UnregisterAllParsers();
            if (0 < unregistered)
            {
                Debug.Log($"Unregistered {unregistered} parser{(unregistered == 1 ? "" : "s")}.");
            }
        }

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
            Assert.IsTrue(Approximately(1.0f, value), $"Expected {value} to be equal to {1.0f}");
            arg = new CommandArg("3");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(Approximately(3.0f, value), $"Expected {value} to be equal to {3.0f}");
            arg = new CommandArg("-100");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(-100.0f, value),
                $"Expected {value} to be equal to {-100.0f}"
            );

            for (int i = 0; i < NumTries; ++i)
            {
                float expected = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                arg = new CommandArg(expected.ToString(CultureInfo.InvariantCulture));
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(Approximately(expected, value), $"{expected} not equal to {value}");
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

            const double tooBig = (double)float.MaxValue * 2;
            arg = new CommandArg(tooBig.ToString(CultureInfo.InvariantCulture));
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            const double tooSmall = (double)float.MinValue * 2;
            arg = new CommandArg(tooSmall.ToString(CultureInfo.InvariantCulture));
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

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
                Approximately(1.0f, (float)value),
                $"Expected {value} to be equal to {1.0}"
            );
            arg = new CommandArg("3");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(3.0f, (float)value),
                $"Expected {value} to be equal to {3.0}"
            );
            arg = new CommandArg("-100");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(-100.0f, (float)value),
                $"Expected {value} to be equal to {-100.0}"
            );

            for (int i = 0; i < NumTries; ++i)
            {
                double expected = _random.NextDouble() * _random.Next(int.MinValue, int.MaxValue);
                arg = new CommandArg(expected.ToString(CultureInfo.InvariantCulture));
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(Approximately(expected, value), $"{expected} not equal to {value}");
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

            arg = new CommandArg(int.MaxValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(int.MaxValue, value);

            arg = new CommandArg(nameof(int.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(int.MinValue, value);

            arg = new CommandArg(int.MinValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(int.MinValue, value);

            const long tooBig = int.MaxValue + 1L;
            arg = new CommandArg(tooBig.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            const long tooSmall = int.MinValue - 1L;
            arg = new CommandArg(tooSmall.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Long()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out long value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0L, value);

            byte[] bytes = new byte[sizeof(long)];
            for (int i = 0; i < NumTries; ++i)
            {
                _random.NextBytes(bytes);
                long expected = BitConverter.ToInt64(bytes, 0);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            arg = new CommandArg(nameof(long.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(long.MaxValue, value);

            arg = new CommandArg(long.MaxValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(long.MaxValue, value);

            arg = new CommandArg(nameof(long.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(long.MinValue, value);

            arg = new CommandArg(long.MinValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(long.MinValue, value);

            arg = new CommandArg(long.MinValue + "0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(long.MinValue + "0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Ulong()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out ulong value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0UL, value);

            byte[] bytes = new byte[sizeof(ulong)];
            for (int i = 0; i < NumTries; ++i)
            {
                _random.NextBytes(bytes);
                ulong expected = BitConverter.ToUInt64(bytes, 0);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            arg = new CommandArg(nameof(ulong.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(ulong.MaxValue, value);

            arg = new CommandArg(ulong.MaxValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(ulong.MaxValue, value);

            arg = new CommandArg(nameof(ulong.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(ulong.MinValue, value);

            arg = new CommandArg(ulong.MinValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(ulong.MinValue, value);

            arg = new CommandArg("-1");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(ulong.MaxValue + "0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

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

            arg = new CommandArg("Value6");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            Assert.IsTrue(arg.TryGet(out TestEnum2 testEnum2Value));
            Assert.AreEqual(TestEnum2.Value6, testEnum2Value);

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
            Assert.IsTrue(arg.TryGet(out testEnum2Value));
            Assert.AreEqual((TestEnum2)1, testEnum2Value);

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
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
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

            const float vector2RoundTolerance = 0.01f;

            // x,y
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float y = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                expected = new Vector2(x, y);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, vector2RoundTolerance)
                        && Approximately(expected.y, value.y, vector2RoundTolerance),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Input: ({x},{y}). "
                        + $"Value: ({value.x},{value.y}). "
                        + $"Expected: ({expected.x},{expected.y})."
                );

                arg = new CommandArg($"{expected.x},{expected.y}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x) && Approximately(expected.y, value.y),
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
                        Approximately(expected.x, value.x) && Approximately(expected.y, value.y),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                    );
                }
            }

            // x,y,z (z is ok, but ignored)
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float y = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float z = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                expected = new Vector2(x, y);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, vector2RoundTolerance)
                        && Approximately(expected.y, value.y, vector2RoundTolerance),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Input: ({x},{y},{z}). "
                        + $"Value: ({value.x},{value.y}). "
                        + $"Expected: ({expected.x},{expected.y})."
                );

                arg = new CommandArg($"{expected.x},{expected.y},{z}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x) && Approximately(expected.y, value.y),
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
                        Approximately(expected.x, value.x) && Approximately(expected.y, value.y),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                    );
                }
            }

            arg = new CommandArg(nameof(UnityEngine.Vector2.zero));
            expected = UnityEngine.Vector2.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x) && Approximately(expected.y, value.y),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector2.up));
            expected = UnityEngine.Vector2.up;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x) && Approximately(expected.y, value.y),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector2.left));
            expected = UnityEngine.Vector2.left;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x) && Approximately(expected.y, value.y),
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
        public void Vector3()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Vector3 value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            Vector3 expected;

            const float vector3RoundTolerance = 0.01f;

            // Unexpected input
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
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
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float y = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float z = 0f;
                expected = new Vector3(x, y);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, vector3RoundTolerance)
                        && Approximately(expected.y, value.y, vector3RoundTolerance)
                        && Approximately(expected.z, value.z, vector3RoundTolerance),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Input: ({x},{y},{z}). "
                        + $"Value: ({value.x},{value.y},{value.z}). "
                        + $"Expected: ({expected.x},{expected.y},{expected.z})."
                );

                arg = new CommandArg($"{expected.x},{expected.y}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x)
                        && Approximately(expected.y, value.y)
                        && Approximately(expected.z, value.z),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y},{value.z}). Expected: ({x},{y},{z})."
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
                        Approximately(expected.x, value.x)
                            && Approximately(expected.y, value.y)
                            && Approximately(expected.z, value.z),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y}). Expected: ({x},{y})."
                    );
                }
            }

            // x,y,z (z is ok, but ignored)
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float y = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float z = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                expected = new Vector3(x, y, z);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, vector3RoundTolerance)
                        && Approximately(expected.y, value.y, vector3RoundTolerance)
                        && Approximately(expected.z, value.z, vector3RoundTolerance),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Input: ({x},{y},{z}). "
                        + $"Value: ({value.x},{value.y},{value.z}). "
                        + $"Expected: ({expected.x},{expected.y},{expected.z})."
                );

                arg = new CommandArg($"{expected.x},{expected.y},{expected.z}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x)
                        && Approximately(expected.y, value.y)
                        && Approximately(expected.z, value.z),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y},{value.z}). Expected: ({x},{y},{z})."
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
                        Approximately(expected.x, value.x)
                            && Approximately(expected.y, value.y)
                            && Approximately(expected.z, value.z),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y},{value.z}). Expected: ({x},{y},{z})."
                    );
                }
            }

            arg = new CommandArg(nameof(UnityEngine.Vector3.zero));
            expected = UnityEngine.Vector3.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector3.up));
            expected = UnityEngine.Vector3.up;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector3.left));
            expected = UnityEngine.Vector3.left;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector3.back));
            expected = UnityEngine.Vector3.back;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector3.forward));
            expected = UnityEngine.Vector3.forward;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector3.one));
            expected = UnityEngine.Vector3.one;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z),
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
        public void Uint()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out uint value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0U, value);

            arg = new CommandArg("-1");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("1.3");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            unchecked
            {
                for (int i = 0; i < NumTries; ++i)
                {
                    uint expected = (uint)_random.Next(int.MinValue, int.MaxValue);
                    arg = new CommandArg(expected.ToString());
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.AreEqual(expected, value);
                }
            }

            const long tooBig = uint.MaxValue + 1L;
            arg = new CommandArg(tooBig.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(nameof(uint.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(uint.MaxValue, value);

            arg = new CommandArg(nameof(uint.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(uint.MinValue, value);

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Ushort()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out ushort value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual((ushort)0, value);

            arg = new CommandArg("-1");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("1.3");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            unchecked
            {
                for (int i = 0; i < NumTries; ++i)
                {
                    ushort expected = (ushort)_random.Next(short.MinValue, short.MaxValue);
                    arg = new CommandArg(expected.ToString());
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.AreEqual(expected, value);
                }
            }

            const int tooBig = ushort.MaxValue + 1;
            arg = new CommandArg(tooBig.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(nameof(ushort.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(ushort.MaxValue, value);

            arg = new CommandArg(ushort.MaxValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(ushort.MaxValue, value);

            arg = new CommandArg(nameof(ushort.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(ushort.MinValue, value);

            arg = new CommandArg(ushort.MinValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(ushort.MinValue, value);

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void String()
        {
            string expected = string.Empty;
            CommandArg arg = new(expected);
            Assert.IsTrue(arg.TryGet(out string value));
            Assert.AreEqual(arg.String, value);
            Assert.AreEqual(expected, value);

            expected = "asdf";
            arg = new CommandArg(expected);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(arg.String, value);
            Assert.AreEqual(expected, value);

            expected = "1111";
            arg = new CommandArg(expected);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(arg.String, value);
            Assert.AreEqual(expected, value);

            expected = "1.3333";
            arg = new CommandArg(expected);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(arg.String, value);
            Assert.AreEqual(expected, value);

            for (int i = 0; i < NumTries; ++i)
            {
                expected = Guid.NewGuid().ToString();
                arg = new CommandArg(expected);
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(arg.String, value);
                Assert.AreEqual(expected, value);
            }

            expected = "#$$$__.azxfd87&*_&&&-={'|";
            arg = new CommandArg(expected);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(arg.String, value);
            Assert.AreEqual(expected, value);

            // Check strings aren't sanitized
            expected = "   ";
            arg = new CommandArg(expected);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(arg.String, value);
            Assert.AreEqual(expected, value);

            // Make sure string.Empty isn't resolved to ""
            expected = "Empty";
            arg = new CommandArg(expected);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(arg.String, value);
            Assert.AreEqual(expected, value);

            expected = "string.Empty";
            arg = new CommandArg(expected);
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(arg.String, value);
            Assert.AreEqual(expected, value);
        }

        [Test]
        public void Quaternion()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Quaternion value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            Quaternion expected;

            // Unexpected input
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
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

            const float quaternionRoundTolerance = 0.00001f;

            // x,y,z, w (z is ok, but ignored)
            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float y = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float z = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float w = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                expected = new Quaternion(x, y, z, w);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, quaternionRoundTolerance)
                        && Approximately(expected.y, value.y, quaternionRoundTolerance)
                        && Approximately(expected.z, value.z, quaternionRoundTolerance)
                        && Approximately(expected.w, value.w, quaternionRoundTolerance),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Input: ({x},{y},{z},{w}). "
                        + $"Value: ({value.x},{value.y},{value.z},{value.w}). "
                        + $"Expected: ({expected.x},{expected.y},{expected.z},{expected.w})."
                );

                arg = new CommandArg($"{x},{y},{z},{w}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x)
                        && Approximately(expected.y, value.y)
                        && Approximately(expected.z, value.z)
                        && Approximately(expected.w, value.w),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y},{value.z},{value.w}). "
                        + $"Expected: ({expected.x},{expected.y},{expected.z},{expected.w})."
                );

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{x},{y},{z},{w}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.IsTrue(
                        Approximately(expected.x, value.x)
                            && Approximately(expected.y, value.y)
                            && Approximately(expected.z, value.z)
                            && Approximately(expected.w, value.w),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y},{value.z},{value.w}). "
                            + $"Expected: ({expected.x},{expected.y},{expected.z},{expected.w})."
                    );
                }
            }

            arg = new CommandArg(nameof(UnityEngine.Quaternion.identity));
            expected = UnityEngine.Quaternion.identity;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z)
                    && Approximately(expected.w, value.w),
                $"Expected {value} to be approximately {expected}. "
                    + $"Value: ({value.x},{value.y},{value.z},{value.w}). "
                    + $"Expected: ({expected.x},{expected.y},{expected.z},{expected.w})."
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

            arg = new CommandArg("(1.0, 0.5)");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("(0.7)");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

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
                    Approximately(expected.r, value.r, 0.001f)
                        && Approximately(expected.g, value.g, 0.001f)
                        && Approximately(expected.b, value.b, 0.001f)
                        && Approximately(expected.a, value.a, 0.001f),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({r},{g},{b},{expected.a}). "
                        + $"Expected: ({expected.r},{expected.g},{expected.b},{expected.a})."
                );

                arg = new CommandArg($"{r},{g},{b}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.r, value.r, 0.001f)
                        && Approximately(expected.g, value.g, 0.001f)
                        && Approximately(expected.b, value.b, 0.001f)
                        && Approximately(expected.a, value.a, 0.001f),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({r},{g},{b},{expected.a}). "
                        + $"Expected: ({expected.r},{expected.g},{expected.b},{expected.a})."
                );

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{r},{g},{b}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.IsTrue(
                        Approximately(expected.r, value.r, 0.001f)
                            && Approximately(expected.g, value.g, 0.001f)
                            && Approximately(expected.b, value.b, 0.001f)
                            && Approximately(expected.a, value.a, 0.001f),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({r},{g},{b},{expected.a}). "
                            + $"Expected: ({expected.r},{expected.g},{expected.b},{expected.a})."
                    );
                }
            }

            for (int i = 0; i < NumTries; ++i)
            {
                float r = (float)_random.NextDouble();
                float g = (float)_random.NextDouble();
                float b = (float)_random.NextDouble();
                float a = (float)_random.NextDouble();
                expected = new Color(r, g, b, a);
                arg = new CommandArg(expected.ToString());

                // Colors have a floating point precision of 3 decimal places, otherwise our equality checks will be off
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.r, value.r, 0.001f)
                        && Approximately(expected.g, value.g, 0.001f)
                        && Approximately(expected.b, value.b, 0.001f)
                        && Approximately(expected.a, value.a, 0.001f),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({r},{g},{b},{a}). "
                        + $"Expected: ({expected.r},{expected.g},{expected.b},{expected.a})."
                );

                arg = new CommandArg($"{r},{g},{b},{a}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.r, value.r, 0.001f)
                        && Approximately(expected.g, value.g, 0.001f)
                        && Approximately(expected.b, value.b, 0.001f)
                        && Approximately(expected.a, value.a, 0.001f),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({r},{g},{b},{a}). "
                        + $"Expected: ({expected.r},{expected.g},{expected.b},{expected.a})."
                );

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{r},{g},{b},{a}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.IsTrue(
                        Approximately(expected.r, value.r, 0.001f)
                            && Approximately(expected.g, value.g, 0.001f)
                            && Approximately(expected.b, value.b, 0.001f)
                            && Approximately(expected.a, value.a, 0.001f),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({r},{g},{b},{a}). "
                            + $"Expected: ({expected.r},{expected.g},{expected.b},{expected.a})."
                    );
                }
            }

            arg = new CommandArg("invisible");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
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
                Approximately((float)value, 2.5f),
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
                Approximately(expected.x, 1.2564f) && Approximately(expected.y, 3.6f),
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

                    parsed = 0;
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
        public void ParserRegistration()
        {
            bool registered = CommandArg.RegisterParser<int>(CustomIntParser1);
            Assert.IsTrue(registered);
            Assert.IsTrue(CommandArg.TryGetParser(out CommandArgParser<int> registeredParser));
            Assert.AreEqual((CommandArgParser<int>)CustomIntParser1, registeredParser);

            registered = CommandArg.RegisterParser<int>(CustomIntParser1);
            Assert.IsFalse(registered);
            Assert.IsTrue(CommandArg.TryGetParser(out registeredParser));
            Assert.AreEqual((CommandArgParser<int>)CustomIntParser1, registeredParser);

            registered = CommandArg.RegisterParser<int>(CustomIntParser2);
            Assert.IsFalse(registered);
            Assert.IsTrue(CommandArg.TryGetParser(out registeredParser));
            Assert.AreEqual((CommandArgParser<int>)CustomIntParser1, registeredParser);

            registered = CommandArg.RegisterParser<int>(CustomIntParser2, force: true);
            Assert.IsTrue(registered);

            Assert.IsTrue(CommandArg.TryGetParser(out registeredParser));
            Assert.AreEqual((CommandArgParser<int>)CustomIntParser2, registeredParser);

            return;

            static bool CustomIntParser1(string input, out int parsed)
            {
                parsed = 1;
                return false;
            }

            static bool CustomIntParser2(string input, out int parsed)
            {
                parsed = 2;
                return false;
            }
        }

        [Test]
        public void ParserDeregistration()
        {
            Assert.IsFalse(CommandArg.TryGetParser(out CommandArgParser<int> registeredParser));

            bool deregistered = CommandArg.UnregisterParser<int>();
            Assert.IsFalse(deregistered);

            bool registered = CommandArg.RegisterParser<int>(CustomIntParser1);
            Assert.IsTrue(registered);
            Assert.IsTrue(CommandArg.TryGetParser(out registeredParser));

            deregistered = CommandArg.UnregisterParser<int>();
            Assert.IsTrue(deregistered);
            Assert.IsFalse(CommandArg.TryGetParser(out registeredParser));

            deregistered = CommandArg.UnregisterParser<int>();
            Assert.IsFalse(deregistered);
            Assert.IsFalse(CommandArg.TryGetParser(out registeredParser));

            return;

            static bool CustomIntParser1(string input, out int parsed)
            {
                parsed = 1;
                return false;
            }
        }

        private static bool Approximately(float a, float b, float tolerance = 0.0001f)
        {
            float delta = Math.Abs(a - b);
            // Check ToString representations too, the numbers may be crazy small or crazy big, outside the scope of our tolerance
            return delta <= tolerance
                || a.ToString(CultureInfo.InvariantCulture)
                    .Equals(b.ToString(CultureInfo.InvariantCulture));
        }

        private static bool Approximately(double a, double b, double tolerance = 0.0001)
        {
            double delta = Math.Abs(a - b);
            // Check ToString representations too, the numbers may be crazy small or crazy big, outside the scope of our tolerance
            return delta <= tolerance
                || a.ToString(CultureInfo.InvariantCulture)
                    .Equals(b.ToString(CultureInfo.InvariantCulture));
        }
    }
}
