namespace CommandTerminal.Editor.CustomEditors
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using UnityEditor;
    using UnityEngine;

#if UNITY_EDITOR
    public static class CommandTerminalFontLoader
    {
        private const string FontPath = "Fonts";

        private static string GetCallerScriptDirectory([CallerFilePath] string sourceFilePath = "")
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                return string.Empty;
            }

            return Path.GetDirectoryName(sourceFilePath);
        }

        private static string FindPackageRootPath(string startDirectory)
        {
            string currentPath = startDirectory;
            while (!string.IsNullOrEmpty(currentPath))
            {
                if (File.Exists(Path.Combine(currentPath, "package.json")))
                {
                    try
                    {
                        DirectoryInfo directoryInfo = new(currentPath);
                        if (!directoryInfo.Exists)
                        {
                            return currentPath;
                        }

                        while (directoryInfo != null)
                        {
                            try
                            {
                                if (
                                    File.Exists(
                                        Path.Combine(directoryInfo.FullName, "package.json")
                                    )
                                )
                                {
                                    return directoryInfo.FullName;
                                }
                            }
                            catch
                            {
                                return currentPath;
                            }

                            try
                            {
                                directoryInfo = directoryInfo.Parent;
                            }
                            catch
                            {
                                return currentPath;
                            }
                        }
                    }
                    catch
                    {
                        return currentPath;
                    }
                }

                string parentPath = Path.GetDirectoryName(currentPath);
                if (parentPath == currentPath)
                {
                    break;
                }
                currentPath = parentPath;
            }

            return string.Empty;
        }

        public static Font[] LoadFonts()
        {
            string fontDirectoryRelativePath = FindFontDirectory();

            if (string.IsNullOrEmpty(fontDirectoryRelativePath))
            {
                return Array.Empty<Font>();
            }

            string[] searchFolders = { fontDirectoryRelativePath };
            List<Font> foundFonts = new();

            string[] fontGuids = AssetDatabase.FindAssets("t:Font", searchFolders);

            foreach (string guid in fontGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);

                if (
                    !string.IsNullOrEmpty(assetPath)
                    && assetPath.StartsWith(
                        fontDirectoryRelativePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && (
                        assetPath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                        || assetPath.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                    )
                )
                {
                    Font fontAsset = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
                    if (fontAsset != null)
                    {
                        foundFonts.Add(fontAsset);
                    }
                }
            }

            return foundFonts.ToArray();
        }

        private static string FindFontDirectory()
        {
            string scriptDirectory = GetCallerScriptDirectory();
            if (string.IsNullOrEmpty(scriptDirectory))
            {
                return string.Empty;
            }

            string packageRootAbsolute = FindPackageRootPath(scriptDirectory);
            if (string.IsNullOrEmpty(packageRootAbsolute))
            {
                return string.Empty;
            }

            string targetPathAbsolute = Path.Combine(
                packageRootAbsolute,
                FontPath.Replace('/', Path.DirectorySeparatorChar)
            );

            return AbsoluteToUnityRelativePath(targetPathAbsolute);
        }

        private static string AbsoluteToUnityRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            absolutePath = absolutePath.Replace('\\', '/');
            string projectRoot = Application.dataPath.Replace('\\', '/');

            projectRoot = Path.GetDirectoryName(projectRoot)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            if (absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                // +1 to remove the leading slash only if projectRoot doesn't end with one
                int startIndex = projectRoot.EndsWith("/")
                    ? projectRoot.Length
                    : projectRoot.Length + 1;
                return absolutePath.Length > startIndex ? absolutePath[startIndex..] : "";
            }
            string assetsPath = Application.dataPath.Replace('\\', '/');
            if (absolutePath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
            {
                int startIndex = assetsPath.EndsWith("/")
                    ? assetsPath.Length
                    : assetsPath.Length + 1;
                if (absolutePath.Length > startIndex)
                {
                    return "Assets/" + absolutePath[startIndex..];
                }

                return "Assets";
            }

            return string.Empty;
        }
    }
#endif
}
