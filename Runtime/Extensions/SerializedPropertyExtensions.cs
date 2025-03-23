namespace CommandTerminal.Extensions
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Reflection.Emit;
    using UnityEditor;
    using UnityEngine;

    public static class FieldAccessorFactory
    {
        public static Func<object, object> CreateFieldGetter(FieldInfo field)
        {
#if WEB_GL
            return field.GetValue;
#else
            DynamicMethod dynamicMethod = new(
                $"Get{field.Name}",
                typeof(object),
                new[] { typeof(object) },
                field.DeclaringType,
                true
            );

            ILGenerator il = dynamicMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(
                field.DeclaringType.IsValueType ? OpCodes.Unbox : OpCodes.Castclass,
                field.DeclaringType
            );
            il.Emit(OpCodes.Ldfld, field);
            if (field.FieldType.IsValueType)
            {
                il.Emit(OpCodes.Box, field.FieldType);
            }

            il.Emit(OpCodes.Ret);
            return (Func<object, object>)dynamicMethod.CreateDelegate(typeof(Func<object, object>));
#endif
        }
    }

    public static class SerializedPropertyExtensions
    {
        private static readonly Dictionary<
            Type,
            Dictionary<string, Func<object, object>>
        > FieldProducersByType = new();
        private static readonly PropertyInfo GradientProperty =
            typeof(SerializedProperty).GetProperty(
                "gradientValue",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

        public static object GetValue(this SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return property.intValue;
                case SerializedPropertyType.Boolean:
                    return property.boolValue;
                case SerializedPropertyType.Float:
                    return property.floatValue;
                case SerializedPropertyType.String:
                    return property.stringValue;
                case SerializedPropertyType.Color:
                    return property.colorValue;
                case SerializedPropertyType.ObjectReference:
                    return property.objectReferenceValue;
                case SerializedPropertyType.LayerMask:
                    return (LayerMask)property.intValue;
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex;
                case SerializedPropertyType.Vector2:
                    return property.vector2Value;
                case SerializedPropertyType.Vector3:
                    return property.vector3Value;
                case SerializedPropertyType.Vector4:
                    return property.vector4Value;
                case SerializedPropertyType.Rect:
                    return property.rectValue;
                case SerializedPropertyType.Character:
                    return (char)property.intValue;
                case SerializedPropertyType.AnimationCurve:
                    return property.animationCurveValue;
                case SerializedPropertyType.Bounds:
                    return property.boundsValue;
                case SerializedPropertyType.Gradient:
                    return GetGradientValue(property);
                case SerializedPropertyType.Quaternion:
                    return property.quaternionValue;
                case SerializedPropertyType.Vector2Int:
                    return property.vector2IntValue;
                case SerializedPropertyType.Vector3Int:
                    return property.vector3IntValue;
                case SerializedPropertyType.RectInt:
                    return property.rectIntValue;
                case SerializedPropertyType.BoundsInt:
                    return property.boundsIntValue;
                case SerializedPropertyType.ManagedReference:
                    return property.managedReferenceValue;
                case SerializedPropertyType.Generic:
                {
                    object obj = property.serializedObject.targetObject;
                    string[] propertyNames = property.propertyPath.Split('.');

                    foreach (string name in propertyNames)
                    {
                        if (obj == null)
                        {
                            return null;
                        }

                        Type type = obj.GetType();
                        if (
                            !FieldProducersByType.TryGetValue(
                                type,
                                out Dictionary<string, Func<object, object>> fieldProducersByName
                            )
                        )
                        {
                            fieldProducersByName = new Dictionary<string, Func<object, object>>();
                            FieldProducersByType[type] = fieldProducersByName;
                        }

                        if (
                            !fieldProducersByName.TryGetValue(
                                name,
                                out Func<object, object> fieldProducer
                            )
                        )
                        {
                            FieldInfo field = type.GetField(
                                name,
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                            );
                            fieldProducer =
                                field == null
                                    ? _ => null
                                    : FieldAccessorFactory.CreateFieldGetter(field);
                            fieldProducersByName[name] = fieldProducer;
                        }

                        obj = fieldProducer(obj);
                    }

                    return obj;
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// Gets the instance object that contains the given SerializedProperty.
        /// </summary>
        /// <param name="property">The SerializedProperty.</param>
        /// <param name="fieldInfo">Outputs the FieldInfo of the referenced field.</param>
        /// <returns>The instance object that owns the field.</returns>
        public static object GetEnclosingObject(
            this SerializedProperty property,
            out FieldInfo fieldInfo
        )
        {
            fieldInfo = null;
            object obj = property.serializedObject.targetObject;
            if (obj == null)
            {
                return null;
            }
            Type type = obj.GetType();
            string[] pathParts = property.propertyPath.Split('.');

            // Traverse the path but stop at the second-to-last field
            for (int i = 0; i < pathParts.Length - 1; ++i)
            {
                string fieldName = pathParts[i];

                if (fieldName == "Array")
                {
                    // Move to "data[i]"
                    ++i;
                    if (pathParts.Length <= i)
                    {
                        break;
                    }

                    int index = int.Parse(pathParts[i].Replace("data[", "").Replace("]", ""));
                    obj = GetElementAtIndex(obj, index);
                    type = obj?.GetType();
                    continue;
                }

                fieldInfo = type?.GetField(
                    fieldName,
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
                );
                if (fieldInfo == null)
                {
                    return null;
                }

                // Move deeper but stop before the last property in the path
                if (i < pathParts.Length - 2)
                {
                    obj = fieldInfo.GetValue(obj);
                    type = fieldInfo.FieldType;
                }
            }

            return obj;
        }

        private static object GetElementAtIndex(object obj, int index)
        {
            if (obj is System.Collections.IList list && index >= 0 && index < list.Count)
            {
                return list[index];
            }
            return null;
        }

        // Special handling for Gradients, since Unity doesn't expose gradientValue in SerializedProperty
        private static Gradient GetGradientValue(SerializedProperty property)
        {
            return GradientProperty?.GetValue(property) as Gradient;
        }
    }
#endif
}
