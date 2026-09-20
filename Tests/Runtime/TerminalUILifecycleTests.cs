namespace WallstopStudios.DxCommandTerminal.Tests.Runtime
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
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
        Pins the closed-state lifecycle contract of TerminalUI: the visual
        tree is built on the first open (not on enable), a fully closed
        terminal stops its per-frame UI work, and a re-enabled component
        rebuilds on the next open. The rig uses zero-duration open/close
        animations so the state settles on the next frame regardless of the
        editor's tick rate.
     */
    public sealed class TerminalUILifecycleTests
    {
        private const string PackageRoot = "Packages/com.wallstop-studios.dxcommandterminal";

        private const int FrameBudget = 600;

        private PanelSettings _panelSettings;
        private GameObject _terminalObject;
        private TerminalUI _terminal;
        private readonly List<GameObject> _spawnedObjects = new();

        private static T LoadAsset<T>(string relativePath)
            where T : ScriptableObject
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<T>($"{PackageRoot}/{relativePath}");
#else
            return null;
#endif
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _spawnedObjects.Count - 1; 0 <= i; --i)
            {
                if (_spawnedObjects[i] != null)
                {
                    UnityEngine.Object.Destroy(_spawnedObjects[i]);
                }
            }

            _spawnedObjects.Clear();

            if (_terminalObject != null)
            {
                UnityEngine.Object.Destroy(_terminalObject);
            }

            if (_panelSettings != null)
            {
                UnityEngine.Object.Destroy(_panelSettings);
            }
        }

        [UnityTest]
        public IEnumerator EnableDoesNotBuildUiWhileClosed()
        {
            yield return SpawnTerminalWithDocument();

            Assert.AreEqual(
                0,
                _terminal._uiDocument.rootVisualElement.childCount,
                "A terminal that starts closed must not build its visual tree on enable"
            );
            Assert.That(
                _terminal._commandInput == null,
                "The command input must not exist before the first open"
            );

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Opening the terminal builds and shows the input");

            Assert.AreEqual(
                1,
                _terminal._uiDocument.rootVisualElement.childCount,
                "The terminal root is attached after the first open"
            );
        }

        [UnityTest]
        public IEnumerator ClosedTerminalDefersLogSyncUntilOpen()
        {
            yield return SpawnTerminalWithDocument();

            _terminal.SetState(TerminalState.OpenFull);
            yield return null;

            _terminal.SetState(TerminalState.Closed);
            int frameBudget = 10;
            while (0 < frameBudget-- && !_terminal.IsClosed)
            {
                yield return null;
            }

            Assert.IsTrue(
                _terminal.IsClosed,
                "Sanity: the zero-duration close settles on the next frame"
            );

            Terminal.Log(TerminalLogType.Message, "while-closed");
            frameBudget = 10;
            while (0 < frameBudget--)
            {
                yield return null;
            }

            ScrollView logView = _terminal._uiDocument.rootVisualElement.Q<ScrollView>(
                "LogScrollView"
            );
            Assert.That(logView != null, "Sanity: the log view exists after the open/close cycle");
            Assert.AreEqual(
                0,
                logView.contentContainer.childCount,
                "A fully closed terminal must not sync buffered logs every frame"
            );

            _terminal.SetState(TerminalState.OpenFull);
            frameBudget = FrameBudget;
            while (0 < frameBudget-- && logView.contentContainer.childCount == 0)
            {
                yield return null;
            }

            Assert.AreEqual(
                1,
                logView.contentContainer.childCount,
                "Opening the terminal syncs output logged while it was closed"
            );
            Label loggedLine = logView.contentContainer[0] as Label;
            Assert.AreEqual(
                "while-closed",
                loggedLine?.text,
                "The buffered message must survive the closed window"
            );
        }

        [UnityTest]
        public IEnumerator ReenableRebuildsUiOnNextOpen()
        {
            yield return SpawnTerminalWithDocument();

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Sanity: the terminal builds on the first open");

            _terminal.enabled = false;
            Assert.AreEqual(
                0,
                _terminal._uiDocument.rootVisualElement.childCount,
                "Disabling detaches the terminal tree"
            );

            _terminal.enabled = true;
            Assert.That(
                _terminal._commandInput == null,
                "Re-enabling must not rebuild the visual tree while the terminal stays closed"
            );

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Reopening after re-enable rebuilds the tree");

            Assert.AreEqual(
                1,
                _terminal._uiDocument.rootVisualElement.childCount,
                "The rebuilt terminal root is attached after reopening"
            );
        }

        /*
            A null persisted font is the default component state ("derive
            from the pack" per InitializeFont), not a misconfiguration:
            reopening must not route it through SetFont's null guard. The
            unhandled "[Error] Cannot set null font." on the rebuild fails
            this test; before the fix every reopen after a disable logged it.
         */
        [UnityTest]
        public IEnumerator ReopenWithNullPersistedFontLogsNoError()
        {
            yield return SpawnTerminalWithDocument();

            Assert.That(
                _terminal._persistedFont == null,
                "Sanity: the rig must run with the default null persisted font"
            );

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Sanity: the first open builds the tree");

            _terminal.enabled = false;
            _terminal.enabled = true;

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible(
                "Reopening with a null persisted font must not log an error"
            );

            Font appliedFont = _terminal
                ._uiDocument
                .rootVisualElement
                .style
                .unityFontDefinition
                .value
                .font;
            Assert.AreEqual(
                _terminal.CurrentFont,
                appliedFont,
                "The rebuild must reapply the resolved font definition to the new tree"
            );
        }

        /*
            The first build must render the pack-resolved font, not Unity's
            default OS font: SetupUI's SetFont call runs before
            InitializeFont resolves a pack font and a null write is skipped,
            so the fresh document root carried no font definition until a
            rebuild reapplied it.
         */
        [UnityTest]
        public IEnumerator FirstOpenWithNullPersistedFontAppliesResolvedFont()
        {
            yield return SpawnTerminalWithDocument();

            Assert.That(
                _terminal._persistedFont == null,
                "Sanity: the rig must run with the default null persisted font"
            );

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Sanity: the first open builds the tree");

            Font appliedFont = _terminal
                ._uiDocument
                .rootVisualElement
                .style
                .unityFontDefinition
                .value
                .font;
            Assert.AreEqual(
                _terminal.CurrentFont,
                appliedFont,
                "The first build must apply the pack-resolved font to the document root"
            );
        }

        /*
            The frame the close animation snaps to the closed target is the
            frame the final height (0) and the hidden input display are
            written; a gate evaluated after the snap would skip that write
            and freeze the surface at the last animated height (with a
            zero-duration close, at the full open height).
         */
        [UnityTest]
        public IEnumerator ClosingWritesFinalClosedHeights()
        {
            yield return SpawnTerminalWithDocument();

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Sanity: the terminal is open and laid out");

            _terminal.SetState(TerminalState.Closed);
            int frameBudget = 10;
            while (0 < frameBudget-- && !_terminal.IsClosed)
            {
                yield return null;
            }

            Assert.IsTrue(
                _terminal.IsClosed,
                "Sanity: the zero-duration close settles on the next frame"
            );
            yield return null;

            Assert.AreEqual(
                0f,
                _terminal._uiDocument.rootVisualElement.resolvedStyle.height,
                "The closing frame must write the final closed height to the document root"
            );
            Assert.AreEqual(
                DisplayStyle.None,
                _terminal
                    ._uiDocument.rootVisualElement.Q<VisualElement>("InputContainer")
                    .resolvedStyle.display,
                "The closing frame must hide the input container"
            );
        }

        /*
            On-screen state buttons are the open controls for a closed
            terminal; the opt-in showGUIButtons mode builds its tree eagerly
            on enable so the buttons exist before any open.
         */
        [UnityTest]
        public IEnumerator StateButtonsBuildEagerlyWhileClosed()
        {
            yield return SpawnTerminalWithDocument(showButtons: true);

            VisualElement stateButtons = _terminal._uiDocument.rootVisualElement.Q(
                "StateButtonContainer"
            );
            Assert.AreEqual(
                1,
                _terminal._uiDocument.rootVisualElement.childCount,
                "The showGUIButtons mode builds its tree on enable"
            );
            Assert.That(
                stateButtons != null,
                "The state button container exists while the terminal is closed"
            );
            Assert.AreEqual(
                2,
                stateButtons.childCount,
                "Both state buttons are built while the terminal is closed"
            );
        }

        /*
            An out-of-band rebuild while closed (the editor change hook) can
            leave stale heights and a visible input; the idle gate owes one
            refresh pass after any build before it may skip.
         */
        [UnityTest]
        public IEnumerator OutOfBandRebuildClampsClosedTerminal()
        {
            yield return SpawnTerminalWithDocument();

            _terminal.SetState(TerminalState.OpenFull);
            yield return WaitForInputVisible("Sanity: the terminal is open and laid out");
            _terminal._persistedFont = _terminal.CurrentFont;

            _terminal.SetState(TerminalState.Closed);
            int frameBudget = 10;
            while (0 < frameBudget-- && !_terminal.IsClosed)
            {
                yield return null;
            }

            Assert.IsTrue(_terminal.IsClosed, "Sanity: the terminal settles closed");

            /*
                Toggle the tracked property on, let the change hook rebuild,
                then toggle it back off: the final rebuild runs while closed
                with the idle gate active, so only the initial-refresh latch
                can clamp the stale tree back to the closed state.
             */
            ToggleShowButtonsInInspector(true);
            frameBudget = 10;
            while (0 < frameBudget--)
            {
                yield return null;
            }

            ToggleShowButtonsInInspector(false);
            frameBudget = 600;
            while (
                0 < frameBudget--
                && (
                    _terminal._uiDocument.rootVisualElement.resolvedStyle.height != 0f
                    || _terminal
                        ._uiDocument.rootVisualElement.Q<VisualElement>("InputContainer")
                        .resolvedStyle.display != DisplayStyle.None
                )
            )
            {
                yield return null;
            }

            Assert.AreEqual(
                0f,
                _terminal._uiDocument.rootVisualElement.resolvedStyle.height,
                "The idle gate must clamp an out-of-band rebuild back to the closed height"
            );
            Assert.AreEqual(
                DisplayStyle.None,
                _terminal
                    ._uiDocument.rootVisualElement.Q<VisualElement>("InputContainer")
                    .resolvedStyle.display,
                "The idle gate must hide the input after an out-of-band rebuild"
            );
        }

        /*
            An external SetState before the terminal's Awake (script
            execution order) cannot build yet: the document root does not
            exist on an inactive GameObject, so SetupUI fails with a benign
            logged error instead of throwing, and the next open builds and
            syncs normally.
         */
        [UnityTest]
        public IEnumerator SetStateBeforeAwakeBuildsSafely()
        {
#if !UNITY_EDITOR
            Assert.Ignore("Terminal UI lifecycle coverage runs in the editor Play Mode suite.");
            yield break;
#endif
            LogAssert.Expect(LogType.Error, "No UI root element assigned, cannot setup UI.");
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalSetStateBeforeAwake");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");

            /*
                Awake has not run on the inactive component and the document
                root does not exist yet; opening must not throw.
             */
            Assert.DoesNotThrow(
                () => _terminal.SetState(TerminalState.OpenFull),
                "Opening before Awake must not throw"
            );

            _terminalObject.SetActive(true);
            _terminal.SetState(TerminalState.OpenFull);

            int frameBudget = FrameBudget;
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
                "The next open after activation builds and shows the input"
            );
            Assert.AreEqual(
                DisplayStyle.Flex,
                _terminal._commandInput.resolvedStyle.display,
                "The terminal is usable after the pre-Awake open failed benignly"
            );
        }

        /*
            A terminal destroyed while another live enabled terminal exists
            must hand TerminalUI.Instance to that peer: built-in commands
            resolve UI operations through the static Instance, and a stale
            null degrades them even though a working terminal remains.
         */
        [UnityTest]
        public IEnumerator DestroyingInstanceOwnerHandsInstanceBackToLivePeer()
        {
            yield return SpawnTerminalWithDocument();
            TerminalUI first = _terminal;

            GameObject peerObject = SpawnPeerTerminal("TerminalInstanceOwner");
            yield return new WaitUntil(() => peerObject.GetComponent<StartTracker>().Started);
            TerminalUI second = TerminalUI.Instance;
            Assert.AreNotSame(
                first,
                second,
                "Sanity: the second terminal claims the static Instance"
            );

            UnityEngine.Object.Destroy(second.gameObject);
            int frameBudget = 10;
            while (
                0 < frameBudget-- && (TerminalUI.Instance == null || TerminalUI.Instance == second)
            )
            {
                yield return null;
            }

            Assert.AreSame(
                first,
                TerminalUI.Instance,
                "Destroying the Instance owner must hand Instance to the live peer"
            );
        }

        /*
            With two components forwarding Unity logs, disabling one must not
            strip the survivor's subscription: the delegate occurrence count
            matches the number of attached components, and OnDisable removes
            exactly one.
         */
        [UnityTest]
        public IEnumerator DisablingOneForwarderKeepsTheOtherSubscribed()
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalLogForwarderA");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = false;
            _terminal._logUnityMessages = true;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);

            GameObject peerObject = new GameObject("TerminalLogForwarderB");
            peerObject.SetActive(false);
            TerminalUI peer = peerObject.AddComponent<TerminalUI>();
            peer.resetStateOnInit = false;
            peer._logUnityMessages = true;
            _spawnedObjects.Add(peerObject);
            StartTracker peerTracker = peerObject.AddComponent<StartTracker>();
            peerObject.SetActive(true);
            yield return new WaitUntil(() => peerTracker.Started);

            peer.enabled = false;
            yield return null;

            Debug.Log("unity-forwarded");
            yield return null;

            bool forwarded = false;
            foreach (LogItem entry in Terminal.Buffer.Logs)
            {
                if (string.Equals(entry.message, "unity-forwarded", StringComparison.Ordinal))
                {
                    forwarded = true;
                    break;
                }
            }

            Assert.IsTrue(
                forwarded,
                "Disabling one log forwarder must keep the survivor subscribed"
            );
        }

        /*
            Ownership follows the newest enabled claim: disabling the older
            terminal leaves Instance with the enabled peer, and re-enabling
            the older terminal reclaims Instance (editor property tracking
            gates on Instance, so a reclaimed terminal must re-own it).
         */
        [UnityTest]
        public IEnumerator ReenablingInstanceOwnerReclaimsInstanceFromDisabledPeer()
        {
            yield return SpawnTerminalWithDocument();
            TerminalUI first = _terminal;

            GameObject peerObject = SpawnPeerTerminal("TerminalInstanceReclaim");
            yield return new WaitUntil(() => peerObject.GetComponent<StartTracker>().Started);
            TerminalUI peer = peerObject.GetComponent<TerminalUI>();
            Assert.AreSame(
                peer,
                TerminalUI.Instance,
                "Sanity: the newer enabled terminal owns Instance"
            );

            first.enabled = false;
            yield return null;
            Assert.AreSame(
                peer,
                TerminalUI.Instance,
                "Disabling the older terminal leaves Instance with the enabled peer"
            );

            first.enabled = true;
            yield return null;
            Assert.AreSame(
                first,
                TerminalUI.Instance,
                "Re-enabling the older terminal must reclaim Instance"
            );
        }

        /*
            Session configuration ownership follows the last enabled
            component; disabling that component must restore the newest
            remaining enabled component's configuration, not leave its own
            applied to the shared session.
         */
        [UnityTest]
        public IEnumerator DisablingConfigOwnerRestoresPeerConfig()
        {
            yield return SpawnTerminalWithDocument();
            TerminalUI first = _terminal;

            GameObject peerObject = SpawnPeerTerminal("TerminalConfigOwner", logBufferSize: 32);
            yield return new WaitUntil(() => peerObject.GetComponent<StartTracker>().Started);

            CommandLog buffer = Terminal.Buffer;
            Assert.AreEqual(
                32,
                buffer.Capacity,
                "Sanity: the second component's configuration wins while it is enabled"
            );

            peerObject.GetComponent<TerminalUI>().enabled = false;
            yield return null;

            Assert.AreSame(
                buffer,
                Terminal.Buffer,
                "Restoring the peer configuration must reuse the shared buffer instance"
            );
            Assert.AreEqual(
                first._logBufferSize,
                Terminal.Buffer.Capacity,
                "Disabling the config owner must restore the remaining enabled "
                    + "component's configuration"
            );
            Assert.AreSame(
                first,
                TerminalUI.Instance,
                "Disabling the owner must hand Instance to the remaining enabled terminal"
            );
        }

        /*
            Disable/enable cycles without resetStateOnInit must preserve the
            shared session: the same backend instances, manual registrations,
            buffer contents, history, and configured filters survive.
         */
        [UnityTest]
        public IEnumerator ReenableWithoutResetPreservesSessionState()
        {
            yield return SpawnTerminalWithDocument(resetState: false);

            Assert.IsTrue(
                Terminal.Shell.RunCommand("log"),
                "Sanity: the default 'log' command runs"
            );
            Assert.IsTrue(
                Terminal.Shell.AddCommand(
                    "session-preserved-cmd",
                    _ => Terminal.Log("preserved ran"),
                    minArgs: 0,
                    maxArgs: 0,
                    help: "test"
                ),
                "Sanity: the manual command registers"
            );
            CommandLog buffer = Terminal.Buffer;
            CommandHistory history = Terminal.History;
            CommandShell shell = Terminal.Shell;
            CommandAutoComplete autoComplete = Terminal.AutoComplete;
            int bufferCountBefore = buffer.Logs.Count;
            string[] historyBefore = history
                .GetHistory(onlySuccess: true, onlyErrorFree: true)
                .ToArray();
            Assert.IsNotEmpty(historyBefore, "Sanity: 'log' created a history entry");

            _terminal.enabled = false;
            yield return null;
            _terminal.enabled = true;
            yield return null;

            Assert.AreSame(shell, Terminal.Shell, "The shell is reused without reset");
            Assert.AreSame(buffer, Terminal.Buffer, "The buffer is reused without reset");
            Assert.AreSame(history, Terminal.History, "The history is reused without reset");
            Assert.AreSame(
                autoComplete,
                Terminal.AutoComplete,
                "The auto-complete is reused without reset"
            );
            Assert.AreEqual(
                bufferCountBefore,
                buffer.Logs.Count,
                "Buffer contents survive the disable/enable cycle"
            );
            Assert.IsTrue(
                Terminal.Shell.RunCommand("session-preserved-cmd"),
                "Manual registrations survive the disable/enable cycle"
            );
            Assert.AreEqual(
                bufferCountBefore + 1,
                buffer.Logs.Count,
                "The manual command's output lands in the preserved buffer"
            );
            string[] historyAfter = history
                .GetHistory(onlySuccess: true, onlyErrorFree: true)
                .ToArray();
            Assert.AreEqual(
                historyBefore.Length + 1,
                historyAfter.Length,
                "History entries survive the disable/enable cycle"
            );
            Assert.AreEqual(
                historyBefore[0],
                historyAfter[0],
                "The oldest history entry is preserved in order"
            );
        }

        /*
            With resetStateOnInit, every re-enable cycle recreates the
            backends and reapplies the same configuration: repeated cycles
            must be idempotent (identical command set, no error
            accumulation).
         */
        [UnityTest]
        public IEnumerator ReenableWithResetRecreatesIdempotently()
        {
            yield return SpawnTerminalWithDocument();

            CommandLog buffer1 = Terminal.Buffer;
            CommandShell shell1 = Terminal.Shell;
            shell1.EnsureAutoCommandsRegistered();
            int commandCount1 = shell1.Commands.Count;
            Assert.AreNotEqual(0, commandCount1, "Sanity: default commands register");

            _terminal.enabled = false;
            yield return null;
            _terminal.enabled = true;
            yield return null;

            CommandLog buffer2 = Terminal.Buffer;
            CommandShell shell2 = Terminal.Shell;
            Assert.AreNotSame(buffer1, buffer2, "Reset recreates the buffer");
            Assert.AreNotSame(shell1, shell2, "Reset recreates the shell");
            shell2.EnsureAutoCommandsRegistered();
            Assert.AreEqual(
                commandCount1,
                shell2.Commands.Count,
                "The recreated shell registers the same command set"
            );
            Assert.IsTrue(shell2.RunCommand("help"), "The recreated shell runs commands");

            _terminal.enabled = false;
            yield return null;
            _terminal.enabled = true;
            yield return null;

            CommandShell shell3 = Terminal.Shell;
            Assert.AreNotSame(shell2, shell3, "A second reset cycle recreates the shell again");
            shell3.EnsureAutoCommandsRegistered();
            Assert.AreEqual(
                commandCount1,
                shell3.Commands.Count,
                "Repeated reset cycles stay idempotent: the command set does not drift"
            );
            Assert.IsTrue(
                shell3.RunCommand("help"),
                "Repeated reset cycles keep the shell functional"
            );
        }

        /*
            A terminal whose configuration ignores every discovered command
            stays functional: no commands register, runs fail cleanly, and
            logging and completion keep working.
         */
        [UnityTest]
        public IEnumerator IgnoringEveryCommandKeepsTerminalFunctional()
        {
            List<string> allCommands = CommandShell
                .RegisteredCommands.Value.Select(tuple => tuple.attribute.Name)
                .ToList();
            Assert.IsNotEmpty(allCommands, "Sanity: at least one command is discovered");

            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalAllIgnored");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = true;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal.ignoreDefaultCommands = true;
            _terminal._disabledCommands = new List<string>(allCommands);
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);

            Terminal.Shell.EnsureAutoCommandsRegistered();
            Assert.IsEmpty(
                Terminal.Shell.Commands,
                "Ignoring every discovered command must leave no commands registered"
            );
            Assert.IsFalse(
                Terminal.Shell.RunCommand("help"),
                "A shell with no commands must fail runs cleanly"
            );
            Assert.IsTrue(
                Terminal.Log("still-logging"),
                "Logging keeps working with no commands registered"
            );
            Assert.AreEqual(
                1,
                Terminal.Buffer.Logs.Count,
                "The logged message landed in the buffer"
            );
            List<string> completions = new();
            Terminal.AutoComplete.Complete("he", completions);
            Assert.IsEmpty(
                completions,
                "Completion must return nothing when no commands are registered"
            );
        }

        /*
            Logging between OnEnable and Start lands in the session the
            enable created. With resetStateOnInit, Start's forced refresh
            wipes it; without reset, it survives. Both outcomes are the
            documented reset semantics.
         */
        [UnityTest]
        public IEnumerator LoggingBeforeStartSurvivesWithoutReset()
        {
            yield return LogBeforeStartAndAwaitStart(resetStateOnInit: false);

            /*
                Content-based assert: the buffer is shared and capacity-capped
                (ring wrap keeps Count at Capacity), so absolute or
                baseline-relative counts are suite-order sensitive. Nothing
                logs between the pre-Start write and Start, so the entry is
                the newest one.
             */
            IReadOnlyList<LogItem> logs = Terminal.Buffer.Logs;
            Assert.AreNotEqual(0, logs.Count, "Sanity: the pre-Start log exists");
            Assert.AreEqual(
                "before-start",
                logs[logs.Count - 1].message,
                "A pre-Start log survives Start when resetStateOnInit is off"
            );
        }

        [UnityTest]
        public IEnumerator LoggingBeforeStartIsWipedByForcedReset()
        {
            yield return LogBeforeStartAndAwaitStart(resetStateOnInit: true);
            Assert.AreEqual(
                0,
                Terminal.Buffer.Logs.Count,
                "Start's forced reset wipes a pre-Start log when resetStateOnInit is set"
            );
        }

        /*
            The play-session reset clears every piece of state a previous
            Play Mode session leaves behind (stale static references, the
            shared session's backends, a leaked log callback) and stays
            idempotent on repeat; the next Apply recreates the session.
            Restores the saved backends so later suites keep their session.
         */
        [Test]
        public void PlaySessionResetClearsStaleStaticStateAndIsIdempotent()
        {
            /*
                Seed the shared session so the test is order-independent: a
                fresh domain running only this fixture starts with no
                backends at all.
             */
            if (Terminal.Buffer == null)
            {
                TerminalSession.Current.Apply(
                    new TerminalSession.Config(64, 64, null, null, false),
                    force: false
                );
            }

            CommandLog originalBuffer = Terminal.Buffer;
            CommandHistory originalHistory = Terminal.History;
            CommandShell originalShell = Terminal.Shell;
            CommandAutoComplete originalAutoComplete = Terminal.AutoComplete;
            try
            {
                Assert.That(
                    originalBuffer != null,
                    "Sanity: the shared session has backends before the reset"
                );

                TerminalUI.ResetForNextPlaySession();
                Assert.That(
                    TerminalUI.Instance == null,
                    "The play-session reset must clear the stale static Instance"
                );
                Assert.That(
                    Terminal.Buffer == null,
                    "The play-session reset must drop the session's backends"
                );
                Assert.That(Terminal.History == null, "The history must drop with the session");
                Assert.That(Terminal.Shell == null, "The shell must drop with the session");
                Assert.That(
                    Terminal.AutoComplete == null,
                    "The auto-complete must drop with the session"
                );

                TerminalUI.ResetForNextPlaySession();
                Assert.That(Terminal.Buffer == null, "A repeated play-session reset stays cleared");

                TerminalSession.Current.Apply(
                    new TerminalSession.Config(64, 64, null, null, false),
                    force: false
                );
                Assert.That(
                    Terminal.Buffer != null,
                    "The next Apply recreates the session from the cleared state"
                );
            }
            finally
            {
                Terminal.Buffer = originalBuffer;
                Terminal.History = originalHistory;
                Terminal.Shell = originalShell;
                Terminal.AutoComplete = originalAutoComplete;
            }
        }

        private IEnumerator LogBeforeStartAndAwaitStart(bool resetStateOnInit)
        {
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalEarlyLog");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = resetStateOnInit;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);

            /*
                Awake and OnEnable ran during SetActive; Start has not run
                yet, so this is a pre-Start log.
             */
            Assert.IsTrue(
                Terminal.Log(TerminalLogType.Message, "before-start"),
                "The backends created during OnEnable accept an early log"
            );

            yield return new WaitUntil(() => tracker.Started);
        }

        private GameObject SpawnPeerTerminal(string name, int logBufferSize = 256)
        {
            GameObject peerObject = new(name, typeof(StartTracker), typeof(TerminalUI));
            TerminalUI peer = peerObject.GetComponent<TerminalUI>();
            peer.resetStateOnInit = false;
            peer._logBufferSize = logBufferSize;
            _spawnedObjects.Add(peerObject);
            return peerObject;
        }

        private IEnumerator WaitForInputVisible(string message)
        {
            int frameBudget = FrameBudget;
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
                $"{message}: the command input must exist"
            );
            Assert.AreEqual(
                DisplayStyle.Flex,
                _terminal._commandInput.resolvedStyle.display,
                message
            );
        }

        private void ToggleShowButtonsInInspector(bool value)
        {
            SerializedObject serializedObject = new(_terminal);
            serializedObject.FindProperty(nameof(TerminalUI.showGUIButtons)).boolValue = value;
            serializedObject.ApplyModifiedProperties();
        }

        private IEnumerator SpawnTerminalWithDocument(
            bool showButtons = false,
            bool resetState = true
        )
        {
#if !UNITY_EDITOR
            Assert.Ignore("Terminal UI lifecycle coverage runs in the editor Play Mode suite.");
            yield break;
#endif
            _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            _terminalObject = new GameObject("TerminalUILifecycle");
            _terminalObject.SetActive(false);
            UIDocument document = _terminalObject.AddComponent<UIDocument>();
            document.panelSettings = _panelSettings;
            _terminal = _terminalObject.AddComponent<TerminalUI>();
            _terminal._uiDocument = document;
            _terminal.resetStateOnInit = resetState;
            _terminal.easeOutTime = 0f;
            _terminal.easeInTime = 0f;
            _terminal.showGUIButtons = showButtons;
            _terminal._themePack = LoadAsset<TerminalThemePack>("Packs/Themes/Medium.asset");
            _terminal._fontPack = LoadAsset<TerminalFontPack>("Packs/Fonts/Medium.asset");
            StartTracker tracker = _terminalObject.AddComponent<StartTracker>();
            _terminalObject.SetActive(true);
            yield return new WaitUntil(() => tracker.Started);
        }
    }
}
