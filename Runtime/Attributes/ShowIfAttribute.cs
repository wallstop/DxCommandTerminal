namespace Attributes
{
    using UnityEngine;

    public sealed class ShowIfAttribute : PropertyAttribute
    {
        public readonly string conditionField;
        public readonly bool inverse;

        public ShowIfAttribute(string conditionField, bool inverse = false)
        {
            this.conditionField = conditionField;
            this.inverse = inverse;
        }
    }
}
