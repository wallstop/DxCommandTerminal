#if UNITY_EDITOR
namespace DxCommandTerminal.T13.Compatibility.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.DxCommandTerminal.Backend;
    using WallstopStudios.DxCommandTerminal.Persistence;
    using WallstopStudios.DxCommandTerminal.Themes;
    using WallstopStudios.DxCommandTerminal.UI;
    using Object = UnityEngine.Object;

    public sealed class CompatibilityProbe
    {
        private const string FixtureRoot = "Assets/Compatibility";

        [UnityTest]
        public IEnumerator StoredPrefabPlayModeLifecycle()
        {
            Assert.That(Application.isPlaying, Is.True);
            string expectedDomainReload = Environment.GetEnvironmentVariable(
                "DX_T13_DOMAIN_RELOAD_ENABLED"
            );
            if (expectedDomainReload != null)
            {
                Assert.That(
                    EditorSettings.enterPlayModeOptionsEnabled,
                    Is.EqualTo(expectedDomainReload == "0")
                );
                if (expectedDomainReload == "0")
                {
                    Assert.That(
                        (
                            EditorSettings.enterPlayModeOptions
                            & EnterPlayModeOptions.DisableDomainReload
                        ) != 0,
                        Is.True
                    );
                }
            }
            Scene originalScene = SceneManager.GetActiveScene();
            List<Scene> scenes = new();
            string persistenceFile = Environment.GetEnvironmentVariable("DX_T13_PERSISTENCE_FILE");
            Assert.That(string.IsNullOrEmpty(persistenceFile), Is.False);
            string fixturePersistence = Path.Combine(
                Application.dataPath,
                "Compatibility",
                "TerminalFixture.persistence.json"
            );
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(persistenceFile));
                File.Copy(fixturePersistence, persistenceFile, true);
                Scene firstScene = SceneManager.CreateScene("DxCmdT13First");
                SceneManager.SetActiveScene(firstScene);
                scenes.Add(firstScene);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    $"{FixtureRoot}/TerminalFixture.prefab"
                );
                Assert.That(prefab != null, Is.True);
                GameObject firstObject = Object.Instantiate(prefab);
                TerminalUI firstTerminal = firstObject.GetComponent<TerminalUI>();
                TerminalThemePersister persister =
                    firstObject.GetComponent<TerminalThemePersister>();
                UIDocument document = firstObject.AddComponent<UIDocument>();
                SerializedObject terminalObject = new SerializedObject(firstTerminal);
                terminalObject.FindProperty("_uiDocument").objectReferenceValue = document;
                terminalObject.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(terminalObject.FindProperty("resetStateOnInit").boolValue, Is.False);
                Assert.That(Terminal.Buffer.Capacity, Is.EqualTo(64));
                Assert.That(Terminal.History.Capacity, Is.EqualTo(32));
                Assert.That(firstTerminal.CurrentTheme, Is.EqualTo("light-theme"));
                Assert.That(firstTerminal.CurrentFont.name, Is.EqualTo("Courier Prime"));
                CommandLog buffer = Terminal.Buffer;
                Assert.That(Terminal.Log("t13-early-log"), Is.True);
                yield return null;
                Assert.That(Terminal.Buffer, Is.SameAs(buffer));
                Assert.That(ContainsLog(buffer, "t13-early-log"), Is.True);
                int frameBudget = 120;
                while (
                    0 < frameBudget--
                    && !string.Equals(
                        firstTerminal.CurrentTheme,
                        "dark-theme",
                        StringComparison.Ordinal
                    )
                )
                {
                    yield return null;
                }
                Assert.That(firstTerminal.CurrentTheme, Is.EqualTo("dark-theme"));
                TerminalFontPack fontPack = AssetDatabase.LoadAssetAtPath<TerminalFontPack>(
                    "Packages/com.wallstop-studios.dxcommandterminal/Packs/Fonts/Minimal.asset"
                );
                Assert.That(fontPack != null, Is.True);
                Font persistedFont = fontPack.Fonts[0];
                firstTerminal.SetTheme("light-theme", persist: true);
                firstTerminal.SetFont(persistedFont, persist: true);
                persister.savePeriodically = true;
                persister.savePeriod = 0;
                frameBudget = 120;
                while (
                    0 < frameBudget-- && !PersistenceContains(persistenceFile, persistedFont.name)
                )
                {
                    yield return null;
                }
                Assert.That(PersistenceContains(persistenceFile, persistedFont.name), Is.True);
                Assert.That(PersistenceContains(persistenceFile, "light-theme"), Is.True);

                Scene secondScene = SceneManager.CreateScene("DxCmdT13Second");
                SceneManager.SetActiveScene(secondScene);
                scenes.Add(secondScene);
                yield return SceneManager.UnloadSceneAsync(firstScene);
                Assert.That(TerminalUI.Instance == null, Is.True);
                Assert.That(Terminal.Buffer, Is.SameAs(buffer));
                Assert.That(Terminal.Log("t13-after-scene-change"), Is.True);
                GameObject secondObject = Object.Instantiate(prefab);
                TerminalUI secondTerminal = secondObject.GetComponent<TerminalUI>();
                UIDocument secondDocument = secondObject.AddComponent<UIDocument>();
                SerializedObject secondTerminalObject = new SerializedObject(secondTerminal);
                secondTerminalObject.FindProperty("_uiDocument").objectReferenceValue =
                    secondDocument;
                secondTerminalObject.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(
                    secondTerminalObject.FindProperty("resetStateOnInit").boolValue,
                    Is.False
                );
                Assert.That(Terminal.Buffer.Capacity, Is.EqualTo(64));
                Assert.That(Terminal.History.Capacity, Is.EqualTo(32));
                frameBudget = 120;
                while (0 < frameBudget-- && secondTerminal.CurrentFont != persistedFont)
                {
                    yield return null;
                }
                Assert.That(secondTerminal.CurrentTheme, Is.EqualTo("light-theme"));
                Assert.That(secondTerminal.CurrentFont, Is.SameAs(persistedFont));
                Assert.That(Terminal.Buffer, Is.SameAs(buffer));
                Assert.That(ContainsLog(buffer, "t13-after-scene-change"), Is.True);
                Assert.That(Terminal.Log("t13-before-reset"), Is.True);
                GameObject resetObject = Object.Instantiate(prefab);
                resetObject.SetActive(false);
                SerializedObject resetObjectData = new SerializedObject(
                    resetObject.GetComponent<TerminalUI>()
                );
                resetObjectData.FindProperty("resetStateOnInit").boolValue = true;
                resetObjectData.ApplyModifiedPropertiesWithoutUndo();
                resetObject.SetActive(true);
                yield return null;
                Assert.That(Terminal.Buffer == buffer, Is.False);
                Assert.That(ContainsLog(Terminal.Buffer, "t13-before-reset"), Is.False);
            }
            finally
            {
                if (originalScene.IsValid() && originalScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalScene);
                }
                for (int index = 0; index < scenes.Count; ++index)
                {
                    if (scenes[index].IsValid() && scenes[index].isLoaded)
                    {
                        SceneManager.UnloadSceneAsync(scenes[index]);
                    }
                }
                File.Delete(persistenceFile);
            }
        }

        private static bool ContainsLog(CommandLog buffer, string expected)
        {
            for (int index = 0; index < buffer.Logs.Count; ++index)
            {
                if (string.Equals(buffer.Logs[index].message, expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool PersistenceContains(string path, string expected)
        {
            return File.Exists(path)
                && 0 <= File.ReadAllText(path).IndexOf(expected, StringComparison.Ordinal);
        }
    }
}
#endif
