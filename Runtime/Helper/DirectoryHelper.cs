namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using UnityEngine;

    public static class DirectoryHelper
    {
        public static string GetCallerScriptDirectory([CallerFilePath] string sourceFilePath = "")
        {
            return string.IsNullOrWhiteSpace(sourceFilePath)
                ? string.Empty
                : Path.GetDirectoryName(sourceFilePath);
        }

        public static string FindPackageRootPath(string startDirectory)
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

        public static string FindAbsolutePathToDirectory(string directory)
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
                directory.Replace('/', Path.DirectorySeparatorChar)
            );

            return AbsoluteToUnityRelativePath(targetPathAbsolute);
        }

        public static string AbsoluteToUnityRelativePath(string absolutePath)
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
                int startIndex = projectRoot.EndsWith("/", StringComparison.OrdinalIgnoreCase)
                    ? projectRoot.Length
                    : projectRoot.Length + 1;
                return absolutePath.Length > startIndex ? absolutePath[startIndex..] : string.Empty;
            }
            string assetsPath = Application.dataPath.Replace('\\', '/');
            if (absolutePath.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
            {
                int startIndex = assetsPath.EndsWith("/", StringComparison.OrdinalIgnoreCase)
                    ? assetsPath.Length
                    : assetsPath.Length + 1;
                if (startIndex < absolutePath.Length)
                {
                    return "Assets/" + absolutePath[startIndex..];
                }

                return "Assets";
            }

            return string.Empty;
        }
    }
}
