namespace WallstopStudios.DxCommandTerminal.Editor.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Animations;
    using UnityEngine;
    using UnityEngine.AI;
    using UnityEngine.Playables;
    using UnityEngine.UIElements;
    using UnityEngine.VFX;
    using Object = UnityEngine.Object;

    /*
        using System.IO;
using UnityEditor;

public static class UIAssetCreator
{
    /// <summary>
    /// Creates a .uss file at the given path (inside your Assets folder) by writing raw text
    /// and then calls ImportAsset so Unity runs its USS importer.
    /// </summary>
    public static void CreateUSS(string relativePathInProject, string ussText)
    {
        // make sure path ends with .uss
        if (!relativePathInProject.EndsWith(".uss", System.StringComparison.OrdinalIgnoreCase))
            relativePathInProject += ".uss";

        var fullDiskPath = Path.Combine(Application.dataPath.Substring(0, Application.dataPath.Length - 6),
                                        relativePathInProject);

        // 1) write the file
        Directory.CreateDirectory(Path.GetDirectoryName(fullDiskPath));
        File.WriteAllText(fullDiskPath, ussText);

        // 2) import it so Unity creates the StyleSheet asset
        AssetDatabase.ImportAsset(relativePathInProject);
        AssetDatabase.Refresh();
    }

    /// <summary>
    /// Same for UXML files.
    /// </summary>
    public static void CreateUXML(string relativePathInProject, string uxmlText)
    {
        if (!relativePathInProject.EndsWith(".uxml", System.StringComparison.OrdinalIgnoreCase))
            relativePathInProject += ".uxml";

        var fullDiskPath = Path.Combine(Application.dataPath.Substring(0, Application.dataPath.Length - 6),
                                        relativePathInProject);

        Directory.CreateDirectory(Path.GetDirectoryName(fullDiskPath));
        File.WriteAllText(fullDiskPath, uxmlText);

        AssetDatabase.ImportAsset(relativePathInProject);
        AssetDatabase.Refresh();
    }
}

     */

    public static class AssetCreator
    {
        private static readonly Dictionary<Type, string> TypesToExtend = new()
        {
            // Core Unity assets
            { typeof(GameObject), ".prefab" },
            { typeof(SceneAsset), ".unity" },
            { typeof(Material), ".mat" },
            { typeof(Shader), ".shader" },
            { typeof(ComputeShader), ".compute" },
            { typeof(TextAsset), ".txt" },
            { typeof(MonoScript), ".cs" },
            { typeof(AnimationClip), ".anim" },
            { typeof(RuntimeAnimatorController), ".controller" },
            { typeof(AnimatorController), ".controller" },
            { typeof(AnimatorOverrideController), ".overrideController" },
            { typeof(AvatarMask), ".mask" },
            { typeof(PhysicMaterial), ".physicMaterial" },
            { typeof(PhysicsMaterial2D), ".physicsMaterial2D" },
            { typeof(RenderTexture), ".renderTexture" },
            { typeof(ShaderVariantCollection), ".shadervariants" },
            { typeof(Cubemap), ".cubemap" },
            { typeof(CubemapArray), ".cubemaparray" },
            { typeof(NavMeshData), ".navmesh" },
            { typeof(AssetBundleManifest), ".manifest" },
            { typeof(TerrainData), ".asset" },
            { typeof(PlayableAsset), ".playable" },
            { typeof(VisualEffectAsset), ".vfx" },
            { typeof(StyleSheet), ".uss" },
            { typeof(VisualTreeAsset), ".uxml" },
        };

        public static T CreateAssetSafe<T>(
            T objectToSave,
            string name = null,
            string targetDirectory = "Assets/Packages/WallstopStudios.DxCommandTerminal"
        )
            where T : Object
        {
            if (objectToSave == null)
            {
                Debug.LogError("Object to save is null. Cannot create asset.");
                return null;
            }

            string fileExtension = string.Empty;
            string existingAssetPath = AssetDatabase.GetAssetPath(objectToSave);
            if (string.IsNullOrWhiteSpace(existingAssetPath))
            {
                foreach (KeyValuePair<Type, string> typeToExtend in TypesToExtend)
                {
                    if (!typeToExtend.Key.IsAssignableFrom(objectToSave.GetType()))
                    {
                        continue;
                    }

                    fileExtension = typeToExtend.Value;
                    break;
                }
            }
            else
            {
                fileExtension = Path.GetExtension(existingAssetPath);
            }

            if (string.IsNullOrWhiteSpace(fileExtension))
            {
                Debug.LogError(
                    $"Failed to determine file extension for {objectToSave}, cannot save."
                );
                return null;
            }

            string assetName = $"{name ?? objectToSave.name}{fileExtension}";
            string fullPath = Path.Combine(targetDirectory, assetName);
            fullPath = fullPath.Replace('\\', '/');

            if (!Directory.Exists(targetDirectory))
            {
                try
                {
                    Directory.CreateDirectory(targetDirectory);
                    Debug.Log($"Created directory: {targetDirectory}");
                }
                catch (IOException ex)
                {
                    Debug.LogError($"Failed to create directory {targetDirectory}: {ex}");
                    return null;
                }
            }

            string uniquePath = AssetDatabase.GenerateUniqueAssetPath(fullPath);
            if (uniquePath != fullPath)
            {
                Debug.LogWarning(
                    $"An asset already exists at {fullPath}. Saving to {uniquePath} instead."
                );
            }
            else
            {
                uniquePath = fullPath;
            }

            try
            {
                if (string.IsNullOrEmpty(existingAssetPath))
                {
                    if (string.Equals(fileExtension, ".uss", StringComparison.OrdinalIgnoreCase))
                    {
                        AssetDatabase.ImportAsset(uniquePath);
                    }
                    AssetDatabase.CreateAsset(objectToSave, uniquePath);
                }
                else
                {
                    bool copyOk = AssetDatabase.CopyAsset(existingAssetPath, uniquePath);
                    if (!copyOk)
                    {
                        Debug.LogError(
                            $"Failed to copy asset from path {existingAssetPath} to {uniquePath}"
                        );
                        return null;
                    }
                }

                Debug.Log($"Successfully created asset at: {uniquePath}");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                return AssetDatabase.LoadAssetAtPath<T>(uniquePath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Failed to create asset at {uniquePath}: {ex}");
                return null;
            }
        }
    }
}
