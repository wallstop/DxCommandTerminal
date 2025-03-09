namespace CommandTerminal.Editor
{
#if UNITY_EDITOR
    using Attributes;
    using UnityEngine;
    using UnityEditor;

    [CustomPropertyDrawer(typeof(ShowIfAttribute))]
    public sealed class ShowIfPropertyDrawer : PropertyDrawer
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
            ShowIfAttribute showIf = (ShowIfAttribute)attribute;
            SerializedProperty conditionProperty = property.serializedObject.FindProperty(
                showIf.conditionField
            );
            if (conditionProperty is { propertyType: SerializedPropertyType.Boolean })
            {
                bool condition = conditionProperty.boolValue;
                return showIf.inverse ? !condition : condition;
            }
            return true;
        }
    }

#endif
}
