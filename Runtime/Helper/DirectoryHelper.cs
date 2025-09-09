namespace WallstopStudios.DxCommandTerminal.Helper
{
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using UnityEngine;

    internal static class DirectoryHelper
    {
        internal static string GetCallerScriptDirectory([CallerFilePath] string sourceFilePath = "")
        {
            return string.IsNullOrWhiteSpace(sourceFilePath)
                ? string.Empty
                : Path.GetDirectoryName(sourceFilePath);
        }

        internal static string FindPackageRootPath(string startDirectory)
        {
            return FindRootPath(
                startDirectory,
                path => File.Exists(Path.Combine(path, "package.json"))
            );
        }

        internal static string FindRootPath(
            string startDirectory,
            Func<string, bool> terminalCondition
        )
        {
            string currentPath = startDirectory;
            while (!string.IsNullOrWhiteSpace(currentPath))
            {
                try
                {
                    if (terminalCondition(currentPath))
                    {
                        DirectoryInfo directoryInfo = new(currentPath);
                        if (!directoryInfo.Exists)
                        {
                            return currentPath;
                        }

                        return directoryInfo.FullName;
                    }
                }
                catch
                {
                    return currentPath;
                }

                try
                {
                    string parentPath = Path.GetDirectoryName(currentPath);
                    if (string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    currentPath = parentPath;
                }
                catch
                {
                    return currentPath;
                }
            }

            return string.Empty;
        }

        internal static string FindAbsolutePathToDirectory(string directory)
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

        internal static string AbsoluteToUnityRelativePath(string absolutePath)
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
            if (absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                int startIndex = projectRoot.EndsWith("/", StringComparison.OrdinalIgnoreCase)
                    ? projectRoot.Length
                    : projectRoot.Length + 1;
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
