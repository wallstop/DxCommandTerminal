namespace CommandTerminal.Utils
{
    using System;
    using System.Collections.Generic;

    [Serializable]
    public sealed class ListWrapper<T>
    {
        public List<T> list = new();
    }
}
