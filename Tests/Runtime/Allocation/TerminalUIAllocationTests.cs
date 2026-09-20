namespace WallstopStudios.DxCommandTerminal.Tests.Runtime.Allocation
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Backend;
    using Components;
    using NUnit.Framework;
    using Themes;
    using UI;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /*
        Zero-allocation pins for the TerminalUI-level sync paths of the
        #108/#111 campaign: the per-keystroke hint sweep, the legacy Tab
        cycle, and the steady refresh passes. The UITK ChangeEvent a field
        write allocates is Unity-owned and deliberately unpinned, and the
        provider Tab cycle deliberately materializes the new input line
        (pinned as a documented cost); everything else here claims zero.
        Companion timing evidence lives in
        TerminalUIStandardOperationsBenchmarkTests.
    */
    public sealed class TerminalUIAllocationTests
    {
        private const int CommandCount = 32;
        private const int LogCapacity = 16;
        private const int ProviderCandidateCount = 32;

        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";
        private const string SelectiveToken = "bench-cmd-00";
        private const string ProviderCommandName = "bench-complete";

        private TerminalUI _terminal;
        private GameObject _terminalObject;
        private PanelSettings _panelSettings;

        private static void HandleNoop(CommandArg[] arguments) { }

        private static T LoadAsset<T>(string relativePath)
            where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
            Assert.That(asset != null, $"Expected the test asset at {PackageRoot}/{relativePath}");
            return asset;
        }

        [TearDown]
        public void TearDown()
        {
            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }

            Terminal.Shell?.ClearCustomCommands();
        }

        [UnityTest]
        public IEnumerator PerKeystrokeHintSweepIsAllocationFree()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            RegisterTier();

            _terminal._commandInput.value = SelectiveToken;
            yield return null;

            AllocationAssertions.AssertZeroAllocations(
                "per-keystroke hint sweep (Always mode)",
                () => _terminal.ResetAutoComplete()
            );
            Assert.AreEqual(
                CommandCount,
                _terminal._lastCompletionBuffer.Count,
                "Sanity: the sweep must keep every prefix match as a hint"
            );
        }

        [UnityTest]
        public IEnumerator TabLegacyCycleIsAllocationFree()
        {
            yield return SpawnTerminal(HintDisplayMode.AutoCompleteOnly);
            RegisterTier();

            _terminal._commandInput.value = SelectiveToken;
            yield return null;

            AllocationAssertions.AssertZeroAllocations(
                "legacy Tab completion cycle",
                () => _terminal.CompleteCommand(true)
            );
            Assert.AreEqual(
                CommandCount,
                _terminal._lastCompletionBuffer.Count,
                "Sanity: the cycle must keep the completion buffer populated"
            );
        }

        [UnityTest]
        public IEnumerator TabProviderCycleMaterializesInsertionStrings()
        {
            yield return SpawnTerminal(HintDisplayMode.AutoCompleteOnly);
            RegisterTier();
            RegisterProviderCommand();

            string input = $"{ProviderCommandName} candidate-00";
            _terminal._commandInput.value = input;
            yield return null;

            /*
                Documented, not pinned to zero: each accepted cycle rebuilds
                the input line (Remove + Insert) and quotes multi-word
                tokens; the strings ARE the feature's output.
             */
            AllocationAssertions.AssertDetectsAllocation(
                "provider Tab completion cycle (inserted line strings)",
                () => _terminal.CompleteCommand(true)
            );
            Assert.That(
                _terminal._commandInput.value.StartsWith(
                    ProviderCommandName,
                    StringComparison.Ordinal
                ),
                "Sanity: the provider cycle must apply token completions"
            );
        }

        [UnityTest]
        public IEnumerator SteadyRefreshPassWhileOpenIsAllocationFree()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            RegisterTier();

            for (int i = 0; i < LogCapacity; ++i)
            {
                Terminal.Buffer.HandleLog($"bench log {i}", string.Empty, TerminalLogType.Message);
            }

            _terminal._commandInput.value = SelectiveToken;
            yield return null;
            yield return null;

            AllocationAssertions.AssertZeroAllocations(
                "steady-state refresh pass while open",
                () => _terminal.RefreshUI()
            );
        }

        [UnityTest]
        public IEnumerator SteadyRefreshPassWhileClosedIsAllocationFree()
        {
            yield return SpawnTerminal(HintDisplayMode.Always);
            RegisterTier();
            _terminal._commandInput.value = SelectiveToken;
            yield return null;

            _terminal.Close();
            int frameBudget = 600;
            while (0 < frameBudget-- && !_terminal.IsClosed)
            {
                yield return null;
            }

            Assert.IsTrue(
                _terminal.IsClosed,
                "Sanity: the terminal must settle closed before the pin"
            );
            AllocationAssertions.AssertZeroAllocations(
                "steady-state refresh pass while closed (the pass the idle gate skips)",
                () => _terminal.RefreshUI()
            );
        }

        private IEnumerator SpawnTerminal(HintDisplayMode hintMode)
        {
#if UNITY_EDITOR
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUIAllocation");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.hintDisplayMode = hintMode;
            _terminal._logBufferSize = LogCapacity;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
#else
            Assert.Ignore("TerminalUI allocation pins run in the editor Play Mode suite.");
            yield break;
#endif

            _terminal.SetState(TerminalState.OpenFull);
            yield return null;

            int frameBudget = 600;
            while (
                0 < frameBudget--
                && (
                    _terminal._commandInput == null
                    || _terminal._commandInput.resolvedStyle.display != DisplayStyle.Flex
                )
            )
            {
                yield return null;
            }

            Assert.That(
                _terminal._commandInput != null,
                "The terminal input field should exist after the terminal opens"
            );
        }

        private void RegisterTier()
        {
            for (int i = 0; i < CommandCount; ++i)
            {
                Terminal.Shell.AddCommand($"bench-cmd-{i:D4}", HandleNoop, 1);
            }
        }

        private void RegisterProviderCommand()
        {
            List<string> candidates = new List<string>(ProviderCandidateCount);
            for (int i = 0; i < ProviderCandidateCount; ++i)
            {
                candidates.Add($"candidate-{i:D4}");
            }

            CommandBuilder builder = CommandBuilder
                .Create(ProviderCommandName)
                .Arg<string>(
                    "candidate",
                    spec => spec.Required().Choices(context => candidates, value => value)
                )
                .Handler((context, arguments) => { });
            Assert.IsTrue(
                Terminal.Shell.AddCommand(builder, out _),
                "The provider command should register"
            );
        }
    }
}
