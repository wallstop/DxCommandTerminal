namespace WallstopStudios.DxCommandTerminal.Attributes
{
    using UnityEngine;

    public sealed class DxShowIfAttribute : PropertyAttribute
    {
        public readonly string conditionField;
        public readonly bool inverse;

        public DxShowIfAttribute(string conditionField, bool inverse = false)
        {
            this.conditionField = conditionField;
            this.inverse = inverse;
        }
    }
}
