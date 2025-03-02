namespace Editor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using UnityEditor;
    using UnityEngine;

    [InitializeOnLoad]
    public static class SetupDisableHotkeys
    {
        private static readonly string WebGLPath = "Assets/Plugins/WebGL/";

        private static readonly string PackagePathRelative =
            "Packages/com.wallstop-studios.dxcommandterminal/Runtime/Javascript/";

        private static readonly string LibraryPathRelative =
            "Library/PackageCache/com.wallstop-studios.dxcommandterminal/Runtime/Javascript/";

        private static readonly string[] JsLibFileNames = { "DisableHotkeys.jslib" };

        static SetupDisableHotkeys()
        {
            EditorApplication.delayCall += EnsureJsLibsExist;
        }

        private static void EnsureJsLibsExist()
        {
            HashSet<string> jsLibNames = new();
            foreach (
                string dllGuid in AssetDatabase.FindAssets("t:DefaultAsset", new[] { WebGLPath })
            )
            {
                string jsLibPath = AssetDatabase.GUIDToAssetPath(dllGuid);
                if (!jsLibPath.EndsWith(".jslib"))
                {
                    continue;
                }

                string jsLibName = Path.GetFileName(jsLibPath);
                jsLibNames.Add(jsLibName);
            }

            string[] relativeDirectories = { LibraryPathRelative, PackagePathRelative };

            bool anyCreated = false;
            foreach (
                string jsLibName in JsLibFileNames.Where(jsLibName =>
                    !jsLibNames.Contains(jsLibName)
                )
            )
            {
                string outputAsset = $"{WebGLPath}{jsLibName}";

                if (File.Exists(outputAsset))
                {
                    continue;
                }

                bool created = false;
                foreach (string relativeDirectory in relativeDirectories)
                {
                    try
                    {
                        string sourceFile = $"{relativeDirectory}{jsLibName}";

                        if (!File.Exists(sourceFile))
                        {
                            continue;
                        }
                        Directory.CreateDirectory(WebGLPath);
                        File.Copy(sourceFile, outputAsset);
                        AssetDatabase.ImportAsset(outputAsset);
                        created = true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(
                            $"Failed to copy {jsLibName} from {relativeDirectory}, failed with {e}."
                        );
                    }
                }
                anyCreated |= created;
            }

            if (anyCreated)
            {
                AssetDatabase.Refresh();
            }
        }
    }
}
