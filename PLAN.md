# Terminal Architecture Modernization Plan

## Vision

Deliver a SOLID, dependency-injected terminal stack free of legacy singletons:

- Runtime core (log/history/shell) remains framework-agnostic and receives settings through well-defined interfaces.
- UI/presentation layers bind to runtime via injected services or ScriptableObject registries; no hard-coded `TerminalUI.Instance`.
- Editor tooling manipulates configuration assets rather than reaching into runtime objects.
- Tests target interfaces and profiles directly, eliminating “for tests” hooks.

## Phase 0 – Baseline Audit (1 day)

**Status:** _Working_

### 0.1 Inventory static/singleton entry points ✅

- `Runtime/CommandTerminal/UI/TerminalUI` exposes multiple globals:
  - `Instance`, `ActiveTerminal`, `ActiveTerminals`, plus static provider hooks (`TerminalProvider`, `RuntimeConfigurator`, `InputProvider`, `RuntimeProvider`).
  - Static `UnityLogCallback` registered via `Application.logMessageReceived`.
- `Runtime/CommandTerminal/UI/TerminalRegistry.Default` (and similar `*Proxy.Default` types) bake in lazy singletons for provider interfaces.
- `Runtime/CommandTerminal/Backend/TerminalRuntimeConfig` retains the legacy static configuration surface (`SetMode`, `EditorAutoDiscover`, `TryAutoDiscoverParsers`) with private fallback fields.
- `Runtime/CommandTerminal/Backend/TerminalRuntimeCache` holds pooled `TerminalRuntime` instances via static members.
- Editor-side tooling (`Editor/CustomEditors/TerminalUIEditor`, `Editor/LauncherLayoutDiagnostics`, `Editor/TerminalAssetPackPostProcessor`) declares additional static state and event hooks.
- Utility singletons (array pools, parser instances) are intentionally stateless helpers—note for dependency review but lower priority.

### Next Steps (0.1)

1. Map dependency graph (profiles → runtime → UI/editor) highlighting where Unity-specific code crosses layers.
2. Catalogue other legacy assumptions uncovered during the audit (e.g., `internal` serialized fields, editor accessors) for mitigation planning in later phases.

### 0.2 Legacy coupling observations ✅

- `TerminalUI` exposes many `[SerializeField] internal` members (`_runtime`, `_commandProfile`, `_fontPack`, etc.) relied upon by editor tooling and tests, reinforcing tight coupling.
- Editor scripts (`TerminalUIEditor`, diagnostics utilities) invoke `internal` setters (`SetBlockedCommandsForTests`, `_terminalContainer`, etc.) and manipulate runtime collections directly, bypassing encapsulation.
- `TerminalRuntimeCache` and `TerminalRuntimeConfig` maintain static fallback state that bypasses profile-driven configuration and can reintroduce singleton behaviour even if providers are swapped.
- Tests depend on `ConfigureForTests` helpers and other backdoor APIs, signalling the need for DI-friendly constructors and factories in subsequent phases.

### Next Steps (0.2)

Begin Phase 0.3: produce a lightweight dependency map showing flow between configuration assets, runtime services, UI, and editor tooling to inform Phase 1 refactors.

### 0.3 Dependency Map ✅

#### Configuration Assets / Profiles

- `TerminalRuntimeProfile`, `TerminalCommandProfile`, `TerminalInputProfile`, `TerminalAppearanceProfile`, `TerminalThemePersistenceProfile`.
- Aggregated ad hoc inside `TerminalUI`; editor scripts reach directly into serialized fields.

#### Runtime Core

- `TerminalRuntime` orchestrates `CommandLog`, `CommandHistory`, `CommandShell`, `CommandAutoComplete`.
- `TerminalRuntimeCache` (static) optionally stores runtime instances for reuse.
- `TerminalRuntimeConfig` exposes static configuration (mode flags, parser autodiscovery).

#### Presentation Layer

- `TerminalUI` MonoBehaviour owns serialized references to profiles, runtime containers, input controllers, layout metrics, etc.
- `TerminalKeyboardController`, presenters (`LauncherViewController`, `TerminalUI.LogView`, etc.) are partial classes on `TerminalUI`.
- `TerminalUI` interacts with runtime via static providers (`TerminalRuntimeProvider`, `TerminalRuntimeConfigurator`).

