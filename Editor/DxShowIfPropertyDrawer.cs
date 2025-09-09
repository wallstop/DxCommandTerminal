namespace WallstopStudios.DxCommandTerminal.Editor
{
#if UNITY_EDITOR
    using System.Reflection;
    using Attributes;
    using Extensions;
    using UnityEditor;
    using UnityEngine;

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
            if (attribute is not DxShowIfAttribute showIf)
            {
                return true;
            }

            SerializedProperty conditionProperty = property.serializedObject.FindProperty(
                showIf.conditionField
            );
            if (conditionProperty is not { propertyType: SerializedPropertyType.Boolean })
            {
                if (conditionProperty != null)
                {
                    return true;
                }

                object enclosingObject = property.GetEnclosingObject(out _);
                FieldInfo conditionField = enclosingObject
                    ?.GetType()
                    .GetField(
                        showIf.conditionField,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    );
                if (conditionField?.GetValue(enclosingObject) is bool maybeCondition)
                {
                    return showIf.inverse ? !maybeCondition : maybeCondition;
                }
                return true;
            }

            bool condition = conditionProperty.boolValue;
            return showIf.inverse ? !condition : condition;
        }
    }
#endif
}
