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

}

     */

    public static class AssetCreator
    {
        private static readonly List<(Type, string)> TypesToExtend = new()
        {
            (typeof(GameObject), ".prefab"),
            (typeof(SceneAsset), ".unity"),
            (typeof(Material), ".mat"),
            (typeof(Shader), ".shader"),
            (typeof(ComputeShader), ".compute"),
            (typeof(TextAsset), ".txt"),
            (typeof(MonoScript), ".cs"),
            (typeof(MonoBehaviour), ".cs"),
            (typeof(AnimationClip), ".anim"),
            (typeof(RuntimeAnimatorController), ".controller"),
            (typeof(AnimatorController), ".controller"),
            (typeof(AnimatorOverrideController), ".overrideController"),
            (typeof(AvatarMask), ".mask"),
            (typeof(PhysicMaterial), ".physicMaterial"),
            (typeof(PhysicsMaterial2D), ".physicsMaterial2D"),
            (typeof(RenderTexture), ".renderTexture"),
            (typeof(ShaderVariantCollection), ".shadervariants"),
            (typeof(Cubemap), ".cubemap"),
            (typeof(CubemapArray), ".cubemaparray"),
            (typeof(NavMeshData), ".navmesh"),
            (typeof(AssetBundleManifest), ".manifest"),
            (typeof(TerrainData), ".asset"),
            (typeof(PlayableAsset), ".playable"),
            (typeof(VisualEffectAsset), ".vfx"),
            (typeof(ThemeStyleSheet), ".tss"),
            (typeof(StyleSheet), ".uss"),
            (typeof(VisualTreeAsset), ".uxml"),
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
                foreach ((Type type, string extension) in TypesToExtend)
                {
                    if (!type.IsAssignableFrom(objectToSave.GetType()))
                    {
                        continue;
                    }

                    fileExtension = extension;
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

            try
            {
                if (string.IsNullOrWhiteSpace(existingAssetPath))
                {
                    AssetDatabase.CreateAsset(objectToSave, uniquePath);
                }
                else
                {
                    if (
                        string.Equals(fileExtension, ".uss", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fileExtension, ".tss", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fileExtension, ".uxml", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        // Can't copy StyleSheets for some unknown reason
                        string fileContents = File.ReadAllText(existingAssetPath);
                        File.WriteAllText(uniquePath, fileContents);
                    }
                    else
                    {
                        bool copyOk = AssetDatabase.CopyAsset(existingAssetPath, uniquePath);
                        if (!copyOk)
                        {
                            Debug.LogError(
                                $"Failed to copy asset from path {existingAssetPath} to {uniquePath}."
                            );
                            return null;
                        }
                    }
                }

                Debug.Log(
                    $"Successfully created asset at: {uniquePath} with extension '{fileExtension}'."
                );

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                return AssetDatabase.LoadAssetAtPath<T>(uniquePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create asset at {uniquePath}: {ex}");
                return null;
            }
        }
    }
}
