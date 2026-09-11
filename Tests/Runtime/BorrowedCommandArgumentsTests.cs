namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Backend;
    using NUnit.Framework;

    public sealed class BorrowedCommandArgumentsTests
    {
        private static BorrowedCommandArguments CreateView(object source)
        {
            /*
               The shell constructs views from arrays, lists, and arbitrary
               read-only lists; cover all three construction paths.
            */
            return source switch
            {
                CommandArg[] array => new BorrowedCommandArguments(array),
                List<CommandArg> list => new BorrowedCommandArguments(list),
                IReadOnlyList<CommandArg> readOnly => new BorrowedCommandArguments(readOnly),
                _ => throw new ArgumentException($"Unexpected source type: {source?.GetType()}"),
            };
        }

        [Test]
        public void ArrayBackedViewExposesArguments()
        {
            CommandArg[] source = { new("a"), new("b") };
            BorrowedCommandArguments view = CreateView(source);

            Assert.AreEqual(2, view.Count);
            Assert.IsFalse(view.IsEmpty);
            Assert.AreEqual("a", view[0].contents);
            Assert.AreEqual("b", view[1].contents);
            CollectionAssert.AreEqual(
                new[] { "a", "b" },
                view.Select(argument => argument.contents).ToArray()
            );
        }

        [Test]
        public void ListBackedViewExposesArguments()
        {
            List<CommandArg> source = new() { new("x"), new("y"), new("z") };
            BorrowedCommandArguments view = CreateView(source);

            Assert.AreEqual(3, view.Count);
            CollectionAssert.AreEqual(
                new[] { "x", "y", "z" },
                view.Select(argument => argument.contents).ToArray()
            );
        }

        [Test]
        public void EmptyViewIsSafe()
        {
            BorrowedCommandArguments empty = CreateView(new CommandArg[0]);

            Assert.AreEqual(0, empty.Count);
            Assert.IsTrue(empty.IsEmpty);
            CollectionAssert.IsEmpty(empty.ToArray(), "ToArray on an empty view is empty");

            List<CommandArg> enumerated = new();
            foreach (CommandArg argument in empty)
            {
                enumerated.Add(argument);
            }

            Assert.IsEmpty(enumerated);
        }

        [Test]
        public void IndexerValidatesBounds()
        {
            BorrowedCommandArguments arrayView = CreateView(new CommandArg[] { new("a") });
            BorrowedCommandArguments listView = CreateView(new List<CommandArg> { new("a") });

            Assert.Throws<ArgumentOutOfRangeException>(() => _ = arrayView[-1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = arrayView[1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = listView[-1]);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = listView[1]);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _ = default(BorrowedCommandArguments)[0],
                "A default view has no arguments"
            );
        }

        [Test]
        public void ToArrayProducesIndependentBulkCopies()
        {
            CommandArg[] arraySource = { new("keep") };
            BorrowedCommandArguments arrayView = CreateView(arraySource);
            CommandArg[] arrayCopy = arrayView.ToArray();
            Assert.AreNotSame(arraySource, arrayCopy, "Array views copy, never alias");
            Assert.AreEqual("keep", arrayCopy[0].contents);

            List<CommandArg> listSource = new() { new("one"), new("two") };
            BorrowedCommandArguments listView = CreateView(listSource);
            CommandArg[] listCopy = listView.ToArray();
            Assert.AreEqual(2, listCopy.Length);
            Assert.AreEqual("two", listCopy[1].contents);

            // Retained copies stay stable when the backing buffer is reused.
            listSource.Clear();
            listSource.Add(new CommandArg("reused"));
            Assert.AreEqual("two", listCopy[1].contents);
        }
    }
}
