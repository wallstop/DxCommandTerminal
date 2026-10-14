#if UNITY_EDITOR
namespace DxCommandTerminal.T13.Compatibility.Tests
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.DxCommandTerminal.Backend;
    using WallstopStudios.DxCommandTerminal.UI;

    public sealed class EditorCompatibilityProbe
    {
        private static bool DomainReloadEnabled => !string.Equals(Environment.GetEnvironmentVariable("DX_T13_DOMAIN_RELOAD_ENABLED"), "0", StringComparison.Ordinal);

        [UnityTest]
        public IEnumerator StoredPrefabPlayModeLifecycle()
        {
            if (!DomainReloadEnabled)
            {
                // Fresh imports can force compilation during the first Play Mode entry.
                EditorSettings.enterPlayModeOptionsEnabled = false;
                yield return new EnterPlayMode(expectDomainReload: true);
                yield return new ExitPlayMode();
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            }
            string persistenceBase = Environment.GetEnvironmentVariable("DX_T13_PERSISTENCE_FILE");
            Assert.That(string.IsNullOrEmpty(persistenceBase), Is.False);
            CommandLog previousBuffer = null;
            for (int session = 0; session < (DomainReloadEnabled ? 1 : 2); ++session)
            {
                Assert.That(Application.isPlaying, Is.False);
                if (!DomainReloadEnabled)
                {
                    Environment.SetEnvironmentVariable("DX_T13_PERSISTENCE_FILE", $"{persistenceBase}.session-{session}.json");
                }
                yield return new EnterPlayMode(expectDomainReload: DomainReloadEnabled);
                Assert.That(Application.isPlaying, Is.True);
                Assert.That(TerminalUI.Instance == null, Is.True);
                Assert.That(Terminal.Buffer, Is.Null);
                Assert.That(Terminal.History, Is.Null);
                Assert.That(Terminal.Shell, Is.Null);
                Assert.That(Terminal.AutoComplete, Is.Null);
                if (previousBuffer != null)
                {
                    Assert.That(CompatibilityProbe.ContainsLog(previousBuffer, "t13-previous-session"), Is.True);
                }
                IEnumerator lifecycle = CompatibilityProbe.VerifyStoredPrefabLifecycle();
                using (IDisposable lifecycleScope = (IDisposable)lifecycle)
                {
                    while (lifecycle.MoveNext())
                    {
                        if (lifecycle.Current is AsyncOperation operation)
                        {
                            while (!operation.isDone)
                            {
                                yield return null;
                            }
                        }
                        else
                        {
                            yield return lifecycle.Current;
                        }
                    }
                }
                Assert.That(Terminal.Buffer, Is.Not.SameAs(previousBuffer));
                Assert.That(CompatibilityProbe.ContainsLog(Terminal.Buffer, "t13-previous-session"), Is.False);
                Assert.That(Terminal.Log("t13-previous-session"), Is.True);
                previousBuffer = Terminal.Buffer;
                yield return new ExitPlayMode();
                Assert.That(Application.isPlaying, Is.False);
            }
        }
    }
}
#endif