#### Editor Tooling

- `TerminalUIEditor` manipulates `TerminalUI` serialized/internal fields; registers menu items.
- Diagnostics (`LauncherLayoutDiagnostics`, `TerminalRuntimeInspectorWindow`) subscribe to static events and reference `TerminalUI`.
- Asset post-processors create theme/font packs using static lists.

#### Key Flows

1. Scene/Prefab serialized `TerminalUI` references profiles → `TerminalUI` builds `TerminalRuntimeSettings` → calls static providers to configure `TerminalRuntime`.
2. Editor scripts mutate `TerminalUI` internal state directly, bypassing profiles and services.
3. Tests consume `TerminalUI` and runtime via “ForTests” setters, implying tight coupling.

#### Implications

- Phases 1–2 must introduce service factories/DI to isolate runtime from `TerminalUI`.
- Need aggregated configuration asset to replace scattered serialized fields.
- Editor tooling should pivot to profile-focused inspectors once runtime wiring is refactored.

### Next Steps (0.3)

Transition to Phase 1: define interfaces/factories to decouple runtime creation (`ITerminalRuntimeFactory`, `ITerminalSettingsProvider`) and plan removal of static caches/configs.

### Phase 1 Prep – Interface & Seam Identification ✅

#### Static entry points earmarked for replacement

- `TerminalUI.Instance`, `ActiveTerminal`, `ActiveTerminals` → replace with `ITerminalProvider` instances resolved via injected or serialized service registry.
- Provider proxies (`TerminalRegistry.Default`, `TerminalRuntimeConfiguratorProxy.Default`, `TerminalRuntimeProviderProxy.Default`, `TerminalInputProviderProxy.Default`) → convert into ScriptableObject assets or DI registrations that honour per-scene configuration.
- `TerminalRuntimeCache` → remove or hide behind `ITerminalRuntimeFactory` that manages pooling explicitly; eliminate static reuse.
- `TerminalRuntimeConfig` static surface → wrap in `ITerminalRuntimeConfigurator` backed by configuration assets; deprecate global fallback fields.
- Editor diagnostics hooking static events (`LauncherLayoutDiagnostics`, `TerminalRuntimeInspectorWindow`) → rework to subscribe via provider interfaces once DI is in place.

#### Proposed interface seams

- `ITerminalSettingsProvider`: supplies immutable runtime settings (`TerminalRuntimeSettings`) per terminal instance; implemented by a new `TerminalConfigurationAsset` or `TerminalConfigurationComponent`.
- `ITerminalRuntimeFactory`: responsible for creating/configuring `ITerminalRuntime` given an `ITerminalSettingsProvider` plus optional services (logging, time, diagnostics).
- `ITerminalServiceLocator`: ScriptableObject/MonoBehaviour that exposes configured providers to both runtime and editor tooling (small DI container).
- `ITerminalLog`, `ITerminalShell`, `ITerminalHistory` (optional wrappers) to hide concrete types if further decoupling required.

#### Separation plan

- Phase 1.1: Introduce `ITerminalSettingsProvider` + `TerminalRuntimeFactory` alongside existing static creation. TerminalUI swaps to new factory; static pathways left for backward compat temporarily.
- Phase 1.2: Refactor provider proxies to resolve from `ITerminalServiceLocator`; deprecate static `Default` singletons.
- Phase 1.3: Remove `TerminalRuntimeCache` & static config fallbacks, ensuring tests use factories rather than backdoor helpers.

### Next Steps (Phase 1 Prep)

- Kick off Phase 1.1 by scaffolding `ITerminalSettingsProvider` and `TerminalRuntimeFactory` interfaces/classes.
- Update tests to construct runtime via the new factory to validate the seam before removing static singletons.

## Phase 1 – Core Runtime Decoupling (3–4 days)

1. Extract interfaces:
   - `ITerminalRuntimeFactory`, `ITerminalLog`, `ITerminalShell`.
   - `ITerminalSettingsProvider` (wrap `TerminalRuntimeSettings` / profiles).
