﻿namespace DxCommandTerminal.Tests.Tests.Runtime
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
                Debug.Log(
                    $"Unregistered {unregistered} parser{(unregistered == 1 ? string.Empty : "s")}."
                );
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Decimal()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out decimal value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(new decimal(0.0), value);
            arg = new CommandArg("1");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(new decimal(1), value),
                $"Expected {value} to be equal to {1.0}"
            );
            arg = new CommandArg("3");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(new decimal(3), value),
                $"Expected {value} to be equal to {3.0}"
            );
            arg = new CommandArg("-100");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(new decimal(-100), value),
                $"Expected {value} to be equal to {-100.0}"
            );

            for (int i = 0; i < NumTries; ++i)
            {
                double expectedDouble =
                    _random.NextDouble() * _random.Next(int.MinValue, int.MaxValue);
                decimal expected = new(expectedDouble);
                arg = new CommandArg(expected.ToString(CultureInfo.InvariantCulture));
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(Approximately(expected, value), $"{expected} not equal to {value}");
            }

            arg = new CommandArg(nameof(decimal.Zero));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.Zero, value);
            arg = new CommandArg(decimal.Zero.ToString(CultureInfo.InvariantCulture));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.Zero, value);

            arg = new CommandArg(nameof(decimal.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.MaxValue, value);
            arg = new CommandArg(decimal.MaxValue.ToString(CultureInfo.InvariantCulture));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.MaxValue, value);

            arg = new CommandArg(nameof(decimal.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.MinValue, value);
            arg = new CommandArg(decimal.MinValue.ToString(CultureInfo.InvariantCulture));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.MinValue, value);

            arg = new CommandArg(nameof(decimal.One));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.One, value);
            arg = new CommandArg(decimal.One.ToString(CultureInfo.InvariantCulture));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.One, value);

            arg = new CommandArg(nameof(decimal.MinusOne));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.MinusOne, value);
            arg = new CommandArg(decimal.MinusOne.ToString(CultureInfo.InvariantCulture));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(decimal.MinusOne, value);

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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
            Assert.IsTrue(Approximately(1.0, value), $"Expected {value} to be equal to {1.0}");
            arg = new CommandArg("3");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(Approximately(3.0, value), $"Expected {value} to be equal to {3.0}");
            arg = new CommandArg("-100");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(-100.0, value),
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("     ");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void DateTimeOffset()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out DateTimeOffset value), $"Unexpectedly parsed {value}");

            byte[] longBytes = new byte[sizeof(long)];
            for (int i = 0; i < NumTries; ++i)
            {
                long ticks;
                do
                {
                    _random.NextBytes(longBytes);
                    ticks = BitConverter.ToInt64(longBytes, 0);
                } while (
                    ticks < System.DateTime.MinValue.Ticks || System.DateTime.MaxValue.Ticks < ticks
                );

                DateTimeOffset expected = new(ticks, TimeSpan.Zero);
                arg = new CommandArg(expected.ToString("O"));
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            DateTimeOffset now = System.DateTimeOffset.Now;
            arg = new CommandArg(now.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(now, value);

            DateTimeOffset utcNow = System.DateTimeOffset.UtcNow;
            arg = new CommandArg(utcNow.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(utcNow, value);

            /*
                Don't validate these, as they're mutable and might change between time of
                generation and time of check, all we need to know is if they're parsable
             */
            arg = new CommandArg(nameof(System.DateTimeOffset.Now));
            Assert.IsTrue(arg.TryGet(out value));
            arg = new CommandArg(nameof(System.DateTimeOffset.UtcNow));
            Assert.IsTrue(arg.TryGet(out value));

            arg = new CommandArg(nameof(System.DateTimeOffset.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTimeOffset.MaxValue, value);

            arg = new CommandArg(System.DateTimeOffset.MaxValue.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTimeOffset.MaxValue, value);

            arg = new CommandArg(nameof(System.DateTimeOffset.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTimeOffset.MinValue, value);

            arg = new CommandArg(System.DateTimeOffset.MinValue.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTimeOffset.MinValue, value);

            arg = new CommandArg(nameof(System.DateTimeOffset.UnixEpoch));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTimeOffset.UnixEpoch, value);

            arg = new CommandArg(System.DateTimeOffset.UnixEpoch.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTimeOffset.UnixEpoch, value);

            arg = new CommandArg("00");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void DateTime()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out DateTime value), $"Unexpectedly parsed {value}");

            DateTimeKind[] kinds = System
                .Enum.GetValues(typeof(DateTimeKind))
                .OfType<DateTimeKind>()
                .ToArray();

            byte[] longBytes = new byte[sizeof(long)];
            for (int i = 0; i < NumTries; ++i)
            {
                DateTimeKind kind = kinds[_random.Next(0, kinds.Length)];
                long ticks;
                do
                {
                    _random.NextBytes(longBytes);
                    ticks = BitConverter.ToInt64(longBytes, 0);
                } while (
                    ticks < System.DateTime.MinValue.Ticks || System.DateTime.MaxValue.Ticks < ticks
                );

                DateTime expected = new(ticks, kind);
                arg = new CommandArg(expected.ToString("O"));
                Assert.IsTrue(arg.TryGet(out value));
                if (kind == DateTimeKind.Utc)
                {
                    Assert.AreEqual(expected.ToLocalTime(), value);
                }
                else
                {
                    if (!expected.Equals(value))
                    {
                        Assert.IsTrue(
                            Math.Abs(expected.Hour - value.Hour) <= 1,
                            $"Failed to parse - expected: {expected}, parsed: {value}"
                        );
                    }
                    else
                    {
                        Assert.AreEqual(
                            expected,
                            value,
                            $"Failed to parse - expected is using {expected.Kind}, parsed is using {value.Kind}"
                        );
                    }
                }
            }

            DateTime now = System.DateTime.Now;
            arg = new CommandArg(now.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(now, value);

            DateTime utcNow = System.DateTime.UtcNow;
            arg = new CommandArg(utcNow.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(utcNow.ToLocalTime(), value);

            /*
                Don't validate these, as they're mutable and might change between time of
                generation and time of check, all we need to know is if they're parsable
             */
            arg = new CommandArg(nameof(System.DateTime.Now));
            Assert.IsTrue(arg.TryGet(out value));
            arg = new CommandArg(nameof(System.DateTime.UtcNow));
            Assert.IsTrue(arg.TryGet(out value));
            arg = new CommandArg(nameof(System.DateTime.Today));
            Assert.IsTrue(arg.TryGet(out value));

            arg = new CommandArg(nameof(System.DateTime.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTime.MaxValue, value);

            arg = new CommandArg(System.DateTime.MaxValue.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTime.MaxValue, value);

            arg = new CommandArg(nameof(System.DateTime.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTime.MinValue, value);

            arg = new CommandArg(System.DateTime.MinValue.ToString("O"));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.DateTime.MinValue, value);

            arg = new CommandArg("00");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Guid()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Guid value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(System.Guid.Empty.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.Guid.Empty, value);

            for (int i = 0; i < NumTries; ++i)
            {
                Guid expected = System.Guid.NewGuid();
                arg = new CommandArg(
                    _random.Next() % 2 == 0
                        ? expected.ToString().ToLower()
                        : expected.ToString().ToUpper()
                );
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            arg = new CommandArg(nameof(System.Guid.Empty));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(System.Guid.Empty, value);

            arg = new CommandArg("00");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Char()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out char value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual('0', value);

            for (int i = 0; i < NumTries; ++i)
            {
                char expected = (char)_random.Next(char.MinValue, char.MaxValue);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(
                    arg.TryGet(out value),
                    $"Failed to parse {expected} as char. Cleaned arg: '{arg.CleanedString}'"
                );
                Assert.AreEqual(expected, value);
            }

            arg = new CommandArg("00");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("z");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(arg.String[0], value);

            arg = new CommandArg(nameof(char.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(char.MaxValue, value);

            arg = new CommandArg(char.MaxValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(char.MaxValue, value);

            arg = new CommandArg(nameof(char.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(char.MinValue, value);

            arg = new CommandArg(char.MinValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(char.MinValue, value);

            const long tooBig = char.MaxValue + 1L;
            arg = new CommandArg(tooBig.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            const long tooSmall = char.MinValue - 1L;
            arg = new CommandArg(tooSmall.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Sbyte()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out sbyte value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0, value);

            for (int i = 0; i < NumTries; ++i)
            {
                sbyte expected = (sbyte)_random.Next(sbyte.MinValue, sbyte.MaxValue);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            arg = new CommandArg(nameof(sbyte.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(sbyte.MaxValue, value);

            arg = new CommandArg(sbyte.MaxValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(sbyte.MaxValue, value);

            arg = new CommandArg(nameof(sbyte.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(sbyte.MinValue, value);

            arg = new CommandArg(sbyte.MinValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(sbyte.MinValue, value);

            long tooBig = sbyte.MaxValue + 1L;
            arg = new CommandArg(tooBig.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            long tooSmall = sbyte.MinValue - 1L;
            arg = new CommandArg(tooSmall.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Byte()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out byte value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0, value);

            for (int i = 0; i < NumTries; ++i)
            {
                byte expected = (byte)_random.Next(byte.MinValue, byte.MaxValue);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            arg = new CommandArg(nameof(byte.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(byte.MaxValue, value);

            arg = new CommandArg(byte.MaxValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(byte.MaxValue, value);

            arg = new CommandArg(nameof(byte.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(byte.MinValue, value);

            arg = new CommandArg(byte.MinValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(byte.MinValue, value);

            const long tooBig = byte.MaxValue + 1L;
            arg = new CommandArg(tooBig.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            const long tooSmall = byte.MinValue - 1L;
            arg = new CommandArg(tooSmall.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Short()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out short value), $"Unexpectedly parsed {value}");

            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0, value);

            for (int i = 0; i < NumTries; ++i)
            {
                short expected = (short)_random.Next(short.MinValue, short.MaxValue);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);
            }

            arg = new CommandArg(nameof(short.MaxValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(short.MaxValue, value);

            arg = new CommandArg(short.MaxValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(short.MaxValue, value);

            arg = new CommandArg(nameof(short.MinValue));
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(short.MinValue, value);

            arg = new CommandArg(short.MinValue.ToString());
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(short.MinValue, value);

            const long tooBig = short.MaxValue + 1L;
            arg = new CommandArg(tooBig.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            const long tooSmall = short.MinValue - 1L;
            arg = new CommandArg(tooSmall.ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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
                arg = new CommandArg(expected.ToString("G"));
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
                arg = new CommandArg(expected.ToString("G"));
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Vector2Int()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Vector2Int value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            Vector2Int expected;

            // Unexpected input
            for (int i = 0; i < NumTries; ++i)
            {
                int x = _random.Next(short.MinValue, short.MaxValue);
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
                int x = _random.Next(int.MinValue, int.MaxValue);
                int y = _random.Next(int.MinValue, int.MaxValue);
                expected = new Vector2Int(x, y);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                arg = new CommandArg($"{expected.x},{expected.y}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{expected.x},{expected.y}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.AreEqual(expected, value);
                }
            }

            // x,y, z
            for (int i = 0; i < NumTries; ++i)
            {
                int x = _random.Next(int.MinValue, int.MaxValue);
                int y = _random.Next(int.MinValue, int.MaxValue);
                int z = _random.Next(int.MinValue, int.MaxValue);
                expected = new Vector2Int(x, y);

                arg = new CommandArg($"{expected.x},{expected.y},{z}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{expected.x},{expected.y},{z}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.AreEqual(expected, value);
                }
            }

            arg = new CommandArg(nameof(UnityEngine.Vector2Int.zero));
            expected = UnityEngine.Vector2Int.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Vector2Int.zero.ToString());
            expected = UnityEngine.Vector2Int.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(nameof(UnityEngine.Vector2Int.up));
            expected = UnityEngine.Vector2Int.up;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Vector2Int.up.ToString());
            expected = UnityEngine.Vector2Int.up;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(nameof(UnityEngine.Vector2Int.left));
            expected = UnityEngine.Vector2Int.left;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Vector2Int.left.ToString());
            expected = UnityEngine.Vector2Int.left;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Vector3Int()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Vector3Int value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            Vector3Int expected;

            // Unexpected input
            for (int i = 0; i < NumTries; ++i)
            {
                int x = _random.Next(short.MinValue, short.MaxValue);
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
                int x = _random.Next(int.MinValue, int.MaxValue);
                int y = _random.Next(int.MinValue, int.MaxValue);
                expected = new Vector3Int(x, y);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                arg = new CommandArg($"{expected.x},{expected.y}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{expected.x},{expected.y}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.AreEqual(expected, value);
                }
            }

            // x,y,z
            for (int i = 0; i < NumTries; ++i)
            {
                int x = _random.Next(int.MinValue, int.MaxValue);
                int y = _random.Next(int.MinValue, int.MaxValue);
                int z = _random.Next(int.MinValue, int.MaxValue);
                expected = new Vector3Int(x, y, z);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                arg = new CommandArg($"{expected.x},{expected.y},{expected.z}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg($"{pre}{expected.x},{expected.y},{expected.z}{post}");
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.AreEqual(expected, value);
                }
            }

            arg = new CommandArg(nameof(UnityEngine.Vector3Int.zero));
            expected = UnityEngine.Vector3Int.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Vector3Int.zero.ToString());
            expected = UnityEngine.Vector3Int.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(nameof(UnityEngine.Vector3Int.up));
            expected = UnityEngine.Vector3Int.up;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Vector3Int.up.ToString());
            expected = UnityEngine.Vector3Int.up;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(nameof(UnityEngine.Vector3Int.left));
            expected = UnityEngine.Vector3Int.left;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Vector3Int.left.ToString());
            expected = UnityEngine.Vector3Int.left;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(nameof(UnityEngine.Vector3Int.forward));
            expected = UnityEngine.Vector3Int.forward;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Vector3Int.forward.ToString());
            expected = UnityEngine.Vector3Int.forward;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(nameof(UnityEngine.Vector3Int.one));
            expected = UnityEngine.Vector3Int.one;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Vector3Int.one.ToString());
            expected = UnityEngine.Vector3Int.one;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Rect()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Rect value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            Rect expected;

            // Unexpected input
            for (int i = 0; i < NumTries; ++i)
            {
                int x = _random.Next(short.MinValue, short.MaxValue);
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

            const float rectTolerance = 0.01f;

            for (int i = 0; i < NumTries; ++i)
            {
                float x = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float y = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float width = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                float height = (float)(
                    _random.NextDouble() * _random.Next(short.MinValue, short.MaxValue)
                );
                expected = new Rect(x, y, width, height);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, rectTolerance)
                        && Approximately(expected.y, value.y, rectTolerance)
                        && Approximately(expected.width, value.width, rectTolerance)
                        && Approximately(expected.height, value.height, rectTolerance)
                );

                arg = new CommandArg(
                    $"{expected.x},{expected.y},{expected.width},{expected.height}"
                );
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, rectTolerance)
                        && Approximately(expected.y, value.y, rectTolerance)
                        && Approximately(expected.width, value.width, rectTolerance)
                        && Approximately(expected.height, value.height, rectTolerance)
                );

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg(
                        $"{pre}{expected.x},{expected.y},{expected.width},{expected.height}{post}"
                    );
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.IsTrue(
                        Approximately(expected.x, value.x, rectTolerance)
                            && Approximately(expected.y, value.y, rectTolerance)
                            && Approximately(expected.width, value.width, rectTolerance)
                            && Approximately(expected.height, value.height, rectTolerance)
                    );
                }
            }

            arg = new CommandArg(nameof(UnityEngine.Rect.zero));
            expected = UnityEngine.Rect.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);
            arg = new CommandArg(UnityEngine.Rect.zero.ToString());
            expected = UnityEngine.Rect.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(expected, value);

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void RectInt()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out RectInt value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            RectInt expected;

            // Unexpected input
            for (int i = 0; i < NumTries; ++i)
            {
                int x = _random.Next(short.MinValue, short.MaxValue);
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

            for (int i = 0; i < NumTries; ++i)
            {
                int x = _random.Next(int.MinValue, int.MaxValue);
                int y = _random.Next(int.MinValue, int.MaxValue);
                int width = _random.Next(int.MinValue, int.MaxValue);
                int height = _random.Next(int.MinValue, int.MaxValue);
                expected = new RectInt(x, y, width, height);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                arg = new CommandArg(
                    $"{expected.x},{expected.y},{expected.width},{expected.height}"
                );
                Assert.IsTrue(arg.TryGet(out value));
                Assert.AreEqual(expected, value);

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg(
                        $"{pre}{expected.x},{expected.y},{expected.width},{expected.height}{post}"
                    );
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.AreEqual(expected, value);
                }
            }

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");
        }

        [Test]
        public void Vector4()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out Vector4 value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsFalse(arg.TryGet(out value), $"Unexpectedly parsed {value}");

            Vector4 expected;

            const float vector4RoundTolerance = 0.01f;

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
                float w = 0f;
                expected = new Vector4(x, y);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, vector4RoundTolerance)
                        && Approximately(expected.y, value.y, vector4RoundTolerance)
                        && Approximately(expected.z, value.z, vector4RoundTolerance)
                        && Approximately(expected.w, value.w, vector4RoundTolerance),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Input: ({x},{y},{z},{w}). "
                        + $"Value: ({value.x},{value.y},{value.z},{value.w}). "
                        + $"Expected: ({expected.x},{expected.y},{expected.z},{expected.w})."
                );

                arg = new CommandArg($"{expected.x},{expected.y}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x)
                        && Approximately(expected.y, value.y)
                        && Approximately(expected.z, value.z)
                        && Approximately(expected.w, value.w),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y},{value.z},{value.w}). Expected: ({x},{y},{z},{w})."
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
                            && Approximately(expected.z, value.z)
                            && Approximately(expected.w, value.w),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y},{value.z},{value.w}). Expected: ({x},{y},{z},{w})."
                    );
                }
            }

            // x,y,z
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
                float w = 0f;
                expected = new Vector4(x, y, z);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, vector4RoundTolerance)
                        && Approximately(expected.y, value.y, vector4RoundTolerance)
                        && Approximately(expected.z, value.z, vector4RoundTolerance)
                        && Approximately(expected.w, value.w, vector4RoundTolerance),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Input: ({x},{y},{z},{w}). "
                        + $"Value: ({value.x},{value.y},{value.z},{value.w}). "
                        + $"Expected: ({expected.x},{expected.y},{expected.z},{expected.w})."
                );

                arg = new CommandArg($"{expected.x},{expected.y},{expected.z}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x)
                        && Approximately(expected.y, value.y)
                        && Approximately(expected.z, value.z)
                        && Approximately(expected.w, value.w),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y},{value.z},{value.w}). Expected: ({x},{y},{z},{w})."
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
                            && Approximately(expected.z, value.z)
                            && Approximately(expected.w, value.w),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y},{value.z},{value.w}). Expected: ({x},{y},{z},{w})."
                    );
                }
            }

            // x,y,z,w
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
                expected = new Vector4(x, y, z, w);
                arg = new CommandArg(expected.ToString());
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x, vector4RoundTolerance)
                        && Approximately(expected.y, value.y, vector4RoundTolerance)
                        && Approximately(expected.z, value.z, vector4RoundTolerance)
                        && Approximately(expected.w, value.w, vector4RoundTolerance),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Input: ({x},{y},{z},{w}). "
                        + $"Value: ({value.x},{value.y},{value.z},{value.w}). "
                        + $"Expected: ({expected.x},{expected.y},{expected.z},{expected.w})."
                );

                arg = new CommandArg($"{expected.x},{expected.y},{expected.z},{expected.w}");
                Assert.IsTrue(arg.TryGet(out value));
                Assert.IsTrue(
                    Approximately(expected.x, value.x)
                        && Approximately(expected.y, value.y)
                        && Approximately(expected.z, value.z)
                        && Approximately(expected.w, value.w),
                    $"Expected {value} to be approximately {expected}. "
                        + $"Value: ({value.x},{value.y},{value.z},{value.w}). Expected: ({x},{y},{z},{w})."
                );

                foreach (
                    (string pre, string post) in _prepend.Zip(
                        _append,
                        (preValue, postValue) => (preValue, postValue)
                    )
                )
                {
                    arg = new CommandArg(
                        $"{pre}{expected.x},{expected.y},{expected.z},{expected.w}{post}"
                    );
                    Assert.IsTrue(arg.TryGet(out value));
                    Assert.IsTrue(
                        Approximately(expected.x, value.x)
                            && Approximately(expected.y, value.y)
                            && Approximately(expected.z, value.z)
                            && Approximately(expected.w, value.w),
                        $"Expected {value} to be approximately {expected}. "
                            + $"Value: ({value.x},{value.y},{value.z},{value.w}). Expected: ({x},{y},{z},{w})."
                    );
                }
            }

            arg = new CommandArg(nameof(UnityEngine.Vector4.zero));
            expected = UnityEngine.Vector4.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z)
                    && Approximately(expected.w, value.w),
                $"Expected {value} to be approximately {expected}."
            );
            arg = new CommandArg(UnityEngine.Vector4.zero.ToString());
            expected = UnityEngine.Vector4.zero;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z)
                    && Approximately(expected.w, value.w),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector4.negativeInfinity));
            expected = UnityEngine.Vector4.negativeInfinity;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z)
                    && Approximately(expected.w, value.w),
                $"Expected {value} to be approximately {expected}."
            );
            arg = new CommandArg(UnityEngine.Vector4.negativeInfinity.ToString());
            expected = UnityEngine.Vector4.negativeInfinity;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z)
                    && Approximately(expected.w, value.w),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(nameof(UnityEngine.Vector4.positiveInfinity));
            expected = UnityEngine.Vector4.positiveInfinity;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z)
                    && Approximately(expected.w, value.w),
                $"Expected {value} to be approximately {expected}."
            );
            arg = new CommandArg(UnityEngine.Vector4.positiveInfinity.ToString());
            expected = UnityEngine.Vector4.positiveInfinity;
            Assert.IsTrue(arg.TryGet(out value));
            Assert.IsTrue(
                Approximately(expected.x, value.x)
                    && Approximately(expected.y, value.y)
                    && Approximately(expected.z, value.z)
                    && Approximately(expected.w, value.w),
                $"Expected {value} to be approximately {expected}."
            );

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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
                expected = System.Guid.NewGuid().ToString();
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

            arg = new CommandArg(System.Guid.NewGuid().ToString());
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
            arg = new CommandArg(System.Guid.NewGuid().ToString());
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
                string expectedString = System.Guid.NewGuid().ToString();
                int expected = _random.Next(int.MinValue, int.MaxValue);
                CommandArg arg = new(expectedString);
                Assert.IsFalse(arg.TryGet(out int value));
                Assert.IsTrue(arg.TryGet(out value, CustomParser));
                Assert.AreEqual(expected, value);

                // Make sure the parser isn't sticky
                Assert.IsFalse(arg.TryGet(out value));

                arg = new CommandArg(System.Guid.NewGuid().ToString());
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
                string expectedString = System.Guid.NewGuid().ToString();
                TestStruct1 expected = new(System.Guid.NewGuid());
                CommandArg arg = new(expectedString);
                Assert.IsFalse(arg.TryGet(out TestStruct1 value));
                Assert.IsTrue(arg.TryGet(out value, CustomParser));
                Assert.AreEqual(expected, value);

                // Make sure the parser isn't sticky
                Assert.IsFalse(arg.TryGet(out value));

                arg = new CommandArg(System.Guid.NewGuid().ToString());
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
        public void RegisteredParsersAreUsed()
        {
            const int constParsed = -23;
            bool registered = CommandArg.RegisterParser<int>(CustomIntParser);
            Assert.IsTrue(registered);

            CommandArg arg = new("Garbage");
            RunRegisteredParsingLogic();

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            RunRegisteredParsingLogic();

            arg = new CommandArg("");
            RunRegisteredParsingLogic();

            arg = new CommandArg("x_YZZZZ$$$");
            RunRegisteredParsingLogic();

            bool unregistered = CommandArg.UnregisterParser<int>();
            Assert.IsTrue(unregistered);

            arg = new CommandArg("Garbage");
            RunUnregisteredParsingLogic();

            arg = new CommandArg(System.Guid.NewGuid().ToString());
            RunUnregisteredParsingLogic();

            arg = new CommandArg("");
            RunUnregisteredParsingLogic();

            arg = new CommandArg("x_YZZZZ$$$");
            RunUnregisteredParsingLogic();

            return;

            void RunRegisteredParsingLogic()
            {
                Assert.IsTrue(arg.TryGet(typeof(int), out object value));
                Assert.AreEqual(constParsed, value);
                Assert.IsTrue(arg.TryGet(out int parsed));
                Assert.AreEqual(constParsed, parsed);
                Assert.IsFalse(arg.TryGet(out float _));
            }

            void RunUnregisteredParsingLogic()
            {
                Assert.IsFalse(arg.TryGet(typeof(int), out object _));
                Assert.IsFalse(arg.TryGet(out int _));
                Assert.IsFalse(arg.TryGet(out float _));
            }

            static bool CustomIntParser(string input, out int parsed)
            {
                parsed = constParsed;
                return true;
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
        public void NullParserRegistration()
        {
            bool registered = CommandArg.RegisterParser<int>(null);
            Assert.IsFalse(registered);
            registered = CommandArg.RegisterParser<int>(null, force: true);
            Assert.IsFalse(registered);
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
            Assert.IsNotNull(registeredParser);

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

        private static bool Approximately(decimal a, decimal b, decimal? tolerance = null)
        {
            if (a == b)
            {
                return true;
            }

            tolerance ??= new decimal(0.0001);

            decimal delta = Math.Abs(a - b);
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
