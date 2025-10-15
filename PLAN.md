# DXCommandTerminal Modernization Plan

## Objectives
- Eliminate ambient static state and move toward instance-based, SOLID-compliant architecture without sacrificing discoverability or UX ergonomics.
- Achieve zero-allocation hot paths for terminal rendering, history traversal, and input handling while preserving runtime performance.
- Improve maintainability by decomposing the 3.6k line `TerminalUI` monolith and enforcing clearer module boundaries between runtime model, view, persistence, and tooling.
- Tighten correctness through better validation, deterministic behaviour, and comprehensive automated testing (runtime + UI smoke).
- Preserve or improve usability by keeping configuration approachable (consider ScriptableObject assets, inspectors, and presets) and providing guard rails for integrators and designers.

## Prioritized Initiatives

### P0 — Runtime Instance Core & Dependency Inversion ✅
- Replace `WallstopStudios.DxCommandTerminal.Backend.Terminal` static with an injectable `TerminalRuntime` aggregate (shell, history, buffer, autocomplete) created per-terminal instance (`Runtime/CommandTerminal/Backend/Terminal.cs`).
- Introduce an interface (`ITerminalRuntimeAccessor`) that `TerminalUI` and input controllers can depend on; provide a ScriptableObject-derived factory (`TerminalRuntimeProfile`) with serialized capacities, ignored log types, default command packs, etc. to maintain discoverability.
- Provide a migration shim that keeps `Terminal` static available as a thin proxy during transition but mark it `[Obsolete]`, delegating to the active runtime and throwing if none are registered (breaking change acceptable now).
- Benefits: Single Responsibility (runtime logic disentangled from UI), Open/Closed (future runtimes), Dependency Inversion (consumers target abstraction), easier testing/mocking. Enables multi-terminal scenes and editor preview workflows.

**Status:** Implemented. `TerminalRuntime` now owns buffer/history/shell/autocomplete instances per terminal, with runtime caching and profiles in place. Static facade proxies calls to the active runtime.

### P0 — TerminalUI Decomposition & Presenter Layer ✅
- Split `Runtime/CommandTerminal/UI/TerminalUI.cs` (~3.6k LOC) into focused components:
  - `TerminalUIPresenter` (MonoBehaviour) orchestrating runtime ↔ viewmodel sync and command dispatch.
  - `TerminalUIView`/`LogView`, `HistoryView`, `InputView`, `LauncherView` for UIToolkit manipulation (each under 300 LOC) using pure view logic.
  - `TerminalThemeController` handling font/theme switching and coordinating with persistence.
  - `TerminalAnimationController` dedicated to height easing, scroll positioning, fade logic.
- Apply Interface Segregation: each controller exposes minimal update contracts (`ILogView.UpdateLog(LogSlice slice)` etc.). Use composition over inheritance to keep testability high.
- Break editor-only tooling into partial classes or dedicated editor scripts (`Editor/`) to remove `#if UNITY_EDITOR` clutter from runtime components.
- Benefits: maintainability, easier reasoning, smaller diff surface, simpler UI testing.

**Status:** Implemented via partial classes (`TerminalUI.LogView`, `TerminalUI.AutoCompleteView`, `TerminalUI.LayoutView`) and runtime-focused MonoBehaviour core. Editor drawers now rely on an injectable serialized-property accessor.

### P0 — Zero-Allocation Hot Path Audit ✅
- Profile `LateUpdate` (`Runtime/CommandTerminal/UI/TerminalUI.cs:586`), history fade (`ApplyHistoryFade`), autocomplete refresh, and log drain to identify per-frame allocations; replace `new` operations with reusable buffers (`NativeList`, pooled `List<T>`, struct enumerators).
- Introduce `struct`-based lightweight view models (`LogSlice`, `CompletionBufferView`) that carry spans/indices into pooled storage inside `TerminalRuntime` to avoid copying strings each frame.
- Centralize pooling via `DxArrayPool<T>` (already exists) or custom `ITerminalBufferPool`; ensure `CommandLog.DrainPending` and `CommandShell` reuse `StringBuilder` without ToString allocations on hot paths.
- Add allocation regression tests using Unity's `GC.AllocRecorder` in playmode tests that toggle terminal while issuing commands.

**Status:** Allocation regression guard added (`AllocationRegressionTests.CommandLoggingDoesNotAllocate`) capturing GC allocations during command spam and UI toggles. Remaining profiling work tracks via `ProfilerRecorder` hooks if regressions appear.

### P0 — Validation & State Management Contracts
- Formalize state transitions in a dedicated `TerminalStateMachine` with explicit events (`OpenFull`, `Close`, `ToggleLauncher`). `TerminalKeyboardController` then depends on that contract rather than manipulating `TerminalUI` internals.
- Replace ad-hoc boolean flags (`_needsScrollToEnd`, `_commandIssuedThisFrame`) with explicit commands/events queued into the state machine; process deterministically during update.
- Provide defensive checks and central error logging (reduce `Debug.LogError` scatter) to improve correctness and diagnosability.

### P1 — Configurability via ScriptableObjects & Presets
- Create `TerminalAppearanceProfile`, `TerminalInputProfile`, `TerminalCommandProfile` ScriptableObjects living under `Resources/Wallstop Studios/DxCommandTerminal/` to encapsulate current serialized fields (hotkeys, history fade, button labels, etc.).
- Allow multiple profiles per project and expose assignment in inspector with sensible defaults. `TerminalLauncherSettings` becomes a serializable asset reused across scenes.
- Move persisted theme/font selection into a `TerminalThemePersistenceProfile` (ScriptableObject + runtime adapter) to trim IO concerns from `TerminalThemePersister` MonoBehaviour; supports injection/mocking in tests.

