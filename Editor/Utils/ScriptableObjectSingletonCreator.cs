namespace WallstopStudios.DxCommandTerminal.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.DxCommandTerminal.Internal;

    [InitializeOnLoad]
    public static class ScriptableObjectSingletonCreator
    {
        private const string ResourcesRoot = "Assets/Resources";
        private static bool _ensuring;

        static ScriptableObjectSingletonCreator()
        {
            EnsureSingletonAssets();
        }

        private static bool IsDerivedFromScriptableSingleton(Type t)
        {
            if (t == null || t.IsAbstract || t.IsGenericType)
            {
                return false;
            }
            Type baseType = t;
            while (baseType != null)
            {
                if (baseType.IsGenericType)
                {
                    Type def = baseType.GetGenericTypeDefinition();
                    if (def == typeof(ScriptableObjectSingleton<>))
                    {
                        return true;
                    }
                }
                baseType = baseType.BaseType;
            }
            return false;
        }

        public static void EnsureSingletonAssets()
        {
            if (_ensuring)
            {
                return;
            }

            _ensuring = true;
            try
            {
                EnsureFolder(ResourcesRoot);

                // Collect all concrete types deriving from our singleton base
                List<Type> candidates = new List<Type>();
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types.Where(x => x != null).ToArray();
                    }

                    foreach (Type t in types)
                    {
                        if (IsDerivedFromScriptableSingleton(t))
                        {
                            candidates.Add(t);
                        }
                    }
                }

                // Simple collision detection by simple name
                var collisions = candidates
                    .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

                foreach (Type type in candidates)
                {
                    if (collisions.ContainsKey(type.Name))
                    {
                        Debug.LogWarning(
                            $"ScriptableObjectSingletonCreator: Multiple types share the name '{type.Name}'. Skipping auto-creation. Add [ScriptableSingletonPath] to disambiguate or rename. Types: {string.Join(", ", collisions[type.Name].Select(x => x.FullName))}"
                        );
                        continue;
                    }

                    string sub = GetResourcesSubFolder(type);
                    string parent = string.IsNullOrWhiteSpace(sub)
                        ? ResourcesRoot
                        : PathCombine(ResourcesRoot, sub);

                    EnsureFolder(parent);

                    string assetPath = PathCombine(parent, type.Name + ".asset");

                    UnityEngine.Object atPath = AssetDatabase.LoadAssetAtPath(assetPath, type);
                    if (atPath != null)
                    {
                        continue;
                    }

                    // Try to find any existing asset of exact type and move it
                    string[] guids = AssetDatabase.FindAssets("t:" + type.Name);
                    bool moved = false;
                    foreach (string guid in guids)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }
                        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath(path, type);
                        if (obj == null)
                        {
                            continue;
                        }

                        // Don't overwrite an existing file at the intended target
                        if (
                            File.Exists(assetPath)
                            && !string.Equals(path, assetPath, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            Debug.LogWarning(
                                $"ScriptableObjectSingletonCreator: Target path already occupied at {assetPath}. Skipping move for {type.FullName}."
                            );
                            moved = true; // treat as handled to avoid creating duplicate
                            break;
                        }

                        string result = AssetDatabase.MoveAsset(path, assetPath);
                        if (string.IsNullOrEmpty(result))
                        {
                            moved = true;
                            break;
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"ScriptableObjectSingletonCreator: Failed to move existing {type.FullName} from {path} to {assetPath}: {result}"
                            );
                        }
                    }

                    if (!moved)
                    {
                        ScriptableObject created = ScriptableObject.CreateInstance(type);
                        AssetDatabase.CreateAsset(created, assetPath);
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                _ensuring = false;
            }
        }

        private static string GetResourcesSubFolder(Type type)
        {
            ScriptableSingletonPathAttribute attr =
                type.GetCustomAttribute<ScriptableSingletonPathAttribute>();
            if (attr == null)
            {
                return string.Empty;
            }
            string p = (attr.resourcesPath ?? string.Empty).Trim();
            if (p.StartsWith("/"))
            {
                p = p.TrimStart('/');
            }
            return p.Replace('\\', '/');
        }

        private static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string[] parts = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return;
            }
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static string PathCombine(string a, string b)
        {
            return (a.TrimEnd('/', '\\') + "/" + b.TrimStart('/', '\\')).Replace('\\', '/');
        }
    }
#endif
}
