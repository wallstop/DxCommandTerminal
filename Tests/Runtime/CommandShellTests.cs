namespace DxCommandTerminal.Tests.Tests.Runtime
{
    using System;
    using System.Globalization;
    using CommandTerminal;
    using NUnit.Framework;
    using UnityEngine;
    using Random = System.Random;

    public sealed class CommandShellTests
    {
        private const int NumTries = 100;

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

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value));
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value));
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value));
        }

        [Test]
        public void Double()
        {
            CommandArg arg = new("");
            Assert.IsFalse(arg.TryGet(out double value), $"Unexpectedly parsed {value}");
            arg = new CommandArg("0");
            Assert.IsTrue(arg.TryGet(out value));
            Assert.AreEqual(0f, value);

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

            arg = new CommandArg(Guid.NewGuid().ToString());
            Assert.IsFalse(arg.TryGet(out value));
            arg = new CommandArg("false");
            Assert.IsFalse(arg.TryGet(out value));
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value));
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
            Assert.IsFalse(arg.TryGet(out value));
            arg = new CommandArg("     ");
            Assert.IsFalse(arg.TryGet(out value));
            arg = new CommandArg("asdf");
            Assert.IsFalse(arg.TryGet(out value));
        }
    }
}
