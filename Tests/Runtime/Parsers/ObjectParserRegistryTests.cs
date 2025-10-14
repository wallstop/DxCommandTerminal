namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Parsers
{
    using Backend;
    using Backend.Parsers;
    using NUnit.Framework;

    public sealed class ObjectParserRegistryTests
    {
        [Test]
        public void ListsRegisteredTypes()
        {
            // Ensure we have at least the defaults
            CommandArg.UnregisterAllObjectParsers();
            CommandArg.RegisterObjectParser(IntArgParser.Instance, true);
            CommandArg.RegisterObjectParser(FloatArgParser.Instance, true);

            var types = CommandArg.GetRegisteredObjectParserTypes();
            CollectionAssert.Contains(types, typeof(int));
            CollectionAssert.Contains(types, typeof(float));
        }

        private sealed class CustomType { }

        private sealed class CustomTypeParser : ArgParser<CustomType>
        {
            public static readonly CustomTypeParser Instance = new();

            protected override bool TryParseTyped(string input, out CustomType value)
            {
                value = new CustomType();
                return true;
            }
        }

        [Test]
        public void RegisterAndUnregisterObjectParser()
        {
            CommandArg.UnregisterObjectParser(typeof(CustomType));
            Assert.IsTrue(CommandArg.RegisterObjectParser(CustomTypeParser.Instance, false));
            var types = CommandArg.GetRegisteredObjectParserTypes();
            CollectionAssert.Contains(types, typeof(CustomType));

            Assert.IsTrue(CommandArg.UnregisterObjectParser(typeof(CustomType)));
        }
    }
}
