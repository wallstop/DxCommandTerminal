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
            DynamicMethod dynamicMethod = new(
                "Get" + field.Name,
                typeof(object),
                new[] { typeof(object) },
                field.DeclaringType,
                true
            );

            ILGenerator il = dynamicMethod.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, field.DeclaringType);
            il.Emit(OpCodes.Ldfld, field);
            if (field.FieldType.IsValueType)
            {
                il.Emit(OpCodes.Box, field.FieldType);
            }

            il.Emit(OpCodes.Ret);

            return (Func<object, object>)dynamicMethod.CreateDelegate(typeof(Func<object, object>));
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

        // Special handling for Gradients, since Unity doesn't expose gradientValue in SerializedProperty
        private static Gradient GetGradientValue(SerializedProperty property)
        {
            return GradientProperty?.GetValue(property) as Gradient;
        }
    }
#endif
}
