namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
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

        [Test]
        public void SliceExposesTailWindowOnEveryBackingStorage()
        {
            CommandArg[] arraySource = { new("head"), new("one"), new("two") };
            List<CommandArg> listSource = new(arraySource);
            ListWrapper fallbackSource = new(arraySource);

            foreach (
                BorrowedCommandArguments view in new[]
                {
                    CreateView(arraySource),
                    CreateView(listSource),
                    CreateView(fallbackSource),
                }
            )
            {
                BorrowedCommandArguments tail = view.Slice(1);

                Assert.AreEqual(2, tail.Count, "The slice window excludes the offset");
                CollectionAssert.AreEqual(
                    new[] { "one", "two" },
                    tail.Select(argument => argument.contents).ToArray()
                );
                Assert.AreEqual("one", tail[0].contents);
                Assert.AreEqual("two", tail[1].contents);

                CommandArg[] copy = tail.ToArray();
                Assert.AreEqual(2, copy.Length, "ToArray copies only the slice window");
                Assert.AreEqual("two", copy[1].contents);
            }
        }

        [Test]
        public void SliceIsPureViewAndOriginalStaysWhole()
        {
            List<CommandArg> source = new() { new("head"), new("one"), new("two") };
            BorrowedCommandArguments view = CreateView(source);
            BorrowedCommandArguments tail = view.Slice(1);

            Assert.AreEqual(3, view.Count, "The original view keeps its full window");
            Assert.AreEqual(2, tail.Count);

            source[1] = new CommandArg("changed");
            Assert.AreEqual(
                "changed",
                tail[0].contents,
                "The slice aliases the same backing storage"
            );

            List<CommandArg> enumerated = new();
            foreach (CommandArg argument in tail)
            {
                enumerated.Add(argument);
            }

            Assert.AreEqual(2, enumerated.Count, "Enumeration covers only the slice window");
        }

        [Test]
        public void SliceOfSliceComposes()
        {
            List<CommandArg> source = new() { new("a"), new("b"), new("c"), new("d") };
            BorrowedCommandArguments view = CreateView(source);

            BorrowedCommandArguments middle = view.Slice(1).Slice(1);

            Assert.AreEqual(2, middle.Count);
            Assert.AreEqual("c", middle[0].contents);
            Assert.AreEqual("d", middle[1].contents);

            BorrowedCommandArguments tail = view.Slice(1).Slice(2);

            Assert.AreEqual(1, tail.Count, "Nested slices compose their offsets");
            Assert.AreEqual("d", tail[0].contents);
        }

        [Test]
        public void SliceValidatesOffsets()
        {
            BorrowedCommandArguments view = CreateView(new CommandArg[] { new("a") });

            Assert.Throws<ArgumentOutOfRangeException>(() => view.Slice(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => view.Slice(2));

            BorrowedCommandArguments empty = view.Slice(1);
            Assert.AreEqual(0, empty.Count, "Slicing at the end is an empty window");
            Assert.IsTrue(empty.IsEmpty);
        }

        private sealed class ListWrapper : IReadOnlyList<CommandArg>
        {
            public CommandArg this[int index] => _inner[index];

            public int Count => _inner.Count;

            private readonly IReadOnlyList<CommandArg> _inner;

            public ListWrapper(IReadOnlyList<CommandArg> inner)
            {
                _inner = inner;
            }

            public IEnumerator<CommandArg> GetEnumerator() => _inner.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => _inner.GetEnumerator();
        }
    }
}
