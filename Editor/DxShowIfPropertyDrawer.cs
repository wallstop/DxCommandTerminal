namespace CommandTerminal.Editor
{
#if UNITY_EDITOR
    using Attributes;
    using UnityEngine;
    using UnityEditor;

    [CustomPropertyDrawer(typeof(DxShowIfAttribute))]
    public sealed class DxShowIfPropertyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return !ShouldShow(property) ? 0f : EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (ShouldShow(property))
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        private bool ShouldShow(SerializedProperty property)
        {
            DxShowIfAttribute dxShowIf = (DxShowIfAttribute)attribute;
            SerializedProperty conditionProperty = property.serializedObject.FindProperty(
                dxShowIf.conditionField
            );
            if (conditionProperty is { propertyType: SerializedPropertyType.Boolean })
            {
                bool condition = conditionProperty.boolValue;
                return dxShowIf.inverse ? !condition : condition;
            }
            return true;
        }
    }

#endif
}