2. Convert `TerminalRuntime`/friends to pure C# services:
   - Constructors accept `ITerminalSettingsProvider` and optional dependencies (`ITimeProvider`, `ILogger`).
   - Replace scratch lists with pooled structures or dedicated `FilterState` structs.
3. Introduce DI-friendly bootstrapper (ScriptableObject or MonoBehaviour) that instantiates runtime services per terminal instance.

## Phase 2 – UI Presenter Refactor (4–5 days)

1. Split `TerminalUI` into:
   - `TerminalView` (MonoBehaviour managing UIDocument bindings).
   - `TerminalController` (non-Mono class handling mode transitions, animations, fade logic).
   - `TerminalServiceConnector` (wires controller to DI container).
2. Replace static `TerminalUI.Instance` with:
   - `ITerminalProvider` registered via serialized reference (ScriptableObject asset or Scene service locator).
   - Views request providers via serialized field or `FindObjectOfType<TerminalProvider>()` (temporary).
3. Update inspector/editor scripts to work against profiles and view components; remove direct field access to runtime internals.

## Phase 3 – Configuration & Profiles (2–3 days)

1. Formalize “chunks”:
   - `TerminalRuntimeProfile` handles capacities + filters.
   - `TerminalInputProfile`, `TerminalAppearanceProfile`, `TerminalThemePersistenceProfile` already exist; add `TerminalAnimationProfile` if needed.
2. Create a `TerminalConfigurationAsset` ScriptableObject aggregating active profiles; `TerminalServiceConnector` consumes this.
3. Provide migration tool/editor menu: convert legacy serialized fields to new asset references.

## Phase 4 – Editor Tooling Overhaul (3 days)

1. Replace `TerminalUIEditor` with:
   - Profile-specific custom editors (`TerminalRuntimeProfileEditor`, `TerminalCommandProfileEditor`).
   - Lightweight `TerminalViewEditor` for layout/preview controls only.
2. Implement runtime dashboards (e.g., `TerminalRuntimeInspector`) that operate via public interfaces rather than internal fields.
3. Update diagnostics toggles/logging to route through service interfaces.

## Phase 5 – Testing & CI Updates (2 days)

1. Rewrite unit tests to construct runtime services via interfaces and test fixtures.
2. Introduce integration tests for:
   - Dependency injection end-to-end (instantiate TerminalConfigurationAsset, spawn TerminalView, assert behaviours).
   - Editor-time profile editing (using UnityEditor tests).
3. Update CI scripts to run new test suites; ensure coverage remains high.

## Phase 6 – Cleanup & Release Prep (1–2 days)

1. Remove deprecated `*ForTests` APIs and obsolete fields.
2. Update README/CHANGELOG to reflect new architecture and migration path.
3. Provide upgrade guide for users migrating from singleton-based access to service-based approach.

## Sequencing Notes

- Phase 1 dependency: None (can start immediately).
- Phase 2 depends on Phase 1 interfaces being available.
- Phase 3 requires Phase 2 connectors to consume new configuration asset.
- Phases 4 and 5 can run in parallel once phases 1–3 stabilize.
- Each phase should conclude with regression tests + documentation updates.

### Phase 1.1 Progress (Completed)

- Introduced ITerminalSettingsProvider and ITerminalRuntimeFactory interfaces, plus default TerminalRuntimeFactory implementation.
- Added TerminalConfigurationAsset ScriptableObject wrapping existing runtime profile to bridge new provider pipeline.
- TerminalUI now acquires runtime instances through ResolveRuntimeFactory/SettingsProvider, defaulting to new factory while keeping TerminalRuntimeCache compatibility.
- Updated serialized state to track allowed/blocked command/log lists accordingly.
- Added test-only seams on TerminalUI so runtime factories can be injected and settings provider selection can be observed.
- Extended playmode tests to validate configuration-asset, runtime-profile, and default-provider paths; tear-down now cleans configuration assets to isolate cases.

### Phase 1.2 Progress (In Progress)

