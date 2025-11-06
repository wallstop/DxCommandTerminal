namespace WallstopStudios.DxCommandTerminal.Internal
{
    using System;
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Lightweight ScriptableObject-based singleton loader that searches Resources
    /// using an optional [ScriptableSingletonPath("...")] resources subfolder.
    /// Includes editor fallbacks and keeps null-safe lazy initialization.
    /// </summary>
    /// <typeparam name="T">Concrete ScriptableObject singleton type.</typeparam>
    public abstract class ScriptableObjectSingleton<T> : ScriptableObject
        where T : ScriptableObjectSingleton<T>
    {
        private static Lazy<T> _lazy = CreateLazy();

        public static T Instance => _lazy.Value;

        public static bool HasInstance => _lazy.IsValueCreated && _lazy.Value != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void ClearInstance()
        {
            if (!_lazy.IsValueCreated)
            {
                return;
            }

            if (_lazy.Value != null)
            {
                Destroy(_lazy.Value);
            }
            _lazy = CreateLazy();
        }

        private static string GetResourcesPath()
        {
            Type type = typeof(T);
            ScriptableSingletonPathAttribute attr =
                Attribute.GetCustomAttribute(type, typeof(ScriptableSingletonPathAttribute))
                as ScriptableSingletonPathAttribute;
            if (attr != null && !string.IsNullOrWhiteSpace(attr.resourcesPath))
            {
                return attr.resourcesPath;
            }
            return string.Empty;
        }

        private static Lazy<T> CreateLazy()
        {
            return new Lazy<T>(() =>
            {
                Type type = typeof(T);
                string path = GetResourcesPath();

                // Primary: search in the specified subfolder (or root if empty)
                T[] instances = Resources.LoadAll<T>(path);

                // Fallback: try an exact-name load from root
                if (instances == null || instances.Length == 0)
                {
                    T named = Resources.Load<T>(type.Name);
                    if (named != null)
                    {
                        instances = new[] { named };
                    }
                }

                // Fallback: search entire Resources if a subfolder was specified but nothing found
                if (
                    (instances == null || instances.Length == 0)
                    && !string.Equals(path, string.Empty, StringComparison.OrdinalIgnoreCase)
                )
                {
                    instances = Resources.LoadAll<T>(string.Empty);
                }

                // Editor fallback: pick any already loaded instances
                if (instances == null || instances.Length == 0)
                {
                    T[] found = Resources.FindObjectsOfTypeAll<T>();
                    if (found is { Length: > 0 })
                    {
                        instances = found;
                    }
                }

#if UNITY_EDITOR
                // Editor-only direct path attempts under Assets/Resources
                if (instances == null || instances.Length == 0)
                {
                    string typeName = type.Name;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        string candidate = $"Assets/Resources/{path}/{typeName}.asset";
                        T atPath = AssetDatabase.LoadAssetAtPath<T>(candidate);
                        if (atPath != null)
                        {
                            instances = new[] { atPath };
                        }
                    }
                    if (instances == null || instances.Length == 0)
                    {
                        string candidate = $"Assets/Resources/{typeName}.asset";
                        T atPath = AssetDatabase.LoadAssetAtPath<T>(candidate);
                        if (atPath != null)
                        {
                            instances = new[] { atPath };
                        }
                    }
                }
#endif

                if (instances == null || instances.Length == 0)
                {
                    return null;
                }

                if (instances.Length == 1)
                {
                    return instances[0];
                }

                Array.Sort(instances, (a, b) => string.CompareOrdinal(a?.name, b?.name));
                Debug.LogWarning(
                    $"Found multiple ScriptableObjectSingletons of type {type.Name}, defaulting to first by name."
                );
                return instances[0];
            });
        }
    }
}