**Progress:** `TerminalInputProfile` drives controller bindings (playmode coverage), `TerminalAppearanceProfile` standardises button/hint/history settings, `TerminalCommandProfile` configures ignore lists and disabled commands (with automated tests), and `TerminalThemePersistenceProfile` allows enabling/disabling theme persistence without touching code. Remaining work covers broader persistence APIs beyond themes.

### P1 — UI Rendering & Virtualization Improvements
- Swap manual `VisualElement` management with UIToolkit `ListView` virtualization for history/log to avoid re-creating labels each refresh; ensure zero allocation by providing custom `MakeItem`/`BindItem` that reuse pooled entries.
- Extract USS selectors into modular style sheets under `Styles/` to reduce runtime code toggling class lists; `LogView` can simply set classes based on pre-defined style variants.
- Provide layout data caches and lightweight diffing to avoid clearing/rebuilding containers when nothing changed (`ListsEqual` currently compares single lists but still calls `Clear`/`Add`).

**Progress:** Log rendering now uses a virtualized `ListView` with custom binders (`TerminalUI.LogView`), eliminating per-frame element churn while preserving fade styling. USS modularisation remains to be completed.

### P1 — Input System Stratification
- Introduce an `ITerminalInputSource` abstraction handing parsed commands / navigation intents; implement `LegacyInputSource`, `NewInputSystemSource`, and `EditorShortcutSource`. `TerminalKeyboardController` becomes an adapter composed with an input source chosen via profile.
- Normalize hotkey parsing via dedicated service that pre-resolves key codes at initialization (avoid dictionary lookups each frame) and supports rebinding UI.
- Provide guard rails for conflicting hotkeys (validate at profile load rather than runtime `Debug.LogError`).

**Status:** Phase 1 complete. `TerminalKeyboardController` now resolves any `ITerminalInputTarget`, with new tests verifying interface dispatch and fallback behaviour. Remaining work includes pluggable input sources and hotkey validation services.

### P1 — Persistence & Extensibility
- Redesign persistence to use async-less, job-friendly APIs (no `Task` inside coroutines). Provide `ITerminalPersistenceProvider` interface; default implementation writes JSON via `Unity.Collections.LowLevel.Unsafe.UnsafeUtility` safe wrappers when possible.
- Support scene-level overrides (ScriptableObject) and user-level persistence channels to help multi-terminal scenarios.

### P2 — Observability & Tooling
- Add structured diagnostics (allocation counters, command execution timing) behind development flag to help maintain zero allocation guarantee.
- Provide editor window to inspect active terminal runtimes, registered commands, pending logs (replaces reliance on static global state for debugging).
- Publish developer documentation updates (README + API docs) reflecting new architecture and usage patterns.

**Progress:** Added `Terminal Runtime Inspector` editor window to surface active runtime details (command count, history size, allocation guard status). Remaining work: structured runtime diagnostics beyond the basic view.

## Test Coverage Gaps & Strategy
- **Runtime composition:** Add playmode tests covering multiple terminals instantiated simultaneously with distinct profiles to ensure isolation (new `TerminalRuntime` works).
- **State machine:** Unit tests for `TerminalStateMachine` verifying transitions, animations triggers, and zero allocation command queue behaviour.
- **UI binding:** Introduce UIToolkit integration tests (edit mode with `UIElementsTestUtilities`) validating virtualization binds, theme switching, and launcher metrics (currently untested).
- **Persistence:** Mocked persistence provider tests verifying hydration/save cycles without touching disk; existing `TerminalThemePersister` path can be retired.
- **Input:** Tests per input profile ensuring hotkey translation and conflict detection (no coverage right now for `InputHelpers`). `TerminalKeyboardControllerTests` now validate interface dispatch and fallback to `TerminalUI`.
- **Performance:** Automated allocation guard using `GC.AllocRecorder` around command spam + log scrolling scenario; fail test if allocations exceed threshold.

## Implementation Notes & Sequencing
1. ✅ Land `TerminalRuntime` core + proxy static API (P0). Update tests to inject runtime explicitly.
2. ✅ Extract presenter/view/controller slices from `TerminalUI`, wiring new runtime (P0). Ensure incremental commits keep behaviour parity.
3. ✅ Integrate zero-allocation audit outcomes (P0); add instrumentation and tests.
4. ⏳ Move configuration/persistence into ScriptableObject profiles (P1) and update inspector tooling.
5. ⏳ Roll out input abstraction and UI virtualization (P1), followed by persistence improvements (P1).
6. ⏳ Add observability/tooling features and documentation (P2).

## Risks & Mitigations
- **Backwards compatibility break:** Provide migration guide and temporary proxy static for legacy API. Communicate via `CHANGELOG.md`.
- **Test fragility:** Introduce helper factories for runtimes in tests to keep fixtures concise.
- **Performance regressions:** Run allocation/performance tests per PR; add editor validation to flag accidental LINQ usage (see `linq_hits.txt`).

## Deliverables
- Updated runtime architecture diagrams and README sections describing new profiles and runtime injection.
- Comprehensive `TerminalUI` refactor with modular components, zero-regression tests, and allocation guardrails.
- ScriptableObject-based configuration assets and editor tooling for easier customization.
