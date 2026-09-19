namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using Themes;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public sealed class TerminalThemeAssetTests
    {
        private const string TempFolder = "Assets/TempTerminalThemeAssetTests";

        /*
            The 19 custom properties every terminal theme sheet must define;
            the generated sheet is valid pack content only if it covers them.
         */
        private static readonly string[] RequiredTokens =
        {
            "--terminal-bg",
            "--button-bg",
            "--input-field-bg",
            "--button-selected-bg",
            "--button-hover-bg",
            "--scroll-bg",
            "--scroll-inverse-bg",
            "--scroll-active-bg",
            "--button-text",
            "--button-selected-text",
            "--button-hover-text",
            "--input-text-color",
            "--text-message",
            "--text-warning",
            "--text-input-echo",
            "--text-shell",
            "--text-error",
            "--scroll-color",
            "--caret-color",
        };

        private readonly List<ScriptableObject> _createdAssets = new();

        [TearDown]
        public void TearDown()
        {
            foreach (ScriptableObject created in _createdAssets)
            {
                /*
                    Persisted assets are removed with the temp folder below;
                    destroying them mid-play throws the data-loss guard.
                 */
#if UNITY_EDITOR
                if (created != null && !EditorUtility.IsPersistent(created))
#else
                if (created != null)
#endif
                {
                    UnityEngine.Object.Destroy(created);
                }
            }

            _createdAssets.Clear();

#if UNITY_EDITOR
            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
#endif
        }

        [Test]
        public void GeneratedSheetDefinesEveryRequiredToken()
        {
            TerminalThemeAsset asset = CreateAsset("Sweep");
            string uss = asset.BuildUss();
            foreach (string token in RequiredTokens)
            {
                Assert.IsTrue(
                    uss.Contains(token + ":", StringComparison.Ordinal),
                    $"Generated sheet must define '{token}'"
                );
            }
        }

        [Test]
        public void GeneratedSheetCarriesTheOwnershipMarker()
        {
            TerminalThemeAsset asset = CreateAsset("Marked");
            StringAssert.StartsWith(
                TerminalThemeAsset.GeneratedMarkerPrefix,
                asset.BuildUss(),
                "Generated sheets must identify themselves as generated"
            );
        }

        [Test]
        public void GeneratedSheetTargetsTheAssetDerivedClass()
        {
            foreach (
                (string assetName, string expectedClass) in new[]
                {
                    ("DarkTheme", "dark-theme"),
                    ("Neon Nights", "neon-nights-theme"),
                    ("solarized-dark-theme", "solarized-dark-theme"),
                    ("space theme", "space-theme"),
                    ("NewTerminalTheme", "new-terminal-theme"),
                    ("My Cool_Theme 2", "my-cool-theme-2-theme"),
                    ("3D Theme", "theme-3-d-theme"),
                }
            )
            {
                TerminalThemeAsset asset = CreateAsset(assetName);
                Assert.AreEqual(
                    expectedClass,
                    asset.CssClassName,
                    $"CSS class derived from asset name '{assetName}'"
                );
                StringAssert.Contains(
                    "." + expectedClass + " {",
                    asset.BuildUss(),
                    $"Generated sheet selector for asset '{assetName}'"
                );
            }
        }

        [Test]
        public void ColorsFormatAsUssRgbaValues()
        {
            TerminalThemeAsset asset = CreateAsset("FormatProbe");
            SetColor(asset, "_terminalBg", new Color(0f, 0f, 0f, 0.5f));
            SetColor(asset, "_buttonBg", new Color(1f, 1f, 1f, 1f));
            SetColor(asset, "_textError", new Color(1f, 0.2706f, 0.2275f, 1f));
            string uss = asset.BuildUss();

            StringAssert.Contains("--terminal-bg: rgba(0, 0, 0, 0.5);", uss);
            StringAssert.Contains("--button-bg: rgba(255, 255, 255, 1);", uss);
            StringAssert.Contains("--text-error: rgba(255, 69, 58, 1);", uss);
        }

        private TerminalThemeAsset CreateAsset(string name)
        {
            TerminalThemeAsset asset = ScriptableObject.CreateInstance<TerminalThemeAsset>();
            asset.name = name;
            _createdAssets.Add(asset);
            return asset;
        }

#if UNITY_EDITOR
        [Test]
        public void CreatingTheAssetAutoWritesTheSiblingSheet()
        {
            CreateTempFolder();

            const string assetPath = TempFolder + "/ProbeTheme.asset";
            const string sheetPath = TempFolder + "/ProbeTheme.uss";

            /*
                Creating (or editing) the asset fires OnPostprocessAllAssets,
                the same auto-wire trigger the inspector uses; the sibling
                .uss must appear without any manual step.
             */
            AssetDatabase.CreateAsset(CreateAsset("ProbeTheme"), assetPath);

            Assert.IsTrue(
                File.Exists(sheetPath),
                "Creating a theme asset auto-writes the sibling .uss"
            );
            TerminalThemeAsset asset = AssetDatabase.LoadAssetAtPath<TerminalThemeAsset>(assetPath);
            Assert.AreEqual(
                asset.BuildUss(),
                File.ReadAllText(sheetPath),
                "Sheet content matches the asset"
            );

            SetColor(asset, "_textError", new Color(1f, 0f, 0f, 1f));
            Assert.AreEqual(
                asset.BuildUss(),
                File.ReadAllText(sheetPath),
                "Editing the asset rewrites the sheet"
            );
        }

        [Test]
        public void DeletingTheAssetRemovesItsGeneratedSheet()
        {
            CreateTempFolder();

            const string assetPath = TempFolder + "/ProbeTheme.asset";
            const string sheetPath = TempFolder + "/ProbeTheme.uss";
            AssetDatabase.CreateAsset(CreateAsset("ProbeTheme"), assetPath);
            Assert.IsTrue(File.Exists(sheetPath), "Sanity: the sheet exists after create");

            AssetDatabase.DeleteAsset(assetPath);

            Assert.IsFalse(
                File.Exists(sheetPath),
                "Deleting the asset removes the generated sheet with it"
            );
        }

        [Test]
        public void RenamingTheAssetRegeneratesTheSheetUnderTheNewName()
        {
            CreateTempFolder();

            const string assetPath = TempFolder + "/ProbeTheme.asset";
            const string movedAssetPath = TempFolder + "/RenamedProbe.asset";
            const string oldSheetPath = TempFolder + "/ProbeTheme.uss";
            const string newSheetPath = TempFolder + "/RenamedProbe.uss";
            AssetDatabase.CreateAsset(CreateAsset("ProbeTheme"), assetPath);
            Assert.IsTrue(File.Exists(oldSheetPath), "Sanity: the sheet exists after create");

            Assert.IsTrue(
                string.IsNullOrEmpty(AssetDatabase.MoveAsset(assetPath, movedAssetPath)),
                "Sanity: the asset rename succeeds"
            );

            Assert.IsFalse(File.Exists(oldSheetPath), "The stale old-name sheet is cleaned up");
            Assert.IsTrue(File.Exists(newSheetPath), "The sheet follows the rename");
            Assert.AreEqual(
                AssetDatabase.LoadAssetAtPath<TerminalThemeAsset>(movedAssetPath).BuildUss(),
                File.ReadAllText(newSheetPath),
                "The new-name sheet targets the new asset name"
            );
            StyleSheet movedSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(newSheetPath);
            Assert.IsTrue(movedSheet != null, "The new-name sheet imports as a StyleSheet");
        }

        [Test]
        public void HandWrittenSheetsSharingTheAssetNameArePreserved()
        {
            CreateTempFolder();

            const string assetPath = TempFolder + "/ProbeTheme.asset";
            const string sheetPath = TempFolder + "/ProbeTheme.uss";
            const string handWritten = ".probe-theme {\n    --terminal-bg: rgba(1, 2, 3, 1);\n}";
            File.WriteAllText(sheetPath, handWritten);

            LogAssert.Expect(LogType.Warning, new Regex("Skipping generated theme sheet"));
            AssetDatabase.CreateAsset(CreateAsset("ProbeTheme"), assetPath);

            Assert.AreEqual(
                handWritten,
                File.ReadAllText(sheetPath),
                "A hand-written sheet sharing the asset name must not be clobbered"
            );
        }
#endif

        private static void CreateTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets", "TempTerminalThemeAssetTests");
            }
        }

#if UNITY_EDITOR
        private static void SetColor(TerminalThemeAsset asset, string field, Color value)
        {
            SerializedObject serialized = new(asset);
            serialized.FindProperty(field).colorValue = value;
            serialized.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
#endif
    }
}
