﻿namespace WallstopStudios.DxCommandTerminal.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using DxCommandTerminal.Helper;
    using Extensions;
    using Helper;
    using Themes;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    [CustomEditor(typeof(TerminalThemePack))]
    public sealed class TerminalThemePackEditor : Editor
    {
        private readonly HashSet<StyleSheet> _styleCache = new();
        private readonly HashSet<StyleSheet> _invalidStyles = new();
        private string _lastSelectedDirectory;
        private GUIStyle _impactButtonStyle;

        private void OnEnable()
        {
            _styleCache.Clear();
            _invalidStyles.Clear();
        }

        public override void OnInspectorGUI()
        {
            _impactButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                normal = { textColor = Color.yellow },
                fontStyle = FontStyle.Bold,
            };

            serializedObject.Update();
            TerminalThemePack themePack = target as TerminalThemePack;
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                nameof(TerminalThemePack._themeNames)
            );

            if (themePack == null)
            {
                return;
            }

            bool anyChanged = false;
            if (themePack._themes == null)
            {
                anyChanged = true;
                themePack._themes = new List<StyleSheet>();
            }

            _styleCache.Clear();
            _invalidStyles.Clear();
            bool anyInvalidTheme = false;
            foreach (StyleSheet theme in themePack._themes)
            {
                if (theme == null)
                {
                    anyInvalidTheme = true;
                    continue;
                }
                _styleCache.Add(theme);
                if (!TerminalThemeStyleSheetHelper.GetAvailableThemes(theme).Any())
                {
                    _invalidStyles.Add(theme);
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Data Manipulation", EditorStyles.boldLabel);

            if (
                anyInvalidTheme
                || _styleCache.Count != themePack._themes.Count
                || _invalidStyles.Any()
            )
            {
                if (GUILayout.Button("Fix Invalid Themes", _impactButtonStyle))
                {
                    anyChanged = true;
                    _styleCache.Clear();
                    themePack._themes.RemoveAll(theme => theme == null || !_styleCache.Add(theme));
                }
            }

            Object activeObject = Selection.activeObject;
            if (activeObject == null)
            {
                activeObject = themePack;
            }

            string assetPath = AssetDatabase.GetAssetPath(activeObject);
            string dataPath = Application.dataPath;
            if (
                dataPath.EndsWith("/", StringComparison.OrdinalIgnoreCase)
                || dataPath.EndsWith("\\", StringComparison.OrdinalIgnoreCase)
            )
            {
                dataPath = dataPath.Substring(0, dataPath.Length - 1);
            }
            if (dataPath.EndsWith("Assets", StringComparison.OrdinalIgnoreCase))
            {
                dataPath = dataPath.Substring(0, dataPath.Length - "Assets".Length);
            }

            if (!Directory.Exists(Path.Combine(dataPath, assetPath)))
            {
                assetPath = Path.GetDirectoryName(assetPath) ?? assetPath;
            }

            assetPath = assetPath.Replace('\\', '/');

            GUIContent loadFromCurrentDirectoryContent = new(
                "Load From Current Directory",
                $"Loads all themes from '{assetPath}'"
            );

            if (GUILayout.Button(loadFromCurrentDirectoryContent))
            {
                UpdateFromDirectory(assetPath);
            }
            else if (GUILayout.Button("Load From Directory (Select)"))
            {
                _lastSelectedDirectory = EditorUtility.OpenFolderPanel(
                    "Select Directory",
                    string.IsNullOrWhiteSpace(_lastSelectedDirectory)
                        ? Application.dataPath
                        : _lastSelectedDirectory,
                    string.Empty
                );
                UpdateFromDirectory(_lastSelectedDirectory);
            }

            if (
                anyChanged
                || (
                    !themePack._themes.IsSorted(UnityObjectNameComparer.Instance)
                    && GUILayout.Button("Sort Themes")
                )
                || (themePack._themes.Count != themePack._themeNames.Count)
            )
            {
                SortThemes();
                EditorUtility.SetDirty(themePack);
                serializedObject.ApplyModifiedProperties();
            }

            return;

            void SortThemes()
            {
                themePack._themes.SortByName();
                themePack._themeNames ??= new List<string>();
                themePack._themeNames.Clear();
                themePack._themeNames.AddRange(
                    themePack._themes.SelectMany(TerminalThemeStyleSheetHelper.GetAvailableThemes)
                );
            }

            void UpdateFromDirectory(string directory)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }

                DirectoryInfo directoryInfo = new(directory);
                if (!directoryInfo.Exists)
                {
                    return;
                }

                directory = DirectoryHelper.AbsoluteToUnityRelativePath(directoryInfo.FullName);

                string[] fontGuids = AssetDatabase.FindAssets("t:StyleSheet", new[] { directory });
                foreach (string guid in fontGuids)
                {
                    string styleAssetPath = AssetDatabase.GUIDToAssetPath(guid);

                    if (string.IsNullOrWhiteSpace(styleAssetPath))
                    {
                        continue;
                    }

                    StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                        styleAssetPath
                    );
                    if (
                        styleSheet != null
                        && TerminalThemeStyleSheetHelper.GetAvailableThemes(styleSheet).Any()
                        && _styleCache.Add(styleSheet)
                    )
                    {
                        anyChanged = true;
                        themePack._themes.Add(styleSheet);
                    }
                }
            }
        }
    }
#endif
}