- Introduced ITerminalServiceLocator with default and mutable implementations to broker provider access.
- TerminalUI now resolves ITerminalProvider/ITerminalRuntimeConfigurator/ITerminalInputProvider/ITerminalRuntimeProvider through the locator, keeping legacy static setters as thin shims.
- Added runtime tests verifying locator overrides and mutable fallbacks so future DI work has safety nets.
- Added TerminalServiceBindingAsset ScriptableObject plus a scene-level TerminalServiceBindingComponent so bindings can be serialized or applied automatically. TerminalUI now falls back to the global TerminalServiceBindingSettings default when no local binding is provided.
- TerminalUIEditor now provisions service binding/settings assets, attaches a binding component on the same prefab, and exposes inspector summaries with quick navigation so dependency wiring is visible and consistent by default.
- Editor diagnostics now read runtime data through the locator-backed scope, and runtime provider/scope abstractions no longer depend on the legacy `Terminal` static (now marked obsolete).
- Built-in commands now consume the locator-backed runtime scope (`ITerminalServiceLocator` + `ITerminalRuntimeScope`) instead of hitting `Terminal.*` singletons directly, paving the way for Editor/runtime tooling migration.
- Drafted ITerminalRuntimeScope and ITerminalRuntimeConfiguratorService interfaces with default adapters so existing static surfaces can be composed through the locator. TerminalUI now delegates runtime registration/logging to the scoped service.
- TerminalUI lifecycle now clears and returns runtime instances through the locator-provided `ITerminalRuntimePool`, only falling back to the legacy cache when no pool is present so existing overrides continue to function.
- TerminalRuntimeScope now bridges registration back into the deprecated `Terminal` facade so existing editor tooling and tests relying on static access remain functional while new consumers adopt the locator.
- Static dependency audit highlights remaining targets:
  - `Terminal` static facade still mirrors runtime state; we need to migrate remaining direct callers onto the locator-driven scope and ultimately remove the facade.
  - `TerminalRuntimeConfig` retains static setters/getters plus fallback fields; `TerminalRuntimeConfiguratorProxy` depends on these and will need a service-backed configuration asset.
  - `TerminalRuntimeCache` now serves purely as a compatibility fallback; once pool-backed tests land we can remove the remaining references.
  - Editor utilities (`TerminalUIEditor`, diagnostics windows) set/reset `TerminalUI.Instance` and static providers directly; they should be updated to request the locator or serialized references instead.
- Runtime test shims now expose the locator-provided runtime pool (`StubRuntimePool`) so the suite compiles against the expanded interface surface and continues validating locator overrides.
- Playmode harness (`TestRuntimeScope`) now routes launcher UI assertions through `TerminalUI`'s existing test hooks instead of relying on internal/runtime scope knowledge, keeping coverage intact while respecting the new locator boundaries.
- Appearance-profile assertions in playmode tests now pull history fade targets and Unity-log capture status through `TerminalUI`'s public test hooks, eliminating the last direct scope-only reads in the harness and aligning them with the locator-driven architecture.
- Service locator regression tests inject stub runtime pools and assert binding overrides against the asset itself, ensuring the expanded interface surface and binding workflow stay covered by unit tests.

#### TerminalRuntimeCache Retirement Draft

- ✅ Introduce an `ITerminalRuntimePool` abstraction exposed via the service locator so runtime reuse becomes an injectable concern.
- ✅ Provide a default pool implementation that mirrors current cache semantics while honouring scene/prefab overrides.
- ✅ Update `TerminalUI` (and tests) to obtain runtimes from the pool rather than the static cache, ensuring deterministic lifetimes.
- Mark `TerminalRuntimeCache` obsolete with a transition shim delegating to the pool, then remove once all call sites migrate.
- Add regression tests covering pooled reuse/disposal scenarios to guard against regressions when the cache is retired.

Next:

- Capture runtime pooling behaviour in playmode tests (rent/return cycle, reset clearing).
- Migrate remaining `Terminal.*` call sites to request `ITerminalRuntimeScope` from the service locator so the facade can be retired safely.
- Update configurator proxies and editor tooling to consume locator-provided services instead of static fallbacks.
- Plan removal window for `TerminalRuntimeCache` fallback once consumers and tests migrate.
- Audit remaining test helpers for `Terminal` static usage and transition them to locator-backed access to unblock facade removal.
- Extend editor tooling coverage to exercise the binding asset/component flow, validating the new pool injection path in the editor pipeline.
