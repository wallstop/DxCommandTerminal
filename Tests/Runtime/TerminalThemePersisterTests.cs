namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System.Collections;
    using NUnit.Framework;
    using Persistence;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;

    public sealed class TerminalThemePersisterTests
    {
        [UnityTest]
        public IEnumerator PersistenceProfileCanDisableSaving()
        {
            yield return TerminalTests.SpawnTerminal(
                resetStateOnInit: true,
                configure: terminal => terminal.disableUIForTests = true,
                ensureLargeLogBuffer: true
            );

            TerminalUI terminal = TerminalUI.Instance;
            Assert.IsNotNull(terminal);

            TerminalThemePersister persister =
                terminal.gameObject.AddComponent<TerminalThemePersister>();
            TerminalThemePersistenceProfile profile =
                ScriptableObject.CreateInstance<TerminalThemePersistenceProfile>();
            profile.enablePersistence = false;
            persister.SetPersistenceProfileForTests(profile);

            yield return null;

            Assert.IsFalse(persister.PersistenceEnabledForTests);
            ScriptableObject.DestroyImmediate(profile);
        }
    }
}
